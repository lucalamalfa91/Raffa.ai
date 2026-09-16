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
/// One tenant-scoped <see cref="Domain.RealizedSavings"/> row, reduced to the two fields
/// <see cref="SavingsKpiCalculator"/> needs for the verified-money KPI (task E20/F01/US01/T01,
/// NW-72). A thin projection, not the real entity, so the calculator stays a pure unit — the
/// second input sequence to <see cref="SavingsKpiCalculator.Summarize"/>, never a service
/// (Appendix C rule 6).
/// </summary>
public sealed record RealizedSavingsSnapshot(string Currency, decimal Amount);

/// <summary>
/// One currency's worth of a savings KPI bucket — spec §10.1's own "potential range" wording for
/// "Savings Identified" (and, by the same shape, "Savings In Progress"): never a single collapsed
/// number, always the <see cref="Low"/>/<see cref="High"/> range
/// <see cref="Domain.SavingsOpportunity.EstimatedSavingsLow"/>/<see cref="Domain.SavingsOpportunity.EstimatedSavingsHigh"/>
/// already carry. Grouped by <see cref="Currency"/> rather than summed into one bare decimal —
/// this codebase has no currency-conversion service anywhere (same reasoning
/// <see cref="Domain.SavingsOpportunity.Currency"/>'s own doc comment gives), so silently adding a
/// CHF amount to a USD amount would misstate the total, not merely round it. <see cref="Count"/>
/// and <see cref="AverageConfidence"/> (the mean of every contributing opportunity's own
/// <see cref="Domain.SavingsOpportunity.Confidence"/>) are the honest "how much should this range be
/// trusted" signal the parent story's AC-3 ("never fabricated precision") asks for at the
/// aggregate level — a bare sum with no confidence indicator would itself overstate certainty.
/// Verified money is a different kind of figure and uses <see cref="RealizedSavingsByCurrency"/>.
/// </summary>
public sealed record SavingsRangeByCurrency(
    string Currency,
    decimal Low,
    decimal High,
    int Count,
    double AverageConfidence);

/// <summary>
/// One currency's worth of verified savings — a single <see cref="Amount"/> summed from
/// <see cref="Domain.RealizedSavings.Amount"/> rows, never an estimate band. Grouped by
/// <see cref="Currency"/> rather than summed across them (no conversion service exists).
/// <see cref="Count"/> is the number of recorded outcomes in that currency, so the label can
/// honestly say "€X verified across N outcomes".
/// </summary>
public sealed record RealizedSavingsByCurrency(
    string Currency,
    decimal Amount,
    int Count);

/// <summary>
/// The three spec §10.1 savings dashboard buckets — "Savings Identified" (potential range or
/// approved opportunity), "Savings In Progress" (approved/negotiating opportunities) and "Savings
/// Realized" (verified negotiated/implemented savings) — each grouped by currency.
/// <see cref="Identified"/> and <see cref="InProgress"/> stay estimate bands
/// (<see cref="SavingsRangeByCurrency"/>). <see cref="Realized"/> is verified money from
/// <see cref="Domain.RealizedSavings"/> rows (<see cref="RealizedSavingsByCurrency"/>), not the
/// estimate range of opportunities whose status happens to be Realized — an opportunity with no
/// realized row contributes nothing, and an outcome that did not propagate a realized value
/// (<c>savingsPropagated: null</c>) never reaches this member.
/// </summary>
public sealed record SavingsKpiSummary(
    IReadOnlyList<SavingsRangeByCurrency> Identified,
    IReadOnlyList<SavingsRangeByCurrency> InProgress,
    IReadOnlyList<RealizedSavingsByCurrency> Realized);

/// <summary>
/// Implements task E04/F03/US01/T01 (savings-kpis)'s "Savings Identified"/"Savings In
/// Progress"/"Savings Realized" procurement-homepage KPIs (product spec §10.1; parent story
/// us-01-savings-kpis AC-1), with the verified-money read from task E20/F01/US01/T01 (NW-72).
/// Pure and synchronous — no database call, no HTTP call, no LLM call
/// (Appendix C rule 6) — same convention <c>Raffa.Renewals.Application.RenewalPipelineBuilder</c>/
/// <c>PriorityScoreCalculator</c> and <c>Raffa.Savings.Application.PriceNormalizationCalculator</c>
/// already follow for this codebase's other deterministic aggregations.
/// </summary>
public sealed class SavingsKpiCalculator
{
    /// <summary>
    /// Buckets <paramref name="opportunities"/> by <see cref="Domain.SavingsOpportunityStatus"/>
    /// into Identified/InProgress estimate ranges, then groups <paramref name="realizedSavings"/>
    /// by currency into verified amounts. A tenant with no opportunities (or none in a given
    /// status) and no realized rows gets an honestly empty list for that bucket, never a
    /// fabricated zero-currency row (Appendix C rule 10). Realized opportunity status does not
    /// feed <see cref="SavingsKpiSummary.Realized"/> — only the second sequence does.
    /// </summary>
    public SavingsKpiSummary Summarize(
        IEnumerable<SavingsOpportunitySnapshot> opportunities,
        IEnumerable<RealizedSavingsSnapshot> realizedSavings)
    {
        ArgumentNullException.ThrowIfNull(opportunities);
        ArgumentNullException.ThrowIfNull(realizedSavings);

        var materialized = opportunities.ToList();

        return new SavingsKpiSummary(
            Bucket(materialized, SavingsOpportunityStatus.Identified),
            Bucket(materialized, SavingsOpportunityStatus.InProgress),
            BucketRealized(realizedSavings));
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

    private static IReadOnlyList<RealizedSavingsByCurrency> BucketRealized(
        IEnumerable<RealizedSavingsSnapshot> realizedSavings) =>
        realizedSavings
            .GroupBy(r => r.Currency, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new RealizedSavingsByCurrency(
                g.Key,
                g.Sum(r => r.Amount),
                g.Count()))
            .ToList();
}
