namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// The single extraction auto-accept bar (ADR-024 w17 clause A1/A3). Both
/// <c>StagedExtractionService.DetermineDocumentStatus</c> and <c>DocumentQueryService.IsWeak</c>
/// consume this type so the Documents-row badge and the document's <c>needs_review</c> status
/// cannot disagree. Compare the raw stored double; do not round first.
/// </summary>
public static class ExtractionConfidencePolicy
{
    /// <summary>Raw stored confidence at or above which a field is accepted automatically.</summary>
    public const double AutoAcceptThreshold = 0.90;

    public const string AutoAccepted = "auto_accepted";
    public const string HumanAccepted = "human_accepted";
    public const string ReviewRequired = "review_required";

    /// <summary>Spec §7.3's five critical fields — judged against the same bar as every other
    /// field. There is no always-review list.</summary>
    public static readonly IReadOnlyList<string> CriticalFieldNames =
    [
        "annualSpend",
        "totalContractValue",
        "cancellationDeadline",
        "endDate",
        "renewalTermMonths",
    ];

    /// <summary>
    /// Server-computed decision for an extraction-time confidence. A null confidence is
    /// <see cref="ReviewRequired"/>, never accepted. Does not emit <see cref="HumanAccepted"/> —
    /// that state is written by a human validate/PATCH path, not by this function.
    /// </summary>
    public static string Decide(double? confidence) =>
        confidence is { } value && value >= AutoAcceptThreshold
            ? AutoAccepted
            : ReviewRequired;

    public static bool RequiresReview(double? confidence) =>
        Decide(confidence) == ReviewRequired;

    public static bool IsAutoAccepted(double? confidence) =>
        Decide(confidence) == AutoAccepted;
}
