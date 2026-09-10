namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// One tenant-scoped <see cref="Domain.Contract"/> row, reduced to exactly the fields
/// <see cref="PortfolioAnalysisCalculator"/> needs (task E04/F03/US01/T01, savings-kpis; product
/// spec §10.1 "Contracts Analyzed"/"Annual Spend Analyzed"). <see cref="HasCompletedProcessing"/>
/// is not a column on <see cref="Domain.Contract"/> itself — a contract row exists the moment
/// extraction *starts* (<c>Extraction.StagedExtractionService.EnsureContractAsync</c> creates a
/// bootstrap shell before any stage has run — see that method's own doc comment), so "a Contract
/// row exists" cannot mean "analyzed"; <see cref="PortfolioQueryService.GetAnalysisSummaryAsync"/>
/// (the fetch half — see that method's own doc comment) derives this flag from whether any of the
/// contract's linked <see cref="Domain.Document"/> rows reached
/// <see cref="Domain.DocumentProcessingStatus.Completed"/>, the same fact spec §10.1's own KPI
/// meaning names ("Contracts with completed processing").
/// </summary>
public sealed record ContractAnalysisSnapshot(
    Guid ContractId, string Currency, decimal? AnnualSpend, bool HasCompletedProcessing);

/// <summary>
/// One currency's worth of "Annual Spend Analyzed" (spec §10.1: "Spend represented by processed/
/// validated contracts"). Grouped by <see cref="Currency"/> rather than summed into one bare
/// decimal — <see cref="Domain.Contract.Currency"/> is a per-contract, free-form ISO code and this
/// codebase has no currency-conversion service anywhere (same reasoning
/// <c>Raffa.Savings.Domain.SavingsOpportunity.Currency</c>'s own doc comment gives for that
/// module), so silently adding a CHF contract's spend to a USD contract's would misstate the total.
/// </summary>
public sealed record AnnualSpendByCurrency(string Currency, decimal Amount, int ContractCount);

/// <summary>
/// The "Contracts Analyzed"/"Annual Spend Analyzed" pair of spec §10.1's procurement-homepage KPIs.
/// <see cref="ContractsAnalyzedCount"/> counts every analyzed contract regardless of whether its
/// <see cref="Domain.Contract.AnnualSpend"/> is known yet; <see cref="AnnualSpendAnalyzed"/> sums
/// only the contracts among those where it is — the two denominators can legitimately differ (an
/// analyzed contract with no extracted annual-spend fact still counts toward the first, not the
/// second), which is an honest reflection of what this codebase actually knows, not a bug
/// (Appendix C rule 10).
/// </summary>
public sealed record PortfolioAnalysisSummary(
    int ContractsAnalyzedCount, IReadOnlyList<AnnualSpendByCurrency> AnnualSpendAnalyzed);

/// <summary>
/// Implements task E04/F03/US01/T01 (savings-kpis)'s "Contracts Analyzed"/"Annual Spend Analyzed"
/// procurement-homepage KPIs (product spec §10.1; parent story us-01-savings-kpis AC-1). Pure and
/// synchronous — no database call, no HTTP call, no LLM call (Appendix C rule 6) — same convention
/// <c>Raffa.Renewals.Application.RenewalPipelineBuilder</c>/<c>PriorityScoreCalculator</c> and
/// <c>Raffa.Savings.Application.SavingsKpiCalculator</c> already follow for this codebase's other
/// deterministic aggregations.
/// </summary>
public sealed class PortfolioAnalysisCalculator
{
    /// <summary>
    /// Filters <paramref name="contracts"/> down to <see cref="ContractAnalysisSnapshot.HasCompletedProcessing"/>
    /// rows, then counts them and sums <see cref="ContractAnalysisSnapshot.AnnualSpend"/> per
    /// <see cref="ContractAnalysisSnapshot.Currency"/> (skipping a null <c>AnnualSpend</c> — see the
    /// summary record's own doc comment on why the two counts can differ). A tenant with no analyzed
    /// contracts at all gets an honest <c>(0, [])</c>, never a fabricated row.
    /// </summary>
    public PortfolioAnalysisSummary Summarize(IEnumerable<ContractAnalysisSnapshot> contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        var analyzed = contracts.Where(c => c.HasCompletedProcessing).ToList();

        var spendByCurrency = analyzed
            .Where(c => c.AnnualSpend is not null)
            // Case-insensitive: nothing on the write side (ContractCorrectionService's currency
            // field has no format check; LLM-extracted currency is only .Trim()-ed; the EF
            // configuration only constrains length) normalizes currency-code casing, so "USD" and
            // "usd" must bucket together, not fragment into two rows — the same
            // case-insensitive-currency convention Raffa.Savings.Application
            // .PriceNormalizationCalculator already established for this codebase.
            .GroupBy(c => c.Currency, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new AnnualSpendByCurrency(g.Key, g.Sum(c => c.AnnualSpend!.Value), g.Count()))
            .ToList();

        return new PortfolioAnalysisSummary(analyzed.Count, spendByCurrency);
    }
}
