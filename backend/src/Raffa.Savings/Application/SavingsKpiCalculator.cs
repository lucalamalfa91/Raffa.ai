using Raffa.Savings.Domain;

namespace Raffa.Savings.Application;

/// <summary>
/// One tenant-scoped <see cref="Domain.SavingsOpportunity"/> row, reduced to exactly the fields
/// <see cref="SavingsKpiCalculator"/> needs (task E04/F03/US01/T01, savings-kpis; product spec
/// §10.1 "Savings Identified" / "Savings In Progress" / "Savings Realized"). A thin projection,
/// not the real entity, so the calculator below stays a pure, database-free unit — the same
/// "fetch raw facts, then hand them to a calculator that has never seen a DbContext" split
/// <c>Raffa.Renewals.Application.RenewalPipelineBuilder</c> already establishes for this
/// codebase (<see cref="SavingsKpiQueryService"/> is this type's own fetch half).
/// </summary>
public sealed record SavingsOpportunitySnapshot(
    SavingsOpportunityStatus Status,
    string Currency,
    decimal EstimatedSavingsLow,
    decimal EstimatedSavingsHigh,
    double Confidence);

/// <summary>
/// One currency's worth of a savings KPI bucket — spec §10.1's own "potential range" wording for
/// "Savings Identified" (and, by the same shape, "Savings In Progress"/"Savings Realized"): never
/// a single collapsed number, always the <see cref="Low"/>/<see cref="High"/> range
/// <see cref="Domain.SavingsOpportunity.EstimatedSavingsLow"/>/<see cref="Domain.SavingsOpportunity.EstimatedSavingsHigh"/>
/// already carry. Grouped by <see cref="Currency"/> rather than summed into one bare decimal —
/// this codebase has no currency-conversion service anywhere (same reasoning
/// <see cref="Domain.SavingsOpportunity.Currency"/>'s own doc comment gives), so silently adding a
/// CHF amount to a USD amount would misstate the total, not merely round it. <see cref="Count"/>
/// and <see cref="AverageConfidence"/> (the mean of every contributing opportunity's own
/// <see cref="Domain.SavingsOpportunity.Confidence"/>) are the honest "how much should this range be
/// trusted" signal the parent story's AC-3 ("never fabricated precision") asks for at the
/// aggregate level — a bare sum with no confidence indicator would itself overstate certainty.
/// </summary>
public sealed record SavingsRangeByCurrency(
    string Currency,
    decimal Low,
    decimal High,
    int Count,
    double AverageConfidence);

/// <summary>
/// The three spec §10.1 savings dashboard buckets — "Savings Identified" (potential range or
/// approved opportunity), "Savings In Progress" (approved/negotiating opportunities) and "Savings
/// Realized" (verified negotiated/implemented savings) — each grouped by currency (see
/// <see cref="SavingsRangeByCurrency"/>'s own doc comment). <see cref="Realized"/> reflects every
/// <see cref="Domain.SavingsOpportunity"/> whose <see cref="Domain.SavingsOpportunityStatus"/> is
/// <see cref="SavingsOpportunityStatus.Realized"/> using that row's own estimated range — the same
/// honest, documented gap <see cref="SavingsOpportunityStatus.Realized"/>'s own doc comment names:
/// a distinct, audit-tracked verified realized-value record
/// (<c>Domain.RealizedSavings</c>, module-map.md's own second named entity for this module) is task
/// E04/F02/US02/T02's deliverable, not a dependency this task declares
/// (this task's wave-spec entry depends only on <c>savings-opportunity</c>), so this bucket is not
/// silently held back waiting for it.
/// </summary>
public sealed record SavingsKpiSummary(
    IReadOnlyList<SavingsRangeByCurrency> Identified,
    IReadOnlyList<SavingsRangeByCurrency> InProgress,
    IReadOnlyList<SavingsRangeByCurrency> Realized);

/// <summary>
/// Implements task E04/F03/US01/T01 (savings-kpis)'s "Savings Identified"/"Savings In
/// Progress"/"Savings Realized" procurement-homepage KPIs (product spec §10.1; parent story
/// us-01-savings-kpis AC-1). Pure and synchronous — no database call, no HTTP call, no LLM call
/// (Appendix C rule 6) — same convention <c>Raffa.Renewals.Application.RenewalPipelineBuilder</c>/
/// <c>PriorityScoreCalculator</c> and <c>Raffa.Savings.Application.PriceNormalizationCalculator</c>
/// already follow for this codebase's other deterministic aggregations.
/// </summary>
public sealed class SavingsKpiCalculator
{
    /// <summary>
    /// Buckets <paramref name="opportunities"/> by <see cref="Domain.SavingsOpportunityStatus"/>,
    /// then by <see cref="SavingsOpportunitySnapshot.Currency"/> within each bucket. A tenant with
    /// no opportunities at all (or none in a given status) gets an honestly empty list for that
    /// bucket, never a fabricated zero-currency row (Appendix C rule 10).
    /// </summary>
    public SavingsKpiSummary Summarize(IEnumerable<SavingsOpportunitySnapshot> opportunities)
    {
        ArgumentNullException.ThrowIfNull(opportunities);

        var materialized = opportunities.ToList();

        return new SavingsKpiSummary(
            Bucket(materialized, SavingsOpportunityStatus.Identified),
            Bucket(materialized, SavingsOpportunityStatus.InProgress),
            Bucket(materialized, SavingsOpportunityStatus.Realized));
    }

    private static IReadOnlyList<SavingsRangeByCurrency> Bucket(
        IReadOnlyList<SavingsOpportunitySnapshot> opportunities, SavingsOpportunityStatus status) =>
        opportunities
            .Where(o => o.Status == status)
            // Case-insensitive: nothing on the write side (SavingsOpportunityService.CreateAsync
            // validates only IsNullOrWhiteSpace; LLM-extracted currency is only .Trim()-ed; the EF
            // configuration only constrains length) normalizes currency-code casing, so "USD" and
            // "usd" must bucket together, not fragment into two rows — the same
            // case-insensitive-currency convention Raffa.Savings.Application
            // .PriceNormalizationCalculator already established for this module.
            .GroupBy(o => o.Currency, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SavingsRangeByCurrency(
                g.Key,
                g.Sum(o => o.EstimatedSavingsLow),
                g.Sum(o => o.EstimatedSavingsHigh),
                g.Count(),
                g.Average(o => o.Confidence)))
            .ToList();
}
