using Raffa.SharedKernel;

namespace Raffa.Insights.Contracts;

/// <summary>
/// One contract's raw facts for <c>Raffa.Insights.Criticality.CriticalityScoreCalculator.Calculate</c>
/// (task E13/F07/US01/T01, insights-calculators; parent story us-01-insights AC-1; product spec
/// §12.1/§9.2, R-PORT-01). ADR-002 ("Insights takes DTOs, never <c>Contract</c>/<c>Renewal</c>
/// entities; composition in <c>Raffa.Api</c>") and <c>Raffa.Insights</c>'s own allow-list
/// (<c>[SharedKernel, Benchmark]</c>) mean this record can never carry a real
/// <c>Raffa.Documents.Contracts.Domain.Contract</c>/<c>Raffa.Renewals.Application
/// .PriorityScoreResult</c> — only <c>Raffa.Api</c>, "the one project allowed to reference every
/// module", can build one, the same composition-root pattern
/// <c>Raffa.Renewals.Application.RenewalPriorityInputs</c>/<c>ContractRenewalTerms</c> already
/// establish for the identical dependency-direction reason.
/// </summary>
/// <param name="ContractId">Which contract this scores.</param>
/// <param name="RenewalUrgency">The renewal-priority-score-shaped input the renewal-urgency
/// component normalizes to 0-1 — see <see cref="RenewalUrgencyInputs"/>'s own doc comment.</param>
/// <param name="HighestRiskSeverity">The highest <see cref="CriticalityRiskSeverity"/> recorded
/// against this contract; <see langword="null"/> when no risk has been assessed yet (mirrors
/// <c>Raffa.Documents.Contracts.Application.PortfolioListItem.Risk</c>'s own "null = none
/// recorded" convention).</param>
/// <param name="AnnualSpend">This contract's own annual spend, in whatever currency
/// <paramref name="PortfolioAnnualSpend"/> is also expressed in (the composition root's job to keep
/// the two consistent — see <c>Raffa.Api.InsightsEndpointExtensions</c>'s own doc comment: no
/// currency-conversion service exists anywhere in this codebase, so the two must already share a
/// currency before either reaches this record). <see langword="null"/> when spend has not been
/// extracted/validated yet.</param>
/// <param name="PortfolioAnnualSpend">The tenant's total annual spend across every contract sharing
/// <paramref name="AnnualSpend"/>'s currency — the spend-weight component's denominator. Zero (not
/// null) when the composition root found no comparable spend at all, so the calculator can divide
/// without a separate null check.</param>
/// <param name="SavingsPotential">The raw facts the savings-potential component chooses between —
/// see <see cref="SavingsPotentialInputs"/>'s own doc comment.</param>
/// <param name="CriticalFacts">Confidence, in [0, 1], for every fact this contract's critical-facts
/// pool tracks (risks + priced lines with a recorded unit price — see
/// <c>Raffa.Api.InsightsEndpointExtensions</c>'s own doc comment for exactly which). Empty, not
/// null, when nothing is tracked yet.</param>
public sealed record ContractCriticalityInputs(
    EntityId ContractId,
    RenewalUrgencyInputs RenewalUrgency,
    CriticalityRiskSeverity? HighestRiskSeverity,
    decimal? AnnualSpend,
    decimal PortfolioAnnualSpend,
    SavingsPotentialInputs SavingsPotential,
    IReadOnlyList<CriticalFactConfidence> CriticalFacts);

/// <summary>
/// A renewal-priority-score-shaped input (task E13/F07/US01/T01; R-PORT-01: "renewal urgency
/// (existing priority score, normalized)") — mirrors
/// <c>Raffa.Renewals.Application.PriorityScoreResult.TotalScore</c>'s own 0-100 scale (under the
/// product-spec-default <c>PriorityScoreWeightsOptions</c>, five components at a maximum of 20
/// each — <c>Raffa.Renewals.Application.PriorityScoreCalculator.MaxTotalScore</c>) without
/// referencing that type directly — <c>Raffa.Insights</c> cannot reference
/// <c>Raffa.Renewals</c> (ADR-002; allow-list <c>[SharedKernel, Benchmark]</c>). A composition
/// root that tunes <c>PriorityScoreWeightsOptions</c> away from the spec default changes what
/// <see cref="TotalScore"/>'s own true maximum is; this record does not carry that configured
/// maximum, so <c>CriticalityScoreCalculator</c> always normalizes against the spec-default 100 —
/// documented, not silently assumed (see that calculator's own doc comment).
/// </summary>
/// <param name="TotalScore"><c>PriorityScoreResult.TotalScore</c> echoed verbatim, 0-100 under the
/// spec-default weights; <see langword="null"/> when no renewal/priority computation exists for this
/// contract yet (never a data gap this record invents a number for — Appendix C rule 10).</param>
public sealed record RenewalUrgencyInputs(decimal? TotalScore);

/// <summary>
/// Mirrors <c>Raffa.Documents.Contracts.Domain.RiskSeverity</c>'s four levels plus an explicit
/// <see cref="None"/> for "nothing recorded" — the same "own enum, composition maps 1:1" shape
/// <c>Raffa.Renewals.Domain.ContractRiskLevel</c>'s own doc comment already documents for the
/// identical ADR-002 dependency-direction reason (<c>Raffa.Insights</c> cannot reference
/// <c>Raffa.Documents.Contracts</c>).
/// </summary>
public enum CriticalityRiskSeverity
{
    /// <summary>No risk has been assessed against this contract — distinct from
    /// <see cref="Low"/> (a risk was found and judged low-severity); see
    /// <c>CriticalityScoreCalculator</c>'s own doc comment for why "no data" and "known-low" score
    /// differently (Appendix C rule 10: an unknown fact must not silently read as the best
    /// case).</summary>
    None,
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>
/// The two alternative signals R-PORT-01's savings-potential component chooses between (task
/// E13/F07/US01/T01's own coding objective, verbatim: "above-band lines × spend or the savings
/// opportunity range midpoint ÷ spend, capped 1"). <c>CriticalityScoreCalculator</c> prefers
/// <see cref="AboveBandLineFraction"/> when present (weighted by this contract's own already-computed
/// spend-weight score, so a large above-band contract scores higher than a small one) and falls back
/// to <see cref="SavingsOpportunityRangeLow"/>/<see cref="SavingsOpportunityRangeHigh"/>'s midpoint
/// otherwise — never both, never neither silently read as zero opportunity (see that calculator's
/// own doc comment for the exact formula and the honest zero-signal case).
/// </summary>
/// <param name="AboveBandLineFraction">Fraction (0-1) of this contract's priced lines whose unit
/// price sits above the matched Benchmark Service band — <see langword="null"/> when no per-line
/// benchmark match exists for this contract yet (today: always, since <c>Contract</c> has no
/// supplier-name/geography field to build a <c>Raffa.Benchmark.Contracts.BenchmarkQuery</c> from —
/// the same gap <c>Raffa.Documents.Contracts.Application.Contract360Result.Benchmark</c>'s own doc
/// comment already names; see <c>Raffa.Api.InsightsEndpointExtensions</c>).</param>
/// <param name="SavingsOpportunityRangeLow">The conservative end of this contract's aggregated
/// <c>Raffa.Savings.Domain.SavingsOpportunity</c> estimated-savings range (summed across every
/// opportunity recorded against this contract) — <see langword="null"/> when none exist.</param>
/// <param name="SavingsOpportunityRangeHigh">The aggressive end of the same range — always
/// <c>&gt;=</c> <see cref="SavingsOpportunityRangeLow"/> when both are populated.</param>
public sealed record SavingsPotentialInputs(
    decimal? AboveBandLineFraction,
    decimal? SavingsOpportunityRangeLow,
    decimal? SavingsOpportunityRangeHigh);

/// <summary>
/// One tracked critical fact's confidence (task E13/F07/US01/T01's own coding objective: "open
/// critical facts (count of critical fields below 0.8 ÷ number of critical fields...)"). Opaque to
/// <c>CriticalityScoreCalculator</c> — it only ever counts how many entries fall below the weak
/// threshold, never inspects <see cref="FieldKey"/> itself — so the composition root
/// (<c>Raffa.Api.InsightsEndpointExtensions</c>) decides what counts as "critical" for a given
/// contract (today: recorded risks + priced lines with a unit price, the two entities Appendix C
/// rule 2 already requires evidence/confidence on) without this record needing to change.
/// </summary>
/// <param name="FieldKey">A citation-key-shaped pointer to which fact this is, e.g.
/// <c>"priced-line[2].unitPrice"</c> — see <see cref="InsightsCitationKeys"/>.</param>
/// <param name="Confidence">Raffa's own confidence score in [0, 1] for this fact (Appendix C rule
/// 2's "confidence metadata") — never fabricated: the composition root only includes a fact here
/// when a real, recorded confidence exists for it.</param>
public sealed record CriticalFactConfidence(string FieldKey, double Confidence);
