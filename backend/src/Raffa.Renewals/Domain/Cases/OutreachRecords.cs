using Raffa.SharedKernel;

namespace Raffa.Renewals.Domain.Cases;

/// <summary>
/// <c>mailbox_connection</c>: a person's company mailbox connected to Raffa (Microsoft 365 or Google), used to
/// send the approved case emails from their own address. Only the send permission is granted; the refresh
/// token is stored encrypted (<see cref="Application.Outreach.SecretProtector"/>). One per person per workspace.
/// </summary>
public sealed class MailboxConnectionRecord : TenantScopedEntity
{
    /// <summary>The person's sign-in identity (<c>oid</c>).</summary>
    public required string UserIdentity { get; set; }

    /// <summary>microsoft | google.</summary>
    public required string Provider { get; set; }

    public required string Address { get; set; }

    public string? DisplayName { get; set; }

    public required string RefreshTokenProtected { get; set; }

    public required DateTimeOffset ConnectedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>The last send failure ("reconnect" when the grant was revoked or expired).</summary>
    public string? LastError { get; set; }
}

/// <summary>
/// <c>case_mail_address</c>: the address of a case (<c>r-&lt;token&gt;@&lt;inbound domain&gt;</c>), put in CC
/// and Reply-To of every email Raffa sends, so the supplier's reply reaches the case. The table only routes
/// an incoming message to its workspace and case — it holds no customer content — and is read before any
/// workspace is known (the inbound webhook), so it carries no row-level security policy — and is deliberately
/// not a <see cref="TenantScopedEntity"/> (like the platform registry tables), which the ADR-009 checks would
/// otherwise require to have one.
/// </summary>
public sealed class CaseMailAddressRecord
{
    public EntityId Id { get; set; } = EntityId.New();

    public required TenantId TenantId { get; set; }

    public required EntityId CaseId { get; set; }

    /// <summary>Random, lower-case, unguessable local-part suffix.</summary>
    public required string Token { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? DisabledAt { get; set; }
}

/// <summary>
/// <c>case_inbound_mail</c>: an email that reached a case address. It never moves the case by itself: the
/// owner is notified and confirms it belongs to the case (it is then registered as the supplier's reply,
/// with its attachments) or dismisses it.
/// </summary>
public sealed class CaseInboundMailRecord : TenantScopedEntity
{
    public required EntityId CaseId { get; set; }

    /// <summary>resend.</summary>
    public required string Provider { get; set; }

    public required string ProviderMessageId { get; set; }

    public required string FromAddress { get; set; }

    public required string Subject { get; set; }

    public required DateTimeOffset ReceivedAt { get; set; }

    /// <summary>The new part of the text, for the confirmation card.</summary>
    public required string Preview { get; set; }

    public string? AttachmentNamesJson { get; set; }

    /// <summary>The raw MIME message in object storage.</summary>
    public required string RawStoragePath { get; set; }

    /// <summary>pending | accepted | dismissed.</summary>
    public required string State { get; set; }

    public string? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>The supplier reply (rid) it was registered as.</summary>
    public int? ReplyIndex { get; set; }
}

/// <summary><c>notification</c>: one in-app notification of one person.</summary>
public sealed class NotificationRecord : TenantScopedEntity
{
    public required string UserIdentity { get; set; }

    /// <summary>supplier_message | approval_requested | mailbox_reconnect | test.</summary>
    public required string Kind { get; set; }

    public required string Title { get; set; }

    public required string Body { get; set; }

    /// <summary>Site-relative link (<c>/renewals/{caseId}</c>).</summary>
    public string? Link { get; set; }

    public Guid? CaseId { get; set; }

    /// <summary>The values of the title and body ({"supplier":…, "contract":…}), so the screens can show them in the reader's language.</summary>
    public string? VarsJson { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary>
/// <c>notification_channel</c>: where a person also wants notifications — a browser push subscription, an
/// email address, a Teams or Slack incoming webhook. The target is stored encrypted.
/// </summary>
public sealed class NotificationChannelRecord : TenantScopedEntity
{
    public required string UserIdentity { get; set; }

    /// <summary>webpush | email | teams | slack.</summary>
    public required string Kind { get; set; }

    public required string TargetProtected { get; set; }

    /// <summary>What the person sees ("Chrome on this computer", "#procurement", the email address).</summary>
    public required string Label { get; set; }

    public bool Enabled { get; set; } = true;

    public required DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastDeliveredAt { get; set; }

    public string? LastError { get; set; }
}
