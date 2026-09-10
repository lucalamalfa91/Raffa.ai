using Raffa.Documents.Contracts.Application;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// Proves task E04/F03/US01/T01's (savings-kpis) execution step: <see cref="PortfolioAnalysisCalculator"/>
/// computes spec §10.1's "Contracts Analyzed"/"Annual Spend Analyzed" KPIs from a batch of
/// <see cref="ContractAnalysisSnapshot"/>, with no database, no HTTP call and no LLM call anywhere in
/// the path — parent story us-01-savings-kpis AC-1. Mirrors
/// <c>Raffa.Renewals.Tests.PriorityScoreCalculatorTests</c>'s own plain-`new()`, no-DI-container
/// style; <see cref="PortfolioQueryService.GetAnalysisSummaryAsync"/> (the database-facing fetch
/// half that derives <see cref="ContractAnalysisSnapshot.HasCompletedProcessing"/> from the linked
/// <c>Document</c> rows) is proven separately, at the Testcontainers level, alongside this class's
/// sibling <c>PortfolioQueryServiceTests</c>.
/// </summary>
public sealed class PortfolioAnalysisCalculatorTests
{
    private readonly PortfolioAnalysisCalculator _calculator = new();

    private static ContractAnalysisSnapshot Snapshot(
        bool hasCompletedProcessing, string currency = "USD", decimal? annualSpend = 1_000m) =>
        new(Guid.NewGuid(), currency, annualSpend, hasCompletedProcessing);

    [Fact]
    public void No_contracts_produces_an_honest_zero_count_and_empty_spend_list()
    {
        var result = _calculator.Summarize([]);

        Assert.Equal(0, result.ContractsAnalyzedCount);
        Assert.Empty(result.AnnualSpendAnalyzed);
    }

    [Fact]
    public void A_contract_still_processing_does_not_count_as_analyzed()
    {
        // StagedExtractionService.EnsureContractAsync creates a bootstrap Contract shell before
        // extraction finishes, so "a Contract row exists" is not "analyzed" — only a linked
        // Document that reached DocumentProcessingStatus.Completed makes it so.
        var result = _calculator.Summarize([Snapshot(hasCompletedProcessing: false)]);

        Assert.Equal(0, result.ContractsAnalyzedCount);
        Assert.Empty(result.AnnualSpendAnalyzed);
    }

    [Fact]
    public void An_analyzed_contract_with_no_known_annual_spend_counts_but_contributes_no_spend()
    {
        // The two KPIs can legitimately have different denominators (Appendix C rule 10): counted
        // as analyzed, but its (unknown) AnnualSpend must not silently become a fabricated zero
        // inside the sum.
        var result = _calculator.Summarize([Snapshot(hasCompletedProcessing: true, annualSpend: null)]);

        Assert.Equal(1, result.ContractsAnalyzedCount);
        Assert.Empty(result.AnnualSpendAnalyzed);
    }

    [Fact]
    public void Same_currency_analyzed_contracts_are_summed_and_counted_together()
    {
        var contracts = new[]
        {
            Snapshot(hasCompletedProcessing: true, currency: "USD", annualSpend: 1_000m),
            Snapshot(hasCompletedProcessing: true, currency: "USD", annualSpend: 2_500m),
            Snapshot(hasCompletedProcessing: false, currency: "USD", annualSpend: 9_999m), // excluded
        };

        var result = _calculator.Summarize(contracts);

        Assert.Equal(2, result.ContractsAnalyzedCount);
        var usd = Assert.Single(result.AnnualSpendAnalyzed);
        Assert.Equal("USD", usd.Currency);
        Assert.Equal(3_500m, usd.Amount);
        Assert.Equal(2, usd.ContractCount);
    }

    [Fact]
    public void Different_currencies_never_get_conflated_into_one_amount()
    {
        var contracts = new[]
        {
            Snapshot(hasCompletedProcessing: true, currency: "USD", annualSpend: 1_000m),
            Snapshot(hasCompletedProcessing: true, currency: "CHF", annualSpend: 9_000m),
        };

        var result = _calculator.Summarize(contracts);

        Assert.Equal(2, result.ContractsAnalyzedCount);
        Assert.Equal(2, result.AnnualSpendAnalyzed.Count);
        var usd = Assert.Single(result.AnnualSpendAnalyzed, b => b.Currency == "USD");
        var chf = Assert.Single(result.AnnualSpendAnalyzed, b => b.Currency == "CHF");
        Assert.Equal(1_000m, usd.Amount);
        Assert.Equal(9_000m, chf.Amount);
    }

    [Fact]
    public void Differently_cased_same_currency_rows_are_merged_into_one_bucket()
    {
        // Nothing on the write side normalizes currency-code casing (ContractCorrectionService's
        // currency field has no format check; LLM-extracted currency is only .Trim()-ed), so "USD"
        // and "usd" must be treated as the same currency here — the same case-insensitive
        // convention PriceNormalizationCalculator already applies when comparing currencies. A
        // case-sensitive grouping would silently fragment this into two rows instead of summing
        // them into one.
        var contracts = new[]
        {
            Snapshot(hasCompletedProcessing: true, currency: "USD", annualSpend: 1_000m),
            Snapshot(hasCompletedProcessing: true, currency: "usd", annualSpend: 2_500m),
        };

        var result = _calculator.Summarize(contracts);

        Assert.Equal(2, result.ContractsAnalyzedCount);
        var usd = Assert.Single(result.AnnualSpendAnalyzed);
        Assert.Equal(3_500m, usd.Amount);
        Assert.Equal(2, usd.ContractCount);
    }

    [Fact]
    public void Currency_buckets_are_ordered_deterministically_by_currency_code()
    {
        var contracts = new[]
        {
            Snapshot(hasCompletedProcessing: true, currency: "USD"),
            Snapshot(hasCompletedProcessing: true, currency: "CHF"),
            Snapshot(hasCompletedProcessing: true, currency: "EUR"),
        };

        var result = _calculator.Summarize(contracts);

        Assert.Equal(["CHF", "EUR", "USD"], result.AnnualSpendAnalyzed.Select(b => b.Currency));
    }

    [Fact]
    public void Rejects_a_null_argument()
    {
        Assert.Throws<ArgumentNullException>(() => _calculator.Summarize(null!));
    }
}
