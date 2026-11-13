using Raffa.SharedKernel;

namespace Raffa.Renewals.Domain.Cases;

/// <summary>
/// <c>renewal_case</c> (BACKEND-SPEC §2.1): the AskRaffa 4.0 renewal case aggregate. <see cref="State"/>
/// is the whole engine state (<c>RenewalCase</c> as camelCase JSON); the other columns are a queryable
/// snapshot of it. <see cref="Version"/> is the optimistic-concurrency token every mutation must quote.
/// At most one open case (<see cref="ClosedAt"/> null) per contract.
/// </summary>
public sealed class RenewalCaseRecord : TenantScopedEntity
{
    public required EntityId ContractId { get; set; }

    public Guid? OwnerUserId { get; set; }

    public Guid? ApproverUserId { get; set; }

    /// <summary>avvio | piano | negoziazione | accordo | contratto_firma | acquisizione.</summary>
    public required string Phase { get; set; }

    /// <summary>The engine's step (interview, plan, email, waiting …).</summary>
    public required string Step { get; set; }

    /// <summary>open | waiting_supplier | waiting_internal | closed.</summary>
    public required string Status { get; set; }

    /// <summary>renew | notice | alternatives.</summary>
    public required string Branch { get; set; }

    /// <summary>The engine state (jsonb).</summary>
    public required string State { get; set; }

    public int Version { get; set; }

    public DateOnly? NoticeDeadline { get; set; }

    public DateOnly? ContractEnd { get; set; }

    public string? OutcomeKind { get; set; }

    public string? OutcomeJson { get; set; }

    public required DateTimeOffset OpenedAt { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }
}

/// <summary><c>case_email</c> (§2.2): projection of each case email, one row per (case, engine index).</summary>
public sealed class CaseEmailRecord : TenantScopedEntity
{
    public required EntityId CaseId { get; set; }

    /// <summary>Index of the email inside the case state.</summary>
    public int EmailIndex { get; set; }

    public required string Kind { get; set; }

    public int Version { get; set; }

    /// <summary>draft | to_approve | approved | copied | sent | superseded.</summary>
    public required string State { get; set; }

    /// <summary>Never generated: empty until a person types it.</summary>
    public string? ToAddress { get; set; }

    public required string Subject { get; set; }

    public required string Body { get; set; }

    public string? AttachmentsJson { get; set; }

    public int? ApprovedVersion { get; set; }

    public string? ApprovedBy { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public DateTimeOffset? CopiedAt { get; set; }

    public string? SentBy { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    /// <summary>The text the user says was actually sent (may differ from the approved body).</summary>
    public string? SentBody { get; set; }

    /// <summary>The connected mailbox Raffa sent it from (null: sent outside Raffa, declared by the user).</summary>
    public string? SentVia { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }
}

/// <summary><c>case_delegation</c> (§2.3): explicit approve/close rights on one case.</summary>
public sealed class CaseDelegationRecord : TenantScopedEntity
{
    public required EntityId CaseId { get; set; }

    public Guid? UserId { get; set; }

    public required string UserName { get; set; }

    public bool CanApprove { get; set; }

    public bool CanClose { get; set; }

    public string? GrantedBy { get; set; }

    public required DateTimeOffset GrantedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary><c>case_event</c> (§2.5): append-only timeline of a case (mirrored to the audit log).</summary>
public sealed class CaseEventRecord : TenantScopedEntity
{
    public required EntityId CaseId { get; set; }

    public required DateTimeOffset At { get; set; }

    /// <summary>A person's display name, <c>system</c> or <c>supplier</c>.</summary>
    public required string Actor { get; set; }

    public required string Type { get; set; }

    public string? PayloadJson { get; set; }

    public int? EmailIndex { get; set; }
}

/// <summary><c>case_vendor_message</c> (§2.6): pasted supplier replies, deduplicated by content hash.</summary>
public sealed class CaseVendorMessageRecord : TenantScopedEntity
{
    public required EntityId CaseId { get; set; }

    /// <summary>in | out.</summary>
    public required string Direction { get; set; }

    public required DateTimeOffset ReceivedAt { get; set; }

    public required string PastedText { get; set; }

    public required string ContentHash { get; set; }

    public string? ThreadKey { get; set; }

    public string? ParsedJson { get; set; }

    public string? AnalysedByModel { get; set; }

    public string? PromptVersion { get; set; }

    public string? InputHash { get; set; }
}

/// <summary>
/// <c>case_file</c>: every file a case received — a contract draft, the signed contract, an alternative
/// supplier's offer, a supplier email uploaded as <c>.eml</c>/<c>.msg</c> and that email's attachments.
/// The bytes live in tenant-prefixed object storage (<see cref="StoragePath"/>); the engine state keeps
/// only the file name, so this table is what makes a case file downloadable and reusable.
/// </summary>
public sealed class CaseFileRecord : TenantScopedEntity
{
    public required EntityId CaseId { get; set; }

    /// <summary>draft | signed | offer | email | attachment.</summary>
    public required string Kind { get; set; }

    public required string FileName { get; set; }

    public required string MimeType { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>SHA-256 of the bytes, upper-case hex.</summary>
    public required string Checksum { get; set; }

    public required string StoragePath { get; set; }

    /// <summary><c>draft</c>: the draft version (1-based) the engine gave it.</summary>
    public int? DraftVersion { get; set; }

    /// <summary><c>offer</c>: the alternative supplier.</summary>
    public string? Vendor { get; set; }

    /// <summary><c>email</c> / <c>attachment</c>: the reply index (rid) the email was registered as, if any.</summary>
    public int? ReplyIndex { get; set; }

    /// <summary><c>attachment</c>: the uploaded email it came with.</summary>
    public Guid? SourceFileId { get; set; }

    /// <summary><c>signed</c>: the Documents document it became when the case closed as renewed.</summary>
    public Guid? DocumentId { get; set; }

    public required string UploadedBy { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}

/// <summary><c>user_reminder</c> (§2.7): a person's "check date" — never the contractual notice deadline.</summary>
public sealed class UserReminderRecord : TenantScopedEntity
{
    public Guid? CaseId { get; set; }

    /// <summary>Index of the reminder inside the case state (null for a custom reminder).</summary>
    public int? ReminderIndex { get; set; }

    public Guid? UserId { get; set; }

    public required string UserName { get; set; }

    /// <summary>check_reply | check_contract | custom.</summary>
    public required string Kind { get; set; }

    public required DateOnly DueDate { get; set; }

    /// <summary>proposed | set | due | done | dismissed.</summary>
    public required string State { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}

/// <summary><c>today_snooze</c> (§2.12): an Oggi item postponed by a user until a date.</summary>
public sealed class TodaySnoozeRecord : TenantScopedEntity
{
    public required Guid UserId { get; set; }

    public required string ItemKey { get; set; }

    public required DateOnly Until { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}

/// <summary><c>company_rules</c>: a tenant's company rules (one row per tenant) — the text Raffa reads
/// and the alerting parameters taken from it.</summary>
public sealed class CompanyRulesRecord : TenantScopedEntity
{
    public required string Text { get; set; }

    /// <summary>Days before a notice deadline from which a renewal shows on Oggi.</summary>
    public required int StartWindowDays { get; set; }

    /// <summary>The policy document the rules were taken from, when one was uploaded.</summary>
    public string? SourceFile { get; set; }

    public required string UpdatedBy { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }
}
