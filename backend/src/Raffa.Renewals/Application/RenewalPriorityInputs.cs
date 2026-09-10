using Raffa.Renewals.Domain;

namespace Raffa.Renewals.Application;

/// <summary>
/// The raw facts <see cref="PriorityScoreCalculator"/> needs beyond what
/// <see cref="RenewalEngine"/> already computes (product spec §9.2 "Priority score": "Combine
/// spend, time urgency, benchmark opportunity, uplift risk and contract risk into a priority
/// score"; task E03/F01/US02/T01, parent story us-02-priority-score AC-1). Time urgency is
/// deliberately not repeated here — <see cref="PriorityScoreCalculator.Calculate"/> takes a real
/// <see cref="RenewalCalculationResult"/> for that, so "days until renewal" is always the one
/// deterministic computation <see cref="RenewalEngine"/> already owns (Appendix C rule 6), never a
/// second, possibly-inconsistent copy of that arithmetic.
///
/// <para>
/// Deliberately does not reference <c>Raffa.Documents.Contracts.Domain.Contract</c> or call
/// <c>Raffa.Benchmark.IBenchmarkService</c> (still an R0 placeholder with no query operations —
/// see <see cref="BenchmarkMarketPositionPercent"/>) — the same ADR-002 dependency-direction
/// reason and the same "small DTO a composition root maps onto later" pattern
/// <see cref="ContractRenewalTerms"/> already documents for this module. A caller supplies these
/// values however it likes today (a test's fixture data); mapping real
/// <c>Contract</c>/<c>Risk</c> rows and a real R3 Benchmark Service result onto this DTO is
/// follow-up composition work, out of this task's file scope the same way
/// <see cref="ContractRenewalTerms"/>'s own gap is.
/// </para>
/// </summary>
/// <param name="AnnualSpend">The source <c>Contract.AnnualSpend</c> — feeds the spend-weight
/// component. Null when spend has not been extracted/validated yet; a non-positive value is
/// treated the same as unknown (spend weight cannot be negative).</param>
/// <param name="AnnualUpliftPercent">The renewal price increase, as a percentage (spec §9.3's own
/// renewal-insight-card example: <c>"Annual uplift: 7%"</c> is <c>7m</c>) — feeds the
/// price-increase-risk component (spec §9.2's "Price Increase Risk", parent story AC-1's "uplift
/// risk" — the same fact). Null when not known.</param>
/// <param name="ContractRisk">The highest <see cref="ContractRiskLevel"/> across the contract's
/// risk rows (mirrors <c>PortfolioListItem.Risk</c>) — feeds the contract-risk component. Null
/// when no risk has been assessed for this contract yet.</param>
/// <param name="BenchmarkMarketPositionPercent">
/// How far the contract's spend sits above (positive) or below (negative) the R3 Benchmark
/// Service's market rate for the same category (spec §9.3's own example:
/// <c>"Market position: 18% above benchmark"</c> is <c>18m</c>) — feeds the benchmark-opportunity
/// component. Null when the R3 Benchmark Service has not produced a comparison for this contract —
/// today that is always, since <c>Raffa.Benchmark.IBenchmarkService</c> is still an R0
/// placeholder with no query operations (parent story AC-3: "Benchmark-opportunity component reads
/// the R3 benchmark only when available (else neutral)").
/// </param>
public sealed record RenewalPriorityInputs(
    decimal? AnnualSpend,
    decimal? AnnualUpliftPercent,
    ContractRiskLevel? ContractRisk,
    decimal? BenchmarkMarketPositionPercent)
{
    /// <summary>Every raw fact unknown — every component defaults to its documented no-data value
    /// (benchmark: neutral; every other component: the minimum). Convenience for a caller that has
    /// only a <see cref="RenewalCalculationResult"/> to score, mirroring
    /// <c>Raffa.Documents.Contracts.Application.PortfolioFilter.None</c>'s "well-known default"
    /// pattern.</summary>
    public static readonly RenewalPriorityInputs Unknown = new(null, null, null, null);
}
