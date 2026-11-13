using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MimeKit;
using Raffa.Renewals.Application.Outreach;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// Resend's webhook signature (Svix scheme): HMAC-SHA256 over <c>{svix-id}.{svix-timestamp}.{body}</c> with the
/// base64 secret after <c>whsec_</c>; the <c>svix-signature</c> header lists <c>v1,&lt;base64&gt;</c> values.
/// Timestamps older or newer than five minutes are refused (replay).
/// </summary>
internal static class SvixSignature
{
    public static bool Verify(string? secret, string? id, string? timestamp, string? signatures, string body, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(signatures))
        {
            return false;
        }

        if (!long.TryParse(timestamp, out var ts) || Math.Abs(now.ToUnixTimeSeconds() - ts) > 300)
        {
            return false;
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(secret.StartsWith("whsec_", StringComparison.Ordinal) ? secret[6..] : secret);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(id + "." + timestamp + "." + body));
        foreach (var part in signatures.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var comma = part.IndexOf(',', StringComparison.Ordinal);
            if (comma < 0 || part[..comma] != "v1")
            {
                continue;
            }

            try
            {
                if (CryptographicOperations.FixedTimeEquals(expected, Convert.FromBase64String(part[(comma + 1)..])))
                {
                    return true;
                }
            }
            catch (FormatException)
            {
                // A malformed entry: try the next one.
            }
        }

        return false;
    }

    public static string Sign(string secret, string id, string timestamp, string body)
    {
        var key = Convert.FromBase64String(secret.StartsWith("whsec_", StringComparison.Ordinal) ? secret[6..] : secret);
        return "v1," + Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(id + "." + timestamp + "." + body)));
    }
}

/// <summary>Reads a received email from Resend: the raw MIME message when offered, else rebuilt from its parts.</summary>
internal interface IInboundMailFetcher
{
    /// <returns>The raw message and the recipients Resend reports, or null when it cannot be read.</returns>
    Task<(byte[] Raw, IReadOnlyList<string> Recipients)?> FetchAsync(string emailId, CancellationToken ct);
}

internal sealed class ResendInboundMailFetcher(IHttpClientFactory httpClientFactory, CaseMailOptions options) : IInboundMailFetcher
{
    public const string HttpClientName = "resend";

    public async Task<(byte[] Raw, IReadOnlyList<string> Recipients)?> FetchAsync(string emailId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Resend.ApiKey))
        {
            return null;
        }

        var http = httpClientFactory.CreateClient(HttpClientName);
        var baseUrl = options.Resend.BaseUrl.TrimEnd('/');
        var email = await GetJsonAsync(http, baseUrl + "/emails/receiving/" + Uri.EscapeDataString(emailId), ct).ConfigureAwait(false);
        if (email is not { } e)
        {
            return null;
        }

        var recipients = Addresses(e, "to").Concat(Addresses(e, "cc")).Concat(Addresses(e, "bcc")).ToList();
        var rawUrl = e.TryGetProperty("raw", out var raw) && raw.ValueKind == JsonValueKind.Object ? Str(raw, "download_url") : Str(e, "raw_download_url");
        if (rawUrl is not null)
        {
            using var response = await http.GetAsync(rawUrl, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return (await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false), recipients);
            }
        }

        // No raw message: rebuild one from the text, the HTML and the attachments.
        var message = new MimeMessage();
        if (Str(e, "from") is { } from && MailboxAddress.TryParse(from, out var fromAddress))
        {
            message.From.Add(fromAddress);
        }

        foreach (var to in recipients.Where(r => MailboxAddress.TryParse(r, out _)))
        {
            message.To.Add(MailboxAddress.Parse(to));
        }

        message.Subject = Str(e, "subject") ?? string.Empty;
        if (Str(e, "created_at") is { } created && DateTimeOffset.TryParse(created, out var date))
        {
            message.Date = date;
        }

        var body = new BodyBuilder { TextBody = Str(e, "text"), HtmlBody = Str(e, "html") };
        var list = await GetJsonAsync(http, baseUrl + "/emails/receiving/" + Uri.EscapeDataString(emailId) + "/attachments", ct).ConfigureAwait(false);
        if (list is { } l && l.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in data.EnumerateArray())
            {
                if (Str(a, "download_url") is not { } url)
                {
                    continue;
                }

                using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                    body.Attachments.Add(Str(a, "filename") ?? "attachment", bytes,
                        ContentType.TryParse(Str(a, "content_type") ?? "application/octet-stream", out var type) ? type : new ContentType("application", "octet-stream"));
                }
            }
        }

        message.Body = body.ToMessageBody();
        using var stream = new MemoryStream();
        message.WriteTo(stream);
        return (stream.ToArray(), recipients);
    }

    private async Task<JsonElement?> GetJsonAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Resend.ApiKey);
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).RootElement.Clone();
    }

    internal static IEnumerable<string> Addresses(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.Array => v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!),
                JsonValueKind.String => [v.GetString()!],
                _ => [],
            }
            : [];

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>
/// <c>POST /api/webhooks/resend</c> (anonymous, signed): Resend's <c>email.received</c> event. The email is read
/// (raw MIME), its new text and attachment names extracted, and handed to <see cref="CaseMailService.ReceiveAsync"/>
/// which matches the case address, stores it as pending and notifies the owner. Always 200 for a verified
/// delivery (an unknown address is not an error worth Resend's retries); 401 for a bad signature.
/// </summary>
internal static class ResendWebhookEndpoint
{
    private const long MaxBodyBytes = 1024 * 1024;

    public static IEndpointRouteBuilder MapResendWebhook(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/webhooks/resend", HandleAsync);
        return endpoints;
    }

    internal static async Task<IResult> HandleAsync(
        HttpRequest request, CaseMailOptions options, IInboundMailFetcher fetcher, CaseMailService mail, ILoggerFactory loggers, CancellationToken ct)
    {
        var logger = loggers.CreateLogger("Raffa.Api.ResendWebhook");
        if (request.ContentLength is > MaxBodyBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        string body;
        using (var reader = new StreamReader(request.Body, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }

        if (!SvixSignature.Verify(options.Resend.WebhookSecret, request.Headers["svix-id"], request.Headers["svix-timestamp"], request.Headers["svix-signature"], body, DateTimeOffset.UtcNow))
        {
            return Results.Unauthorized();
        }

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(body).RootElement;
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        if (root.TryGetProperty("type", out var type) && type.GetString() != "email.received")
        {
            return Results.Ok();
        }

        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("email_id", out var idElement) || idElement.GetString() is not { Length: > 0 } emailId)
        {
            return Results.BadRequest();
        }

        var fetched = await fetcher.FetchAsync(emailId, ct).ConfigureAwait(false);
        if (fetched is not { } f)
        {
            logger.LogWarning("Inbound email {EmailId} could not be read from Resend", emailId);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); // Resend retries.
        }

        var recipients = ResendInboundMailFetcher.Addresses(data, "to")
            .Concat(ResendInboundMailFetcher.Addresses(data, "cc"))
            .Concat(ResendInboundMailFetcher.Addresses(data, "bcc"))
            .Concat(f.Recipients)
            .ToList();
        var read = CaseEmailFileReader.Read("message.eml", f.Raw, 20L * 1024 * 1024);
        string from = string.Empty;
        string subject = string.Empty;
        DateTimeOffset received = DateTimeOffset.UtcNow;
        try
        {
            using var stream = new MemoryStream(f.Raw, writable: false);
            var message = MimeMessage.Load(stream);
            from = message.From.Mailboxes.FirstOrDefault()?.ToString() ?? string.Empty;
            subject = message.Subject ?? string.Empty;
            recipients.AddRange(message.To.Mailboxes.Concat(message.Cc.Mailboxes).Select(m => m.Address));
            if (message.Date != DateTimeOffset.MinValue)
            {
                received = message.Date;
            }
        }
        catch (FormatException)
        {
            // Not a MIME message: keep what the webhook said.
        }

        var outcome = await mail.ReceiveAsync(new InboundMail(
            "resend",
            emailId,
            recipients,
            from.Length > 0 ? from : (data.TryGetProperty("from", out var fe) ? fe.GetString() ?? string.Empty : string.Empty),
            subject.Length > 0 ? subject : (data.TryGetProperty("subject", out var se) ? se.GetString() ?? string.Empty : string.Empty),
            received,
            read?.Text ?? string.Empty,
            read?.Attachments.Select(a => a.FileName).ToList() ?? [],
            f.Raw), ct).ConfigureAwait(false);
        logger.LogInformation("Inbound email {EmailId}: {Result}", emailId, outcome.Result);
        return Results.Ok(new { result = outcome.Result });
    }
}
