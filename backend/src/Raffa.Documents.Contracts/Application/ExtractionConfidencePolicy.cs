namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// The single extraction auto-accept bar (ADR-024 w17 clause A1/A3). Both
/// <c>StagedExtractionService.DetermineDocumentStatus</c> and <c>DocumentQueryService.IsWeak</c>
/// consume this type so the Documents-row badge and the document's <c>needs_review</c> status
/// cannot disagree. Compare the raw stored double; do not round first.
///
/// <para>
/// Two fields are never left as a fuzzy model guess: an extracted <c>startDate</c> is officialized
/// at <see cref="OfficialConfidence"/>, and <c>status</c> is derived from the official start/end
/// dates (<see cref="DeriveStatus"/>) rather than from the model's wording.
/// </para>
/// </summary>
public static class ExtractionConfidencePolicy
{
    /// <summary>Raw stored confidence at or above which a field is accepted automatically.</summary>
    public const double AutoAcceptThreshold = 0.90;

    /// <summary>Confidence written onto an officialized start date or date-derived status.</summary>
    public const double OfficialConfidence = 1.0;

    public const string AutoAccepted = "auto_accepted";
    public const string HumanAccepted = "human_accepted";
    public const string ReviewRequired = "review_required";

    public const string StatusFieldName = "status";
    public const string StartDateFieldName = "startDate";

    /// <summary>In-force contract. Same literal the review screen and correction history already
    /// persist (screenshot: "active") — not a parallel vocabulary.</summary>
    public const string StatusActive = "active";

    /// <summary>Not in force on the evaluation date. Same literal
    /// <c>ContractCorrectionServiceTests</c> already uses for a lapsed contract.</summary>
    public const string StatusExpired = "expired";

    /// <summary>Spec §7.3's five critical fields — judged against the same bar as every other
    /// field. There is no always-review list. <see cref="StartDateFieldName"/> and
    /// <see cref="StatusFieldName"/> are the opposite: they are never left <see cref="ReviewRequired"/>
    /// when a value can be officialized or derived.</summary>
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

    /// <summary>
    /// Field-aware decision. An extracted <see cref="StartDateFieldName"/> is always
    /// <see cref="AutoAccepted"/> — presence in the document is the signal, not the model's
    /// percentage. Every other field still uses <see cref="Decide(double?)"/>.
    /// </summary>
    public static string Decide(string fieldName, double? confidence) =>
        IsExtractedStartDate(fieldName) ? AutoAccepted : Decide(confidence);

    public static bool RequiresReview(double? confidence) =>
        Decide(confidence) == ReviewRequired;

    public static bool IsAutoAccepted(double? confidence) =>
        Decide(confidence) == AutoAccepted;

    public static bool IsExtractedStartDate(string fieldName) =>
        string.Equals(fieldName, StartDateFieldName, StringComparison.OrdinalIgnoreCase);

    public static bool IsStatusField(string fieldName) =>
        string.Equals(fieldName, StatusFieldName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when a persisted evidence <c>Decision</c> still needs a human to officialize it.
    /// <see cref="ReviewRequired"/> and a null (legacy row written before the column existed)
    /// both block review; <see cref="AutoAccepted"/> and <see cref="HumanAccepted"/> do not.
    /// </summary>
    public static bool StillRequiresHumanDecision(string? decision) =>
        !string.Equals(decision, AutoAccepted, StringComparison.Ordinal)
        && !string.Equals(decision, HumanAccepted, StringComparison.Ordinal);

    /// <summary>
    /// True when a persisted evidence row is a fact a human should still confirm — the one rule
    /// behind the Documents row's "Review N fields" count and the auto-validation of a document
    /// with nothing left to review. A human or automatic acceptance is never weak, even when the
    /// model's score sits below the bar; an explicit <see cref="ReviewRequired"/> always is.
    /// </summary>
    public static bool IsWeakEvidence(double? confidence, string? decision)
    {
        if (string.Equals(decision, HumanAccepted, StringComparison.Ordinal)
            || string.Equals(decision, AutoAccepted, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(decision, ReviewRequired, StringComparison.Ordinal))
        {
            return true;
        }

        return RequiresReview(confidence);
    }

    /// <summary>
    /// Derives contract status from the official start/end dates. Active when the UTC evaluation
    /// date falls inside the in-force window: started (no start, or start ≤ today) and not ended
    /// (no end, or end ≥ today). Otherwise <see cref="StatusExpired"/>. <see langword="null"/>
    /// when neither date is known — there is nothing to derive from.
    /// </summary>
    public static string? DeriveStatus(DateOnly? startDate, DateOnly? endDate, DateOnly today)
    {
        if (startDate is null && endDate is null)
        {
            return null;
        }

        var started = startDate is null || startDate.Value <= today;
        var notEnded = endDate is null || endDate.Value >= today;
        return started && notEnded ? StatusActive : StatusExpired;
    }
}
