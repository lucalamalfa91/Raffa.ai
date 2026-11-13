using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Raffa.Api.Infrastructure;
using Raffa.Renewals.Application.CaseWork;
using Raffa.Renewals.Application.Notifications;
using Raffa.Renewals.Application.Outreach;

namespace Raffa.Api.Tests;

/// <summary>Renewal plan F2/F3/F3b host adapters: Resend's signature, the outgoing MIME, the Microsoft and Google
/// mailbox calls, and the Teams / Slack webhook payloads — against recorded HTTP, never the real services.</summary>
public sealed class OutreachAdapterTests
{
    private const string Secret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";

    [Fact]
    public void The_resend_signature_is_verified_and_a_forged_or_stale_one_refused()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        const string body = "{\"type\":\"email.received\"}";
        var ts = now.ToUnixTimeSeconds().ToString();
        var signature = SvixSignature.Sign(Secret, "msg_1", ts, body);

        Assert.True(SvixSignature.Verify(Secret, "msg_1", ts, "v1,bogus " + signature, body, now));
        Assert.False(SvixSignature.Verify(Secret, "msg_1", ts, signature, body + " ", now));
        Assert.False(SvixSignature.Verify(Secret, "msg_2", ts, signature, body, now));
        Assert.False(SvixSignature.Verify(Secret, "msg_1", ts, signature, body, now.AddMinutes(6)));
        Assert.False(SvixSignature.Verify(null, "msg_1", ts, signature, body, now));
    }

    [Fact]
    public void The_outgoing_message_carries_the_case_address_in_cc_and_reply_to_and_the_attachments()
    {
        var mail = Mail();
        using var stream = new MemoryStream(OutgoingMime.Bytes(mail));
        var message = MimeMessage.Load(stream);

        Assert.Equal("marta@acme.example", message.From.Mailboxes.Single().Address);
        Assert.Equal("anna@supplier.example", message.To.Mailboxes.Single().Address);
        Assert.Equal("r-abc123def456@in.raffa.test", message.Cc.Mailboxes.Single().Address);
        Assert.Equal(["marta@acme.example", "r-abc123def456@in.raffa.test"], message.ReplyTo.Mailboxes.Select(m => m.Address));
        Assert.Equal("Splunk MSA — renewal proposal", message.Subject);
        Assert.Contains("Dear Splunk team", message.TextBody);
        Assert.Equal("contract.pdf", message.Attachments.OfType<MimePart>().Single().FileName);
    }

    [Fact]
    public async Task Microsoft_exchanges_the_code_sends_through_a_draft_and_reads_only_the_conversation()
    {
        var http = new RecordingHttp(request => request.RequestUri!.AbsoluteUri switch
        {
            var u when u.Contains("/oauth2/v2.0/token") => Json(new { access_token = "at", refresh_token = "rt-new", scope = "https://graph.microsoft.com/Mail.Send https://graph.microsoft.com/Mail.Read" }),
            var u when u.StartsWith("https://graph.microsoft.com/v1.0/me?") => Json(new { mail = "marta@acme.example", displayName = "Marta Keller" }),
            var u when u.EndsWith("/me/messages") && request.Method == HttpMethod.Post => Json(new { id = "draft-1", conversationId = "conv-1", internetMessageId = "<m1@acme>" }),
            var u when u.EndsWith("/me/messages/draft-1/send") => new HttpResponseMessage(HttpStatusCode.Accepted),
            var u when u.Contains("/me/messages?$filter=") => Json(new
            {
                value = new object[]
                {
                    new { id = "old", internetMessageId = "<old>", subject = "x", receivedDateTime = "2026-11-01T10:00:00Z", isDraft = false, from = new { emailAddress = new { address = "anna@splunk.example", name = "Anna" } } },
                    new { id = "r1", internetMessageId = "<r1>", subject = "Re: x", receivedDateTime = "2026-11-20T10:00:00Z", isDraft = false, from = new { emailAddress = new { address = "anna@splunk.example", name = "Anna" } } },
                },
            }),
            var u when u.EndsWith("/me/messages/r1/$value") => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes("MIME")) },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var provider = new MicrosoftMailboxProvider(http, Options());

        var url = Uri.UnescapeDataString(provider.AuthorizeUrl("st", "https://api.raffa.test/api/mailbox/callback/microsoft", "marta@acme.example"));
        Assert.Contains("Mail.Send", url);
        Assert.Contains("Mail.Read", url);
        Assert.Contains("ChannelMessage.Send", url);
        var grant = await provider.ExchangeCodeAsync("code", "https://api.raffa.test/api/mailbox/callback/microsoft");
        Assert.Equal(("rt-new", "marta@acme.example", "Marta Keller"), (grant.RefreshToken, grant.Address, grant.DisplayName));
        Assert.True(provider.CanReceive(grant.Scopes));
        Assert.False(provider.CanPostToTeams(grant.Scopes));

        var token = await provider.AccessTokenAsync("rt-old");
        Assert.Equal(("at", "rt-new"), (token.AccessToken, token.RotatedRefreshToken));
        Assert.Contains("refresh_token=rt-old", http.Requests.Last(r => r.Uri.Contains("/token")).Body);

        var sent = await provider.SendAsync("at", Mail());
        Assert.Equal("conv-1", sent.ConversationId);
        var draft = http.Requests.Single(r => r.Uri.EndsWith("/me/messages"));
        Assert.Equal("Bearer at", draft.Authorization);
        var mime = MimeMessage.Load(new MemoryStream(Convert.FromBase64String(draft.Body)));
        Assert.Equal("anna@supplier.example", mime.To.Mailboxes.Single().Address);
        Assert.Contains(http.Requests, r => r.Uri.EndsWith("/me/messages/draft-1/send"));

        var replies = await provider.ConversationAsync("at", "conv-1", new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero));
        var reply = Assert.Single(replies);
        Assert.Equal(("r1", "anna@splunk.example"), (reply.Id, reply.FromAddress));
        Assert.Contains("conversationId%20eq%20%27conv-1%27", http.Requests.Single(r => r.Uri.Contains("$filter=")).Uri);
        Assert.Equal("MIME", Encoding.ASCII.GetString((await provider.RawAsync("at", "r1"))!));
    }

    [Fact]
    public async Task A_revoked_microsoft_grant_asks_to_reconnect()
    {
        var http = new RecordingHttp(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":\"invalid_grant\"}") });
        var ex = await Assert.ThrowsAsync<MailboxSendException>(() => new MicrosoftMailboxProvider(http, Options()).AccessTokenAsync("rt"));
        Assert.True(ex.Reconnect);
    }

    [Fact]
    public async Task Google_sends_the_raw_message_through_the_gmail_api_with_the_send_only_scope()
    {
        var http = new RecordingHttp(request => request.RequestUri!.Host switch
        {
            "oauth2.googleapis.com" => Json(new { access_token = "gat" }),
            "gmail.googleapis.com" => Json(new { id = "m1" }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var provider = new GoogleMailboxProvider(http, Options());
        var url = Uri.UnescapeDataString(provider.AuthorizeUrl("st", "https://api.raffa.test/cb", null));
        Assert.Contains("https://www.googleapis.com/auth/gmail.send", url);
        Assert.Contains("access_type=offline", url);

        var token = await provider.AccessTokenAsync("grt");
        Assert.Equal("gat", token.AccessToken);
        Assert.Null((await provider.SendAsync(token.AccessToken, Mail())).ConversationId);
        var send = http.Requests.Single(r => r.Uri.Contains("messages/send"));
        var raw = JsonDocument.Parse(send.Body).RootElement.GetProperty("raw").GetString()!;
        var b64 = raw.Replace('-', '+').Replace('_', '/');
        b64 += (b64.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        Assert.Equal("anna@supplier.example", MimeMessage.Load(new MemoryStream(Convert.FromBase64String(b64))).To.Mailboxes.Single().Address);
    }

    [Fact]
    public async Task Teams_and_slack_receive_the_title_the_line_and_the_link_only()
    {
        var http = new RecordingHttp(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var delivery = new NotificationDelivery(Guid.NewGuid(), Guid.NewGuid(), "teams", "https://prod.westeurope.logic.azure.com/wf",
            new NotificationMessage("supplier_message", "New email from Splunk", "Splunk MSA: confirm it belongs to the negotiation.", "/renewals/c1", null),
            "https://app.raffa.test/renewals/c1");

        Assert.Null((await new TeamsWebhookSender(http).SendAsync(delivery, CancellationToken.None)).Error);
        Assert.Null((await new SlackWebhookSender(http).SendAsync(delivery with { ChannelKind = "slack", Target = "https://hooks.slack.com/x" }, CancellationToken.None)).Error);

        var teams = JsonDocument.Parse(http.Requests[0].Body).RootElement.GetProperty("attachments")[0].GetProperty("content");
        Assert.Equal("AdaptiveCard", teams.GetProperty("type").GetString());
        Assert.Equal("https://app.raffa.test/renewals/c1", teams.GetProperty("actions")[0].GetProperty("url").GetString());
        var slack = JsonDocument.Parse(http.Requests[1].Body).RootElement.GetProperty("text").GetString();
        Assert.Equal("*New email from Splunk*\nSplunk MSA: confirm it belongs to the negotiation.\n<https://app.raffa.test/renewals/c1|Open in Raffa>", slack);

        var gone = new RecordingHttp(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.True((await new SlackWebhookSender(gone).SendAsync(delivery, CancellationToken.None)).Gone);
    }

    [Fact]
    public async Task The_resend_webhook_refuses_an_unsigned_call_and_ignores_other_events()
    {
        var options = Options();
        options.Resend.WebhookSecret = Secret;
        var unsigned = await ResendWebhookEndpoint.HandleAsync(Request("{}", sign: false), options, new NoFetcher(), null!, NullLoggerFactory.Instance, CancellationToken.None);
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.UnauthorizedHttpResult>(unsigned);

        var other = await ResendWebhookEndpoint.HandleAsync(Request("{\"type\":\"email.sent\",\"data\":{}}", sign: true), options, new NoFetcher(), null!, NullLoggerFactory.Instance, CancellationToken.None);
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok>(other);

        var unreadable = await ResendWebhookEndpoint.HandleAsync(Request("{\"type\":\"email.received\",\"data\":{\"email_id\":\"e1\"}}", sign: true), options, new NoFetcher(), null!, NullLoggerFactory.Instance, CancellationToken.None);
        Assert.Equal(503, Assert.IsAssignableFrom<IStatusCodeHttpResult>(unreadable).StatusCode);
    }

    private static HttpRequest Request(string body, bool sign)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        if (sign)
        {
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            context.Request.Headers["svix-id"] = "msg_1";
            context.Request.Headers["svix-timestamp"] = ts;
            context.Request.Headers["svix-signature"] = SvixSignature.Sign(Secret, "msg_1", ts, body);
        }

        return context.Request;
    }

    private static CaseMailOptions Options() => new()
    {
        Microsoft = new OAuthClientOptions { ClientId = "ms-client", ClientSecret = "ms-secret" },
        Google = new OAuthClientOptions { ClientId = "g-client", ClientSecret = "g-secret" },
    };

    private static OutgoingMail Mail() => new(
        "marta@acme.example", "Marta Keller", ["anna@supplier.example"], ["r-abc123def456@in.raffa.test"],
        ["marta@acme.example", "r-abc123def456@in.raffa.test"], "Splunk MSA — renewal proposal", "Dear Splunk team,\n\nOur proposal.",
        [new CaseUpload("contract.pdf", "application/pdf", Encoding.ASCII.GetBytes("%PDF-1.7"))]);

    private static HttpResponseMessage Json(object o) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json") };

    private sealed class NoFetcher : IInboundMailFetcher
    {
        public Task<(byte[] Raw, IReadOnlyList<string> Recipients)?> FetchAsync(string emailId, CancellationToken ct) =>
            Task.FromResult<(byte[], IReadOnlyList<string>)?>(null);
    }

    private sealed record Recorded(string Uri, string Body, string? Authorization);

    private sealed class RecordingHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : IHttpClientFactory
    {
        public List<Recorded> Requests { get; } = [];

        public HttpClient CreateClient(string name) => new(new Handler(this, respond));

        private sealed class Handler(RecordingHttp owner, Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
                owner.Requests.Add(new Recorded(request.RequestUri!.AbsoluteUri, body, request.Headers.Authorization?.ToString()));
                return respond(request);
            }
        }
    }
}
