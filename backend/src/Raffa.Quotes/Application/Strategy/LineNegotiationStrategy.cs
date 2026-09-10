using Raffa.SharedKernel;

namespace Raffa.Quotes.Application.Strategy;

/// <summary>
/// One <see cref="Raffa.Quotes.Domain.QuoteLine"/>'s negotiation strategy — the per-line unit
/// <see cref="NegotiationStrategyService.GenerateAsync"/> assembles from
/// <see cref="Raffa.Quotes.Application.Assessment.MarketAssessmentService"/>'s own
/// <c>LineMarketAssessment.TargetSaving</c> (task E05/F02/US01/T02, target-saving) via
/// <see cref="NegotiationStrategyCalculator.Compute"/> (task E05/F03/US01/T01, negotiation-strategy;
/// parent story us-01-negotiation-strategy AC-1). Kept at line granularity, never rolled up to one
/// quote-level strategy, for the identical reason
/// <c>Raffa.Quotes.Application.Assessment.QuoteMarketAssessment</c>'s own doc comment gives for
/// its own per-line <c>LineMarketAssessment</c> list: spec §12.1's own output table is a per-line
/// concept (a quote can legitimately mix lines with very different market positions), and no ADR/
/// spec names a deterministic way to collapse several lines' strategies into one.
/// </summary>
/// <param name="QuoteLineId">Which line this strategy is for.</param>
/// <param name="OpeningTarget">The assertive opening ask — see
/// <see cref="NegotiationStrategyCalculator"/>'s own doc comment for the exact formula.
/// <see langword="null"/> exactly when no recommended target range was available to anchor one —
/// see <see cref="Explanation"/> for why.</param>
/// <param name="AcceptableRangeLow">The low (more aggressive) end of the range this strategy would
/// accept — echoes <c>LineTargetSaving.RecommendedTargetLow</c> verbatim (not recomputed). Null
/// under the same condition as <see cref="OpeningTarget"/>.</param>
/// <param name="AcceptableRangeHigh">The high (more conservative) end of the acceptable range —
/// echoes <c>LineTargetSaving.RecommendedTargetHigh</c> verbatim. Null under the same condition as
/// <see cref="OpeningTarget"/>.</param>
/// <param name="WalkAwayThreshold">The escalation/walk-away ceiling — never above the line's own
/// current <c>QuoteLine.UnitPrice</c> (this strategy never recommends escalating past what is
/// already quoted). Null under the same condition as <see cref="OpeningTarget"/>.</param>
/// <param name="Levers">Always exactly the seven canonical <see cref="NegotiationLeverType"/>
/// values (see <see cref="NegotiationLever"/>'s own doc comment) when a strategy could be computed;
/// empty when it could not (<see cref="OpeningTarget"/> is <see langword="null"/>) — a lever
/// recommendation with no target range to negotiate around would itself be a fabrication (Appendix
/// C rule 10).</param>
/// <param name="Explanation">Human-readable, deterministic trace of what this strategy computed and
/// why — including an honest, named reason when every numeric field above is
/// <see langword="null"/> (never a silent, unexplained abstain — Appendix C rule 10), the same
/// convention <c>LineTargetSaving.Explanation</c> already established.</param>
public sealed record LineNegotiationStrategy(
    EntityId QuoteLineId,
    decimal? OpeningTarget,
    decimal? AcceptableRangeLow,
    decimal? AcceptableRangeHigh,
    decimal? WalkAwayThreshold,
    IReadOnlyList<NegotiationLever> Levers,
    string Explanation);
