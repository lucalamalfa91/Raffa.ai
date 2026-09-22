using Raffa.Chat.Domain.Conversations;
using Raffa.SharedKernel;

namespace Raffa.Chat.Domain.Feedback;

/// <summary>Lifecycle of a <see cref="FeatureRequest"/> — stored as a string.</summary>
public static class FeatureRequestStatus
{
    /// <summary>Stored; no publisher configured, or not published yet.</summary>
    public const string Recorded = "recorded";

    /// <summary>Stored and a GitHub issue was opened for it.</summary>
    public const string IssueOpened = "issue_opened";

    /// <summary>Stored; the publisher failed (reason in <see cref="FeatureRequest.PublishError"/>).
    /// Served on the wire as <see cref="Recorded"/> — the failure is an operator's concern, never
    /// the user's.</summary>
    public const string IssueFailed = "issue_failed";
}

/// <summary>
/// One feature request a user submitted from Ask Raffa's in-chat feedback card (ADR-030 D5),
/// answering the capability-gap offer of one Raffa turn. Tenant-scoped and under Row-Level
/// Security like every other Chat table (ADR-009); <see cref="UserId"/> stays here and is never
/// published. Unique per <c>(tenant, message)</c>: one offer, one report.
/// </summary>
public sealed class FeatureRequest : TenantScopedEntity
{
    public required EntityId ConversationId { get; set; }

    /// <summary>The Raffa turn whose <c>payload.feedbackOffer</c> was answered.</summary>
    public required EntityId MessageId { get; set; }

    /// <summary>The submitting caller's token subject (ADR-010) — for the tenant's own audit
    /// trail only; the published issue carries no identity by design.</summary>
    public required string UserId { get; set; }

    /// <summary><c>Application.Gaps.CapabilityGap.Key</c>.</summary>
    public required string GapKey { get; set; }

    /// <summary>The gap's title in the question's language, as the card showed it.</summary>
    public required string GapTitle { get; set; }

    /// <summary>"it" or "en".</summary>
    public required string Language { get; set; }

    /// <summary>The three answers, serialized (<c>{ what, frequency, importance }</c>).</summary>
    public required string AnswersJson { get; set; }

    /// <summary>The deployment the request came from ("dev", "demo", "local").</summary>
    public required string Environment { get; set; }

    /// <summary>An opaque, non-reversible short hash of the tenant id — enough for the devs to
    /// group reports from one workspace, never enough to name it.</summary>
    public required string WorkspaceHash { get; set; }

    /// <summary>One of <see cref="FeatureRequestStatus"/>.</summary>
    public required string Status { get; set; }

    public int? IssueNumber { get; set; }

    public string? IssueUrl { get; set; }

    /// <summary>Why publishing failed, when it did — an operator-facing string, never shown.</summary>
    public string? PublishError { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
