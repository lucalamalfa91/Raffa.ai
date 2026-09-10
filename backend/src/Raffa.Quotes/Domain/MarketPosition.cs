namespace Raffa.Quotes.Domain;

/// <summary>
/// A quote line's price position against its matched Benchmark Service distribution (task
/// E05/F02/US01/T01, market-assessment; parent story us-01-market-assessment AC-2 "Flag
/// above/in-line/below market + recommended target range + potential saving" — this enum is the
/// "flag" half; the target range/potential saving are task-02's own, separate
/// <c>target-saving</c> artifact). Spec §4.4 verbatim: "Flag above/in-line/below market positions."
///
/// Computed by <c>Raffa.Quotes.Application.Assessment.MarketAssessmentCalculator.Classify</c> —
/// only meaningful when <c>MarketAssessmentStatus.Assessed</c> (see that enum's own doc comment);
/// never hand-set by a caller.
/// </summary>
public enum MarketPosition
{
    /// <summary>The line's price sits below the matched comparables' 25th percentile — cheaper than
    /// the great majority of the market (a favourable position for the buyer).</summary>
    BelowMarket,

    /// <summary>The line's price sits at or between the matched comparables' 25th and 75th
    /// percentile — within the normal, expected market band.</summary>
    InLine,

    /// <summary>The line's price sits above the matched comparables' 75th percentile — more
    /// expensive than the great majority of the market (a candidate for negotiation).</summary>
    AboveMarket,
}
