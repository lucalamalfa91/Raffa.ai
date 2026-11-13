using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Azure;
using Azure.Communication.Email;
using Raffa.Renewals.Application.Notifications;
using Raffa.Renewals.Application.Outreach;
using Raffa.SharedKernel;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// The in-memory delivery queue (plan F3b): notifications are already stored in-app when they are queued, so
/// a lost delivery (a restart) costs only the external echo, never the notification itself.
/// </summary>
internal sealed class NotificationQueue : INotificationDispatcher
{
    private readonly Channel<NotificationDelivery> channel = Channel.CreateBounded<NotificationDelivery>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public ChannelReader<NotificationDelivery> Reader => channel.Reader;

    public void Enqueue(NotificationDelivery delivery) => channel.Writer.TryWrite(delivery);
}

/// <summary>The outcome of one delivery: null error = delivered; <c>Gone</c> = the target no longer exists.</summary>
internal sealed record DeliveryResult(string? Error, bool Gone = false)
{
    public static DeliveryResult Ok { get; } = new((string?)null);
}

/// <summary>Sends one notification to one external channel.</summary>
internal interface INotificationSender
{
    string Kind { get; }

    Task<DeliveryResult> SendAsync(NotificationDelivery delivery, CancellationToken ct);
}

/// <summary>Teams Workflows incoming webhook: an Adaptive Card with the title, the line and an "Open in Raffa" button.</summary>
internal sealed class TeamsWebhookSender(IHttpClientFactory httpClientFactory) : INotificationSender
{
    public string Kind => NotificationChannelKinds.Teams;

    public async Task<DeliveryResult> SendAsync(NotificationDelivery d, CancellationToken ct)
    {
        var body = new List<object>
        {
            new { type = "TextBlock", text = d.Message.Title, weight = "Bolder", size = "Medium", wrap = true },
            new { type = "TextBlock", text = d.Message.Body, wrap = true },
        };
        object[] actions = d.AbsoluteLink is null ? [] : [new { type = "Action.OpenUrl", title = "Open in Raffa", url = d.AbsoluteLink }];
        var payload = new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = new Dictionary<string, object>
                    {
                        ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
                        ["type"] = "AdaptiveCard",
                        ["version"] = "1.4",
                        ["body"] = body,
                        ["actions"] = actions,
                    },
                },
            },
        };
        return await WebhookPost.SendAsync(httpClientFactory, d.Target, payload, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Teams with the person's connected Microsoft account: posts the title, the line and the link to the channel
/// they chose, as them (no webhook). A revoked grant or a gone channel disables the channel.
/// </summary>
internal sealed class TeamsAccountSender(MailboxService mailboxes) : INotificationSender
{
    public string Kind => NotificationChannelKinds.TeamsAccount;

    public async Task<DeliveryResult> SendAsync(NotificationDelivery d, CancellationToken ct)
    {
        if (TeamsChannelTarget.TryParse(d.Target) is not { } target || string.IsNullOrEmpty(d.UserIdentity))
        {
            return new DeliveryResult("No Teams channel chosen.", Gone: true);
        }

        var html = "<b>" + WebUtility.HtmlEncode(d.Message.Title) + "</b><br>" + WebUtility.HtmlEncode(d.Message.Body)
                   + (d.AbsoluteLink is null ? string.Empty : "<br><a href=\"" + WebUtility.HtmlEncode(d.AbsoluteLink) + "\">Open in Raffa</a>");
        try
        {
            await mailboxes.PostToTeamsAsync(new TenantId(d.TenantId), d.UserIdentity, target.TeamId, target.ChannelId, html, ct).ConfigureAwait(false);
            return DeliveryResult.Ok;
        }
        catch (MailboxSendException ex)
        {
            return new DeliveryResult(ex.Reconnect ? "Connect your Microsoft account again." : "Teams refused the message.", ex.Reconnect);
        }
    }
}

/// <summary>Slack incoming webhook: the title in bold, the line and the link.</summary>
internal sealed class SlackWebhookSender(IHttpClientFactory httpClientFactory) : INotificationSender
{
    public string Kind => NotificationChannelKinds.Slack;

    public Task<DeliveryResult> SendAsync(NotificationDelivery d, CancellationToken ct)
    {
        var text = "*" + Escape(d.Message.Title) + "*\n" + Escape(d.Message.Body) + (d.AbsoluteLink is null ? string.Empty : "\n<" + d.AbsoluteLink + "|Open in Raffa>");
        return WebhookPost.SendAsync(httpClientFactory, d.Target, new { text }, ct);
    }

    private static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

internal static class WebhookPost
{
    public const string HttpClientName = "notification-webhook";

    public static async Task<DeliveryResult> SendAsync(IHttpClientFactory factory, string url, object payload, CancellationToken ct)
    {
        using var response = await factory.CreateClient(HttpClientName)
            .PostAsync(url, new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"), ct)
            .ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? DeliveryResult.Ok
            : new DeliveryResult("The webhook answered " + (int)response.StatusCode + ".", response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone);
    }
}

/// <summary>Email through Azure Communication Services (the invitation sender), when mail is configured.</summary>
internal sealed class EmailNotificationSender(InvitationHostOptions hostOptions, EmailClient? client = null) : INotificationSender
{
    public string Kind => NotificationChannelKinds.Email;

    public async Task<DeliveryResult> SendAsync(NotificationDelivery d, CancellationToken ct)
    {
        if (client is null || string.IsNullOrWhiteSpace(hostOptions.Mail.SenderAddress))
        {
            return new DeliveryResult("Email notifications are not configured on this server.");
        }

        var text = d.Message.Body + (d.AbsoluteLink is null ? string.Empty : "\r\n\r\nOpen in Raffa: " + d.AbsoluteLink);
        try
        {
            await client.SendAsync(WaitUntil.Started, new EmailMessage(hostOptions.Mail.SenderAddress, d.Target, new EmailContent(d.Message.Title) { PlainText = text }), ct)
                .ConfigureAwait(false);
            return DeliveryResult.Ok;
        }
        catch (RequestFailedException ex)
        {
            return new DeliveryResult("The mail service refused the message (" + ex.Status + ").");
        }
    }
}

/// <summary>Browser push (VAPID): the title, the line and the link; the service worker shows it.</summary>
internal sealed class WebPushSender(NotificationOptions options) : INotificationSender
{
    public string Kind => NotificationChannelKinds.WebPush;

    public async Task<DeliveryResult> SendAsync(NotificationDelivery d, CancellationToken ct)
    {
        if (!options.WebPushEnabled || WebPushSubscription.TryParse(d.Target) is not { } s)
        {
            return new DeliveryResult("Browser notifications are not configured on this server.");
        }

        var payload = JsonSerializer.Serialize(new { title = d.Message.Title, body = d.Message.Body, url = d.Message.Link ?? "/" });
        using var client = new WebPush.WebPushClient();
        client.SetVapidDetails(options.VapidSubject, options.VapidPublicKey, options.VapidPrivateKey);
        try
        {
            await client.SendNotificationAsync(new WebPush.PushSubscription(s.Endpoint, s.P256dh, s.Auth), payload, cancellationToken: ct).ConfigureAwait(false);
            return DeliveryResult.Ok;
        }
        catch (WebPush.WebPushException ex)
        {
            return new DeliveryResult("The browser push service answered " + (int)ex.StatusCode + ".", ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone);
        }
    }
}

/// <summary>Drains <see cref="NotificationQueue"/>: one attempt plus one retry per delivery, outcome recorded on the channel.</summary>
internal sealed class NotificationDeliveryService(
    NotificationQueue queue,
    IServiceScopeFactory scopes,
    ILogger<NotificationDeliveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var delivery in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            await DeliverAsync(delivery, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task DeliverAsync(NotificationDelivery delivery, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sender = scope.ServiceProvider.GetServices<INotificationSender>().FirstOrDefault(s => s.Kind == delivery.ChannelKind);
        DeliveryResult result;
        if (sender is null)
        {
            result = new DeliveryResult("No sender for this channel.");
        }
        else
        {
            result = await TryAsync(sender, delivery, ct).ConfigureAwait(false);
            if (result.Error is not null && !result.Gone)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                result = await TryAsync(sender, delivery, ct).ConfigureAwait(false);
            }
        }

        try
        {
            await scope.ServiceProvider.GetRequiredService<NotificationService>()
                .RecordDeliveryAsync(new TenantId(delivery.TenantId), delivery.ChannelId, result.Error, result.Gone, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The outcome of a {Kind} notification could not be recorded", delivery.ChannelKind);
        }
    }

    private async Task<DeliveryResult> TryAsync(INotificationSender sender, NotificationDelivery delivery, CancellationToken ct)
    {
        try
        {
            return await sender.SendAsync(delivery, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("A {Kind} notification could not be delivered: {Error}", delivery.ChannelKind, ex.GetType().Name);
            return new DeliveryResult("Delivery failed (" + ex.GetType().Name + ").");
        }
    }
}

/// <summary>
/// Reads the supplier's replies from the connected mailboxes (plan F3, mailbox) every few minutes — only the
/// conversations Raffa started — and adds them to their case as emails to confirm. Several API replicas may
/// run it at once: a message is stored once (unique index), so a race costs only a read.
/// </summary>
internal sealed class MailboxReaderService(IServiceScopeFactory scopes, ILogger<MailboxReaderService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var options = scope.ServiceProvider.GetRequiredService<CaseMailOptions>();
                if (!options.HasTokenKey)
                {
                    continue;
                }

                var added = await scope.ServiceProvider.GetRequiredService<CaseMailService>()
                    .PollMailboxesAsync(Read, stoppingToken).ConfigureAwait(false);
                if (added > 0)
                {
                    logger.LogInformation("{Count} supplier emails read from connected mailboxes", added);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Reading the connected mailboxes failed; next try in {Minutes} minutes", Interval.TotalMinutes);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    internal static (string Preview, IReadOnlyList<string> Attachments) Read(byte[] raw)
    {
        var email = CaseEmailFileReader.Read("message.eml", raw, 20L * 1024 * 1024);
        return email is null ? (string.Empty, []) : (email.Text, email.Attachments.Select(a => a.FileName).ToList());
    }
}
