using Raffa.Chat.Application.Reply;

namespace Raffa.AiFlows.WebResearch.Orchestration;

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
/// <param name="ReleaseBudget">F3-T03: the research call failed for transport or configuration
/// reasons (not a content-filter verdict, not unusable output), so the caller hands the budget unit it
/// reserved before the call back.</param>
/// <param name="FiguresVerified">F3-T01: figures of the shown summary found in a cited quote — telemetry only.</param>
/// <param name="FiguresReported">F3-T01: explicit figures kept without a quote to check them (only when
/// <see cref="WebResearchOptions.AllowReportedFigures"/>) — telemetry only.</param>
/// <param name="SentencesRemoved">F3-T01: sentences cut from the summary because they stated a figure no
/// cited quote carries; non-zero also sets <see cref="GuardIntervened"/> on an answer.</param>
public sealed record WebResearchOutcome(
    WebResearchOutcomeKind Kind,
    string Markdown,
    IReadOnlyList<ReplyCitation> Citations,
    ReplyProvenance Provenance,
    int SourceCount,
    bool GuardIntervened,
    string? GuardViolation,
    string? Error,
    bool ReleaseBudget = false,
    int FiguresVerified = 0,
    int FiguresReported = 0,
    int SentencesRemoved = 0)
{
    public ReplyKind AsReplyKind => Kind switch
    {
        WebResearchOutcomeKind.Answered => ReplyKind.Answer,
        WebResearchOutcomeKind.Refused => ReplyKind.Refusal,
        _ => ReplyKind.Abstain,
    };
}
