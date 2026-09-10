namespace Raffa.Quotes.Application.Strategy;

/// <summary>
/// The fixed negotiation-lever vocabulary spec §12.1's own "Negotiation recommendation" output
/// table names verbatim in its "Levers" row example: "Volume, term, utilization, alternatives,
/// quarter-end, bundle, payment terms" (task E05/F03/US01/T01, negotiation-strategy; parent story
/// us-01-negotiation-strategy AC-1). Not extensible at runtime — a closed, spec-named set, the same
/// "small, fixed, ADR/spec-named vocabulary, not a free-text/DB-driven list" treatment
/// <c>Raffa.Quotes.Domain.MarketPosition</c> and <c>Raffa.Quotes.Domain.SkuMatchStatus</c>
/// already get in this module.
/// </summary>
public enum NegotiationLeverType
{
    /// <summary>Order-size leverage — spec §12.1's own first-listed lever.</summary>
    Volume,

    /// <summary>Commitment-length leverage (a longer term traded for a lower rate).</summary>
    Term,

    /// <summary>Actual-consumption-vs-contracted-tier leverage.</summary>
    Utilization,

    /// <summary>A competing supplier quote as leverage.</summary>
    Alternatives,

    /// <summary>Timing the ask to the supplier's own calendar-quarter close.</summary>
    QuarterEnd,

    /// <summary>Consolidating multiple products/lines into one ask.</summary>
    Bundle,

    /// <summary>Payment-timing leverage (e.g. faster payment for a price concession).</summary>
    PaymentTerms,
}
