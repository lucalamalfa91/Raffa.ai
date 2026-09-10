namespace Raffa.Insights.Contracts;

/// <summary>
/// The same seven-lever vocabulary spec §12.1 names, generalized from
/// <c>Raffa.Quotes.Application.Strategy.NegotiationLeverType</c> for a contract priced line (task
/// E13/F07/US01/T01, insights-calculators; R-STR-02: "levers: volume, term, utilization,
/// alternatives, quarter-end, bundle, payment terms"). Its own enum, not a reference to the Quotes
/// one: <c>Raffa.Insights</c> cannot reference <c>Raffa.Quotes</c> (ADR-002; both modules'
/// allow-list is exactly <c>[SharedKernel, Benchmark]</c> —
/// <c>Raffa.ArchitectureTests.DependencyDirectionTests</c>), the same "own enum, same names"
/// treatment <c>Raffa.Renewals.Domain.ContractRiskLevel</c> already gives
/// <c>RiskSeverity</c>.
/// </summary>
public enum ContractNegotiationLeverType
{
    Volume,
    Term,
    Utilization,
    Alternatives,
    QuarterEnd,
    Bundle,
    PaymentTerms,
}

/// <summary>
/// One recommended negotiation lever plus its rationale, for a contract priced line (task
/// E13/F07/US01/T01; parent story us-01-insights AC-4's own "contract-level levers"; the
/// StrategyPack's "Where you can push" section: "levers with rationale + evidence keys").
/// Deliberately not <c>Raffa.Quotes.Application.Strategy.NegotiationLever</c>'s span/page/
/// confidence evidence shape: a <c>Raffa.Benchmark.Contracts.PricedLine</c> carries none of that
/// (it is a normalized, already-composed DTO, not a raw extraction row), so
/// <see cref="CitationKeys"/> points back to the fact/market/calculator source instead — see
/// <see cref="InsightsCitationKeys"/>.
/// </summary>
/// <param name="LeverType">Which of the seven canonical levers this is.</param>
/// <param name="Rationale">Human-readable, deterministic explanation of why this lever applies —
/// never an LLM call (Appendix C rule 6); a generic, always-available tactic when this line's own
/// data does not ground it more specifically (mirrors
/// <c>Raffa.Quotes.Application.Strategy.NegotiationLever</c>'s identical "grounded vs generic"
/// distinction).</param>
/// <param name="CitationKeys">Citation keys backing <see cref="Rationale"/> (<c>fact:</c>/
/// <c>market:</c>/<c>calc:</c> — <see cref="InsightsCitationKeys"/>); empty when the rationale is the
/// generic, ungrounded variant (no source exists for this line yet) — never fabricated to fill the
/// list (Appendix C rule 10).</param>
public sealed record ContractNegotiationLever(
    ContractNegotiationLeverType LeverType,
    string Rationale,
    IReadOnlyList<string> CitationKeys);

/// <summary>
/// The outcome of <c>Raffa.Insights.Negotiation.PricedLineNegotiationCalculator.Compute</c> for one
/// <c>Raffa.Benchmark.Contracts.PricedLine</c> (task E13/F07/US01/T01; parent story us-01-insights
/// AC-3/AC-4). Bundles both the numeric targets and the levers for that line — the same "one line,
/// one strategy" shape <c>Raffa.Quotes.Application.Strategy.LineNegotiationStrategy</c> already
/// establishes — but, unlike that type, <see cref="Levers"/> is never empty (parent story AC-4: "no
/// benchmark match → no targets, levers only, 'insufficient market data'" — see this type's own
/// calculator for why levers and targets abstain independently here, a deliberate divergence from
/// the Quotes calculator's "no range, no levers" rule).
/// </summary>
/// <param name="OpeningTarget">The assertive opening ask — <see langword="null"/> exactly when
/// <paramref name="line"/> has no usable benchmark band or unit price to anchor one; see
/// <see cref="Explanation"/> for why.</param>
/// <param name="AcceptableRangeLow">The low (more aggressive) end of the acceptable range. Null
/// under the same condition as <see cref="OpeningTarget"/>.</param>
/// <param name="AcceptableRangeHigh">The high (more conservative) end. Null under the same
/// condition.</param>
/// <param name="WalkAwayThreshold">The escalation/walk-away ceiling, never above this line's own
/// current unit price. Null under the same condition.</param>
/// <param name="Levers">Always exactly the seven canonical <see cref="ContractNegotiationLeverType"/>
/// values, in spec §12.1's own listed order — present even when the numeric targets above are all
/// null (AC-4).</param>
/// <param name="Explanation">Human-readable, deterministic trace of what this line's targets
/// computed and why — including an honest, named "insufficient market data" reason when the numeric
/// fields are null (Appendix C rule 10).</param>
public sealed record PricedLineNegotiationResult(
    decimal? OpeningTarget,
    decimal? AcceptableRangeLow,
    decimal? AcceptableRangeHigh,
    decimal? WalkAwayThreshold,
    IReadOnlyList<ContractNegotiationLever> Levers,
    string Explanation);
