using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Raffa.Renewals.Application.Outreach;
using Raffa.Renewals.Domain.Cases;
using Raffa.Renewals.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Renewals.Application.Notifications;

/// <summary>The kinds of notification channel.</summary>
public static class NotificationChannelKinds
{
    public const string WebPush = "webpush";
    public const string Email = "email";
    public const string Teams = "teams";
    public const string Slack = "slack";

    public static readonly IReadOnlyList<string> All = [WebPush, Email, Teams, Slack];
}

/// <summary>What happened, for one person.</summary>
public sealed record NotificationMessage(string Kind, string Title, string Body, string? Link, Guid? CaseId);

/// <summary>One delivery of a notification to one external channel; the target is in clear (never stored so).</summary>
public sealed record NotificationDelivery(
    Guid TenantId, Guid ChannelId, string ChannelKind, string Target, NotificationMessage Message, string? AbsoluteLink);

/// <summary>Delivers to external channels in the background (the host's queue and senders).</summary>
public interface INotificationDispatcher
{
    void Enqueue(NotificationDelivery delivery);
}

/// <summary>No external delivery (tests, hosts without the queue): in-app only.</summary>
public sealed class NullNotificationDispatcher : INotificationDispatcher
{
    public void Enqueue(NotificationDelivery delivery)
    {
    }
}

public sealed record NotificationItem(Guid Id, string Kind, string Title, string Body, string? Link, Guid? CaseId, DateTimeOffset CreatedAt, bool Read);

public sealed record NotificationFeed(IReadOnlyList<NotificationItem> Items, int Unread);

public sealed record NotificationChannelItem(Guid Id, string Kind, string Label, bool Enabled, DateTimeOffset CreatedAt, DateTimeOffset? LastDeliveredAt, string? LastError);

/// <summary><c>GET /api/notifications/settings</c>: the caller's channels and what this deployment offers.</summary>
public sealed record NotificationSettings(IReadOnlyList<NotificationChannelItem> Channels, IReadOnlyList<string> Available, string? VapidPublicKey);

/// <summary>
/// Notifications (plan F3b): every notification is kept in-app (the bell) and also delivered to the
/// channels the person connected — browser push, email, a Teams or Slack incoming webhook. External
/// channels only ever receive the title, a short line and the link: never prices, terms or email text.
/// Targets are stored encrypted. Delivery is in the background and never fails the action that caused it.
/// </summary>
public sealed class NotificationService(
    RenewalsDbContext db,
    ITenantContext tenantContext,
    IClock clock,
    CaseMailOptions mailOptions,
    NotificationOptions options,
    INotificationDispatcher dispatcher)
{
    private readonly SecretProtector? protector = SecretProtector.From(mailOptions);

    public IReadOnlyList<string> AvailableKinds =>
        protector is null
            ? []
            : NotificationChannelKinds.All.Where(k => k != NotificationChannelKinds.WebPush || options.WebPushEnabled).ToList();

    /// <summary>Stores the notification for each person and queues it to their enabled channels.</summary>
    public async Task PublishAsync(TenantId tenantId, IEnumerable<string> identities, NotificationMessage message, CancellationToken ct = default)
    {
        var people = identities.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct(StringComparer.Ordinal).ToList();
        if (people.Count == 0)
        {
            return;
        }

        using var scope = tenantContext.BeginScope(tenantId);
        var now = clock.UtcNow;
        foreach (var who in people)
        {
            db.Notifications.Add(new NotificationRecord
            {
                TenantId = tenantId,
                UserIdentity = who,
                Kind = message.Kind,
                Title = Cut(message.Title, 300),
                Body = Cut(message.Body, 1000),
                Link = message.Link,
                CaseId = message.CaseId,
                CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (protector is null)
        {
            return;
        }

        var channels = await db.NotificationChannels.AsNoTracking()
            .Where(c => people.Contains(c.UserIdentity) && c.Enabled)
            .ToListAsync(ct).ConfigureAwait(false);
        var absolute = message.Link is { } link && !string.IsNullOrWhiteSpace(mailOptions.WebPublicBaseUrl)
            ? mailOptions.WebPublicBaseUrl.TrimEnd('/') + link
            : null;
        foreach (var channel in channels)
        {
            if (protector.Unprotect(channel.TargetProtected) is { } target)
            {
                dispatcher.Enqueue(new NotificationDelivery(tenantId.Value, channel.Id.Value, channel.Kind, target, message, absolute));
            }
        }
    }

    public async Task<NotificationFeed> FeedAsync(TenantId tenantId, string identity, int limit = 30, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var mine = db.Notifications.AsNoTracking().Where(n => n.UserIdentity == identity);
        var rows = await mine.OrderByDescending(n => n.CreatedAt).Take(Math.Clamp(limit, 1, 100)).ToListAsync(ct).ConfigureAwait(false);
        var unread = await mine.CountAsync(n => n.ReadAt == null, ct).ConfigureAwait(false);
        return new NotificationFeed(
            rows.Select(n => new NotificationItem(n.Id.Value, n.Kind, n.Title, n.Body, n.Link, n.CaseId, n.CreatedAt, n.ReadAt is not null)).ToList(),
            unread);
    }

    /// <summary>Marks one notification (or all, when <paramref name="id"/> is null) as read.</summary>
    public async Task MarkReadAsync(TenantId tenantId, string identity, Guid? id, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var query = db.Notifications.Where(n => n.UserIdentity == identity && n.ReadAt == null);
        if (id is { } one)
        {
            var entityId = new EntityId(one);
            query = query.Where(n => n.Id == entityId);
        }

        var now = clock.UtcNow;
        foreach (var n in await query.ToListAsync(ct).ConfigureAwait(false))
        {
            n.ReadAt = now;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<NotificationSettings> SettingsAsync(TenantId tenantId, string identity, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var rows = await db.NotificationChannels.AsNoTracking().Where(c => c.UserIdentity == identity)
            .OrderBy(c => c.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        return new NotificationSettings(
            rows.Select(Item).ToList(),
            AvailableKinds,
            options.WebPushEnabled ? options.VapidPublicKey : null);
    }

    /// <summary>
    /// Adds a channel. <paramref name="target"/>: the push subscription JSON (webpush), an email address,
    /// an https webhook URL (Teams: a Workflows / Power Automate webhook; Slack: <c>https://hooks.slack.com/…</c>).
    /// Returns null with the reason when the target is not acceptable.
    /// </summary>
    public async Task<(NotificationChannelItem? Channel, string? Error)> AddChannelAsync(
        TenantId tenantId, string identity, string kind, string target, string? label, CancellationToken ct = default)
    {
        if (protector is null || !AvailableKinds.Contains(kind))
        {
            return (null, "This channel is not available.");
        }

        target = target.Trim();
        var error = Validate(kind, target);
        if (error is not null)
        {
            return (null, error);
        }

        using var scope = tenantContext.BeginScope(tenantId);
        var row = new NotificationChannelRecord
        {
            TenantId = tenantId,
            UserIdentity = identity,
            Kind = kind,
            TargetProtected = protector.Protect(target),
            Label = Cut(string.IsNullOrWhiteSpace(label) ? DefaultLabel(kind, target) : label.Trim(), 200),
            CreatedAt = clock.UtcNow,
        };
        db.NotificationChannels.Add(row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (Item(row), null);
    }

    public async Task<bool> UpdateChannelAsync(TenantId tenantId, string identity, Guid id, bool enabled, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var row = await db.NotificationChannels.FirstOrDefaultAsync(c => c.Id == new EntityId(id) && c.UserIdentity == identity, ct).ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }

        row.Enabled = enabled;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> RemoveChannelAsync(TenantId tenantId, string identity, Guid id, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var row = await db.NotificationChannels.FirstOrDefaultAsync(c => c.Id == new EntityId(id) && c.UserIdentity == identity, ct).ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }

        db.NotificationChannels.Remove(row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Sends a test notification to the caller (in-app and every enabled channel).</summary>
    public Task TestAsync(TenantId tenantId, string identity, CancellationToken ct = default) =>
        PublishAsync(tenantId, [identity], new NotificationMessage(
            "test", "Raffa — test notification", "Notifications reach you here.", "/settings/notifications", null), ct);

    /// <summary>Records the outcome of a background delivery; a gone push subscription is disabled.</summary>
    public async Task RecordDeliveryAsync(TenantId tenantId, Guid channelId, string? error, bool gone, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var row = await db.NotificationChannels.FirstOrDefaultAsync(c => c.Id == new EntityId(channelId), ct).ConfigureAwait(false);
        if (row is null)
        {
            return;
        }

        if (error is null)
        {
            row.LastDeliveredAt = clock.UtcNow;
            row.LastError = null;
        }
        else
        {
            row.LastError = Cut(error, 300);
            if (gone)
            {
                row.Enabled = false;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    internal static string? Validate(string kind, string target)
    {
        switch (kind)
        {
            case NotificationChannelKinds.Email:
                return MailAddress.TryCreate(target, out _) ? null : "Enter a valid email address.";
            case NotificationChannelKinds.Slack:
                return Uri.TryCreate(target, UriKind.Absolute, out var slack) && slack.Scheme == Uri.UriSchemeHttps
                       && slack.Host.Equals("hooks.slack.com", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : "Paste the Slack Incoming Webhook URL (https://hooks.slack.com/…).";
            case NotificationChannelKinds.Teams:
                return Uri.TryCreate(target, UriKind.Absolute, out var teams) && teams.Scheme == Uri.UriSchemeHttps && IsTeamsHost(teams.Host)
                    ? null
                    : "Paste the Teams Workflows webhook URL (https://…logic.azure.com/… or …powerplatform.com/…).";
            case NotificationChannelKinds.WebPush:
                return WebPushSubscription.TryParse(target) is null ? "The browser did not give a valid push subscription." : null;
            default:
                return "Unknown channel.";
        }
    }

    /// <summary>Teams incoming webhooks: Workflows (Power Automate / Logic Apps) and the legacy connectors.</summary>
    private static bool IsTeamsHost(string host) =>
        host.EndsWith(".logic.azure.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".powerplatform.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".powerautomate.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".webhook.office.com", StringComparison.OrdinalIgnoreCase);

    private static string DefaultLabel(string kind, string target) => kind switch
    {
        NotificationChannelKinds.Email => target,
        NotificationChannelKinds.Slack => "Slack",
        NotificationChannelKinds.Teams => "Microsoft Teams",
        _ => "Browser",
    };

    private static NotificationChannelItem Item(NotificationChannelRecord c) =>
        new(c.Id.Value, c.Kind, c.Label, c.Enabled, c.CreatedAt, c.LastDeliveredAt, c.LastError);

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];
}

/// <summary>A browser push subscription (<c>PushSubscription.toJSON()</c>).</summary>
public sealed record WebPushSubscription(string Endpoint, string P256dh, string Auth)
{
    public static WebPushSubscription? TryParse(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var endpoint = root.GetProperty("endpoint").GetString();
            var keys = root.GetProperty("keys");
            var p256dh = keys.GetProperty("p256dh").GetString();
            var auth = keys.GetProperty("auth").GetString();
            return Uri.TryCreate(endpoint, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(p256dh) && !string.IsNullOrEmpty(auth)
                ? new WebPushSubscription(endpoint!, p256dh!, auth!)
                : null;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
