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

    public abstract Task<SentMail> SendAsync(string accessToken, OutgoingMail mail, CancellationToken cancellationToken = default);

    public async Task<MailboxToken> AccessTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken };
        if (SendScopeOnRefresh)
        {
            form["scope"] = Scopes;
        }

        var token = await TokenAsync(form, cancellationToken).ConfigureAwait(false);
        return new MailboxToken(
            Str(token, "access_token") ?? throw new MailboxSendException("The provider returned no access token.", true),
            Str(token, "refresh_token"),
            Str(token, "scope"));
    }

    /// <summary>Microsoft wants the scopes again on a refresh; Google does not.</summary>
    protected virtual bool SendScopeOnRefresh => false;

    protected HttpRequestMessage Bearer(HttpMethod method, string url, string accessToken, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

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
/// Microsoft 365 / Outlook (plan F2/F3): the multi-tenant Entra app <c>raffa-&lt;env&gt;-mailbox</c>
/// (<c>CaseMail__Microsoft__ClientId/ClientSecret</c>, created by Terraform). Delegated Graph permissions:
/// <c>Mail.Send</c> and <c>Mail.Read</c> (send, and read the replies of the conversations Raffa started — the
/// reader asks Graph for one conversation id at a time, never lists the mailbox), <c>User.Read</c>, and
/// <c>Team.ReadBasic.All</c>, <c>Channel.ReadBasic.All</c>, <c>ChannelMessage.Send</c> for notifications to a
/// Teams channel the person chooses. A message is sent as a draft created from the MIME message and then sent,
/// so its conversation id is known; it stays in the person's Sent Items.
/// </summary>
internal sealed class MicrosoftMailboxProvider(IHttpClientFactory httpClientFactory, CaseMailOptions options)
    : OAuthMailboxProvider(httpClientFactory, options.Microsoft)
{
    private const string Graph = "https://graph.microsoft.com/v1.0";

    public override string Name => "microsoft";

    protected override string AuthorizeEndpoint => "https://login.microsoftonline.com/common/oauth2/v2.0/authorize";

    protected override string TokenEndpoint => "https://login.microsoftonline.com/common/oauth2/v2.0/token";

    protected override string Scopes =>
        "offline_access openid email profile https://graph.microsoft.com/User.Read https://graph.microsoft.com/Mail.Send https://graph.microsoft.com/Mail.Read " +
        "https://graph.microsoft.com/Team.ReadBasic.All https://graph.microsoft.com/Channel.ReadBasic.All https://graph.microsoft.com/ChannelMessage.Send";

    protected override bool SendScopeOnRefresh => true;

    protected override IEnumerable<(string, string)> ExtraAuthorizeParameters => [("response_mode", "query"), ("prompt", "select_account")];

    private static bool Has(string? scopes, string scope) =>
        scopes is not null && scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(s => s.EndsWith("/" + scope, StringComparison.OrdinalIgnoreCase) || s.Equals(scope, StringComparison.OrdinalIgnoreCase));

    public bool CanReceive(string? scopes) => Has(scopes, "Mail.Read");

    public bool CanPostToTeams(string? scopes) => Has(scopes, "ChannelMessage.Send") && Has(scopes, "Team.ReadBasic.All") && Has(scopes, "Channel.ReadBasic.All");

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
        using var request = Bearer(HttpMethod.Get, Graph + "/me?$select=mail,userPrincipalName,displayName", Str(token, "access_token") ?? string.Empty);
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new MailboxSendException("Microsoft did not return the mailbox address.", true);
        }

        var me = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).RootElement;
        var address = Str(me, "mail") ?? Str(me, "userPrincipalName") ?? throw new MailboxSendException("This Microsoft account has no mailbox.", true);
        return new MailboxGrant(refresh, address, Str(me, "displayName"), Str(token, "scope"));
    }

    public override async Task<SentMail> SendAsync(string accessToken, OutgoingMail mail, CancellationToken cancellationToken = default)
    {
        // A draft from the MIME message (To, CC, Reply-To, attachments as composed), then send it: the draft
        // tells the conversation id the supplier's replies will join.
        using var create = Bearer(HttpMethod.Post, Graph + "/me/messages", accessToken,
            new StringContent(Convert.ToBase64String(OutgoingMime.Bytes(mail)), Encoding.ASCII, "text/plain"));
        using var created = await Http.SendAsync(create, cancellationToken).ConfigureAwait(false);
        await EnsureSentAsync(created, cancellationToken).ConfigureAwait(false);
        var draft = JsonDocument.Parse(await created.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).RootElement;
        var id = Str(draft, "id") ?? throw new MailboxSendException("Microsoft did not create the message.", false);
        using var send = Bearer(HttpMethod.Post, Graph + "/me/messages/" + Uri.EscapeDataString(id) + "/send", accessToken, new StringContent(string.Empty));
        using var sent = await Http.SendAsync(send, cancellationToken).ConfigureAwait(false);
        await EnsureSentAsync(sent, cancellationToken).ConfigureAwait(false);
        return new SentMail(Str(draft, "conversationId"), Str(draft, "internetMessageId"));
    }

    public async Task<IReadOnlyList<MailboxMessage>> ConversationAsync(string accessToken, string conversationId, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        var filter = "conversationId eq '" + conversationId.Replace("'", "''", StringComparison.Ordinal) + "'";
        var url = Graph + "/me/messages?$filter=" + Uri.EscapeDataString(filter) + "&$select=id,internetMessageId,from,subject,receivedDateTime,isDraft&$top=50";
        using var request = Bearer(HttpMethod.Get, url, accessToken);
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSentAsync(response, cancellationToken).ConfigureAwait(false);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).RootElement;
        var list = new List<MailboxMessage>();
        if (root.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in value.EnumerateArray())
            {
                if (m.TryGetProperty("isDraft", out var draft) && draft.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                var received = Str(m, "receivedDateTime") is { } r && DateTimeOffset.TryParse(r, out var at) ? at : DateTimeOffset.MinValue;
                if (received <= since)
                {
                    continue;
                }

                var from = m.TryGetProperty("from", out var f) && f.TryGetProperty("emailAddress", out var e) ? e : default;
                list.Add(new MailboxMessage(
                    Str(m, "id") ?? string.Empty,
                    Str(m, "internetMessageId") ?? string.Empty,
                    from.ValueKind == JsonValueKind.Object ? Str(from, "address") ?? string.Empty : string.Empty,
                    from.ValueKind == JsonValueKind.Object ? Str(from, "name") ?? string.Empty : string.Empty,
                    Str(m, "subject") ?? string.Empty,
                    received));
            }
        }

        return list.Where(m => m.Id.Length > 0).OrderBy(m => m.ReceivedAt).ToList();
    }

    public async Task<byte[]?> RawAsync(string accessToken, string messageId, CancellationToken cancellationToken = default)
    {
        using var request = Bearer(HttpMethod.Get, Graph + "/me/messages/" + Uri.EscapeDataString(messageId) + "/$value", accessToken);
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false) : null;
    }

    public async Task<IReadOnlyList<TeamsChannel>> TeamsChannelsAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var result = new List<TeamsChannel>();
        using var teamsRequest = Bearer(HttpMethod.Get, Graph + "/me/joinedTeams?$select=id,displayName", accessToken);
        using var teamsResponse = await Http.SendAsync(teamsRequest, cancellationToken).ConfigureAwait(false);
        await EnsureSentAsync(teamsResponse, cancellationToken).ConfigureAwait(false);
        var teams = JsonDocument.Parse(await teamsResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).RootElement;
        foreach (var team in teams.GetProperty("value").EnumerateArray().Take(50))
        {
            var teamId = Str(team, "id");
            if (teamId is null)
            {
                continue;
            }

            using var channelsRequest = Bearer(HttpMethod.Get, Graph + "/teams/" + Uri.EscapeDataString(teamId) + "/channels?$select=id,displayName", accessToken);
            using var channelsResponse = await Http.SendAsync(channelsRequest, cancellationToken).ConfigureAwait(false);
            if (!channelsResponse.IsSuccessStatusCode)
            {
                continue;
            }

            var channels = JsonDocument.Parse(await channelsResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).RootElement;
            foreach (var channel in channels.GetProperty("value").EnumerateArray())
            {
                if (Str(channel, "id") is { } channelId)
                {
                    result.Add(new TeamsChannel(teamId, Str(team, "displayName") ?? "Team", channelId, Str(channel, "displayName") ?? "Channel"));
                }
            }
        }

        return result;
    }

    public async Task PostToTeamsAsync(string accessToken, string teamId, string channelId, string html, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new { body = new { contentType = "html", content = html } });
        using var request = Bearer(HttpMethod.Post, Graph + "/teams/" + Uri.EscapeDataString(teamId) + "/channels/" + Uri.EscapeDataString(channelId) + "/messages", accessToken,
            new StringContent(body, Encoding.UTF8, "application/json"));
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSentAsync(response, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Google Workspace / Gmail: an OAuth web client (<c>CaseMail__Google__ClientId/ClientSecret</c>) with the
/// <c>gmail.send</c> scope only. Off until a client is configured (Raffa has no Google Cloud project yet);
/// sends only — replies to a Gmail mailbox keep the manual path.
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
        using var request = Bearer(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo", Str(token, "access_token") ?? string.Empty);
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new MailboxSendException("Google did not return the mailbox address.", true);
        }

        var me = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).RootElement;
        var address = Str(me, "email") ?? throw new MailboxSendException("This Google account has no email address.", true);
        return new MailboxGrant(refresh, address, Str(me, "name"), Str(token, "scope"));
    }

    public override async Task<SentMail> SendAsync(string accessToken, OutgoingMail mail, CancellationToken cancellationToken = default)
    {
        var raw = Convert.ToBase64String(OutgoingMime.Bytes(mail)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using var request = Bearer(HttpMethod.Post, "https://gmail.googleapis.com/gmail/v1/users/me/messages/send", accessToken,
            new StringContent(JsonSerializer.Serialize(new { raw }), Encoding.UTF8, "application/json"));
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSentAsync(response, cancellationToken).ConfigureAwait(false);
        return new SentMail(null, null);
    }
}
