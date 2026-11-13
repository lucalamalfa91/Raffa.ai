using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MimeKit;
using Raffa.Renewals.Application.Outreach;

namespace Raffa.Api.Infrastructure;

/// <summary>Builds the MIME message of an <see cref="OutgoingMail"/> (plain text, attachments).</summary>
internal static class OutgoingMime
{
    public static MimeMessage Build(OutgoingMail mail)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(mail.FromName ?? string.Empty, mail.FromAddress));
        foreach (var to in mail.To)
        {
            message.To.Add(MailboxAddress.Parse(to));
        }

        foreach (var cc in mail.Cc)
        {
            message.Cc.Add(MailboxAddress.Parse(cc));
        }

        foreach (var replyTo in mail.ReplyTo)
        {
            message.ReplyTo.Add(MailboxAddress.Parse(replyTo));
        }

        message.Subject = mail.Subject;
        var body = new BodyBuilder { TextBody = mail.Body };
        foreach (var a in mail.Attachments)
        {
            body.Attachments.Add(a.FileName, a.Bytes, ContentType.TryParse(a.MimeType, out var type) ? type : new ContentType("application", "octet-stream"));
        }

        message.Body = body.ToMessageBody();
        return message;
    }

    public static byte[] Bytes(OutgoingMail mail)
    {
        using var stream = new MemoryStream();
        Build(mail).WriteTo(stream);
        return stream.ToArray();
    }
}

/// <summary>Shared OAuth plumbing of the two providers.</summary>
internal abstract class OAuthMailboxProvider(IHttpClientFactory httpClientFactory, OAuthClientOptions client) : IMailboxProvider
{
    public const string HttpClientName = "mailbox";

    public abstract string Name { get; }

    public bool IsConfigured => client.IsConfigured;

    protected abstract string AuthorizeEndpoint { get; }

    protected abstract string TokenEndpoint { get; }

    protected abstract string Scopes { get; }

    protected HttpClient Http => httpClientFactory.CreateClient(HttpClientName);

    protected OAuthClientOptions Client => client;

    public virtual string AuthorizeUrl(string state, string redirectUri, string? loginHint)
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = client.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["scope"] = Scopes,
            ["state"] = state,
            ["login_hint"] = string.IsNullOrWhiteSpace(loginHint) ? null : loginHint,
        };
        foreach (var (k, v) in ExtraAuthorizeParameters)
        {
            query[k] = v;
        }

        return AuthorizeEndpoint + "?" + string.Join("&", query.Where(kv => kv.Value is not null).Select(kv => kv.Key + "=" + Uri.EscapeDataString(kv.Value!)));
    }

    protected virtual IEnumerable<(string, string)> ExtraAuthorizeParameters => [];

    /// <summary>Calls the token endpoint; a refused grant is a <see cref="MailboxSendException"/> asking to reconnect.</summary>
    protected async Task<JsonElement> TokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        form["client_id"] = client.ClientId!;
        form["client_secret"] = client.ClientSecret!;
        using var response = await Http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var reconnect = json.Contains("invalid_grant", StringComparison.Ordinal) || response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized;
            throw new MailboxSendException("The mailbox provider refused the token request (" + (int)response.StatusCode + ").", reconnect);
        }

        return JsonDocument.Parse(json).RootElement.Clone();
    }

    protected static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public abstract Task<MailboxGrant> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default);

    public abstract Task<string?> SendAsync(string refreshToken, OutgoingMail mail, CancellationToken cancellationToken = default);

    protected static async Task EnsureSentAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        _ = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new MailboxSendException("The mailbox refused the message (" + (int)response.StatusCode + ").", response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    }
}

/// <summary>
/// Microsoft 365 / Outlook (plan F2): a multi-tenant Entra app (<c>CaseMail__Microsoft__ClientId/ClientSecret</c>)
/// with the delegated <c>Mail.Send</c> and <c>User.Read</c> permissions — Raffa can send as the person, never
/// read their mailbox. Sends the MIME message through Graph <c>/me/sendMail</c>, which keeps it in Sent Items.
/// </summary>
internal sealed class MicrosoftMailboxProvider(IHttpClientFactory httpClientFactory, CaseMailOptions options)
    : OAuthMailboxProvider(httpClientFactory, options.Microsoft)
{
    private const string Graph = "https://graph.microsoft.com/v1.0";

    public override string Name => "microsoft";

    protected override string AuthorizeEndpoint => "https://login.microsoftonline.com/common/oauth2/v2.0/authorize";

    protected override string TokenEndpoint => "https://login.microsoftonline.com/common/oauth2/v2.0/token";

    protected override string Scopes => "offline_access openid email profile https://graph.microsoft.com/User.Read https://graph.microsoft.com/Mail.Send";

    protected override IEnumerable<(string, string)> ExtraAuthorizeParameters => [("response_mode", "query"), ("prompt", "select_account")];

    public override async Task<MailboxGrant> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
    {
        var token = await TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["scope"] = Scopes,
        }, cancellationToken).ConfigureAwait(false);
        var refresh = Str(token, "refresh_token") ?? throw new MailboxSendException("Microsoft returned no refresh token.", true);
        using var request = new HttpRequestMessage(HttpMethod.Get, Graph + "/me?$select=mail,userPrincipalName,displayName");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Str(token, "access_token"));
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new MailboxSendException("Microsoft did not return the mailbox address.", true);
        }

        var me = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).RootElement;
        var address = Str(me, "mail") ?? Str(me, "userPrincipalName") ?? throw new MailboxSendException("This Microsoft account has no mailbox.", true);
        return new MailboxGrant(refresh, address, Str(me, "displayName"));
    }

    public override async Task<string?> SendAsync(string refreshToken, OutgoingMail mail, CancellationToken cancellationToken = default)
    {
        var token = await TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["scope"] = Scopes,
        }, cancellationToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Post, Graph + "/me/sendMail")
        {
            // Graph accepts a MIME message as base64 text: To, CC, Reply-To and attachments as composed.
            Content = new StringContent(Convert.ToBase64String(OutgoingMime.Bytes(mail)), Encoding.ASCII, "text/plain"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Str(token, "access_token"));
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSentAsync(response, cancellationToken).ConfigureAwait(false);
        return Str(token, "refresh_token");
    }
}

/// <summary>
/// Google Workspace / Gmail (plan F2): an OAuth web client (<c>CaseMail__Google__ClientId/ClientSecret</c>) with
/// the <c>gmail.send</c> scope only (send, never read). Sends the raw MIME message through
/// <c>users.messages.send</c>, which keeps it in the person's Sent folder.
/// </summary>
internal sealed class GoogleMailboxProvider(IHttpClientFactory httpClientFactory, CaseMailOptions options)
    : OAuthMailboxProvider(httpClientFactory, options.Google)
{
    public override string Name => "google";

    protected override string AuthorizeEndpoint => "https://accounts.google.com/o/oauth2/v2/auth";

    protected override string TokenEndpoint => "https://oauth2.googleapis.com/token";

    protected override string Scopes => "openid email profile https://www.googleapis.com/auth/gmail.send";

    protected override IEnumerable<(string, string)> ExtraAuthorizeParameters => [("access_type", "offline"), ("prompt", "consent"), ("include_granted_scopes", "true")];

    public override async Task<MailboxGrant> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
    {
        var token = await TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
        }, cancellationToken).ConfigureAwait(false);
        var refresh = Str(token, "refresh_token") ?? throw new MailboxSendException("Google returned no refresh token.", true);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Str(token, "access_token"));
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new MailboxSendException("Google did not return the mailbox address.", true);
        }

        var me = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).RootElement;
        var address = Str(me, "email") ?? throw new MailboxSendException("This Google account has no email address.", true);
        return new MailboxGrant(refresh, address, Str(me, "name"));
    }

    public override async Task<string?> SendAsync(string refreshToken, OutgoingMail mail, CancellationToken cancellationToken = default)
    {
        var token = await TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        }, cancellationToken).ConfigureAwait(false);
        var raw = Convert.ToBase64String(OutgoingMime.Bytes(mail)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://gmail.googleapis.com/gmail/v1/users/me/messages/send")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { raw }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Str(token, "access_token"));
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSentAsync(response, cancellationToken).ConfigureAwait(false);
        return Str(token, "refresh_token");
    }
}
