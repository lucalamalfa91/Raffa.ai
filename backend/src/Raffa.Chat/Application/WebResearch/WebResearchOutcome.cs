using Raffa.Chat.Application.Reply;

namespace Raffa.Chat.Application.WebResearch;

/// <summary>What one authorised web research produced (ADR-030).</summary>
public enum WebResearchOutcomeKind
{
    /// <summary>A guard-approved summary with its web citations — rendered as an <c>answer</c>
    /// whose provenance is <c>web</c> + unverified.</summary>
    Answered,

    /// <summary>The web was searched but the summary failed a guard — an <c>abstain</c> that names
    /// the sources found, never the rejected text.</summary>
    Abstained,

    /// <summary>The persona declined the query as outside procurement — a <c>refusal</c>.</summary>
    Refused,

    /// <summary>The research role could not be called (no deployment, transport failure) — the
    /// caller decides how to say "unavailable"; <see cref="WebResearchOutcome.Error"/> is the
    /// diagnostic, never shown to the user.</summary>
    Failed,
}

/// <summary>
/// The composer's result. Carries every reply field except <c>actions</c>: those come from the
/// capability catalog in the composition root, never from here and never from the model.
/// </summary>
/// <param name="Kind">See <see cref="WebResearchOutcomeKind"/>.</param>
/// <param name="Markdown">The summary, the abstain reason or the refusal prose, in the request's
/// language.</param>
/// <param name="Citations">One <c>web</c>-corpus citation per source the summary may cite, in
/// marker order; empty unless <see cref="Kind"/> is <see cref="WebResearchOutcomeKind.Answered"/>.</param>
/// <param name="Provenance">Sources <c>["web"]</c> and <c>Unverified = true</c> for an answer;
/// model/prompt/hash echoed from the research call whenever one happened.</param>
/// <param name="SourceCount">How many sources the search tool returned — audit only.</param>
/// <param name="GuardIntervened">Whether a guard rejected the summary.</param>
/// <param name="GuardViolation">The violation, diagnostics only (ADR-011: never in the audit trail's
/// free text, never shown).</param>
/// <param name="Error">The gateway failure when <see cref="Kind"/> is <see cref="WebResearchOutcomeKind.Failed"/>.</param>
public sealed record WebResearchOutcome(
    WebResearchOutcomeKind Kind,
    string Markdown,
    IReadOnlyList<ReplyCitation> Citations,
    ReplyProvenance Provenance,
    int SourceCount,
    bool GuardIntervened,
    string? GuardViolation,
    string? Error)
{
    public ReplyKind AsReplyKind => Kind switch
    {
        WebResearchOutcomeKind.Answered => Reply.ReplyKind.Answer,
        WebResearchOutcomeKind.Refused => Reply.ReplyKind.Refusal,
        _ => Reply.ReplyKind.Abstain,
    };
}
