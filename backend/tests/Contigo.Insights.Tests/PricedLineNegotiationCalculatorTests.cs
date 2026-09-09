using Contigo.Benchmark.Contracts;
using Contigo.Insights.Contracts;
using Contigo.Insights.Negotiation;
using Contigo.Quotes.Application.Assessment;
using Contigo.Quotes.Application.Strategy;
using Contigo.Quotes.Domain;
using Contigo.SharedKernel;

namespace Contigo.Insights.Tests;

/// <summary>
/// Proves task E13/F07/US01/T01's <see cref="PricedLineNegotiationCalculator"/> — parent story
/// us-01-insights AC-3 ("a contract line item yields opening target, acceptable range, walk-away
/// and levers exactly as a quote line does; existing quote tests still pass unchanged") and AC-4
/// ("no benchmark match → no targets, levers only, 'insufficient market data'"). Mirrors
/// <c>Contigo.Quotes.Tests.NegotiationStrategyCalculatorTests</c>'s own shape/style.
/// </summary>
public sealed class PricedLineNegotiationCalculatorTests
{
    private static readonly DateOnly MidQuarterDate = new(2026, 2, 10);

    private static PricedLine Line(
        decimal? quantity = 100m,
        int? termMonths = 12,
        decimal? unitPrice = 2300m,
        BenchmarkDistribution? benchmark = null) =>
        new("SKU-1", "Sales Cloud Enterprise", quantity, unitPrice, "USD", termMonths, benchmark, SampleSize: 42);

    // ----- AC-3: equal to the quote calculator's own output for identical inputs -----

    [Fact]
    public void Priced_line_targets_equal_the_quote_calculators_for_identical_inputs()
    {
        var unitPrice = 2300m;
        var quantity = 100m;
        var distribution = new BenchmarkDistribution(P25: 1500m, P50: 1800m, P75: 2100m);
        var benchmarkResult = new BenchmarkResult(
            distribution, "per seat / year", "USD", Confidence: 0.9, Source: "fixture",
            UpdatedAt: DateTimeOffset.UtcNow,
            ComparisonDimensions: [BenchmarkComparisonDimension.Supplier, BenchmarkComparisonDimension.Product]);

        // ---- Quotes' own calculator, from a QuoteLine ----
        var targetSaving = TargetSavingCalculator.Compute(unitPrice, quantity, benchmarkResult);
        var quoteLine = new QuoteLine
        {
            TenantId = TenantId.New(),
            QuoteId = EntityId.New(),
            Description = "Sales Cloud Enterprise",
            Quantity = quantity,
            UnitPrice = unitPrice,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var quoteStrategy = NegotiationStrategyCalculator.Compute(
            quoteLine, totalLineCountOnQuote: 1, targetSaving, MidQuarterDate);

        // ---- Insights' own calculator, from an equivalent PricedLine ----
        var pricedLine = Line(quantity: quantity, unitPrice: unitPrice, benchmark: distribution);
        var contractResult = PricedLineNegotiationCalculator.Compute(
            EntityId.New(), pricedLine, lineIndex: 0, totalPricedLineCountOnContract: 1, MidQuarterDate);

        Assert.Equal(quoteStrategy.OpeningTarget, contractResult.OpeningTarget);
        Assert.Equal(quoteStrategy.AcceptableRangeLow, contractResult.AcceptableRangeLow);
        Assert.Equal(quoteStrategy.AcceptableRangeHigh, contractResult.AcceptableRangeHigh);
        Assert.Equal(quoteStrategy.WalkAwayThreshold, contractResult.WalkAwayThreshold);
    }

    [Fact]
    public void Walk_away_is_clamped_to_the_current_unit_price_same_as_the_quote_calculator()
    {
        // Reproduces spec §12.1's own illustrative example: range [410k, 440k] -> walk-away 470k.
        var distribution = new BenchmarkDistribution(P25: 410_000m, P50: 440_000m, P75: 500_000m);
        var line = Line(unitPrice: 520_000m, benchmark: distribution);

        var result = PricedLineNegotiationCalculator.Compute(EntityId.New(), line, 0, 1, MidQuarterDate);

        Assert.Equal(470_000m, result.WalkAwayThreshold);
    }

    // ----- AC-4: no benchmark match -> no targets, levers only, "insufficient market data" -----

    [Fact]
    public void No_benchmark_match_yields_no_targets_but_still_seven_levers()
    {
        var line = Line(benchmark: null);

        var result = PricedLineNegotiationCalculator.Compute(EntityId.New(), line, 0, 1, MidQuarterDate);

        Assert.Null(result.OpeningTarget);
        Assert.Null(result.AcceptableRangeLow);
        Assert.Null(result.AcceptableRangeHigh);
        Assert.Null(result.WalkAwayThreshold);
        Assert.Contains("insufficient market data", result.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(7, result.Levers.Count);
    }

    [Fact]
    public void No_unit_price_yields_no_targets_but_still_seven_levers()
    {
        var line = Line(unitPrice: null, benchmark: new BenchmarkDistribution(1500m, 1800m, 2100m));

        var result = PricedLineNegotiationCalculator.Compute(EntityId.New(), line, 0, 1, MidQuarterDate);

        Assert.Null(result.OpeningTarget);
        Assert.Contains("insufficient market data", result.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(7, result.Levers.Count);
    }

    [Fact]
    public void Not_well_ordered_distribution_yields_no_targets_but_still_seven_levers()
    {
        var line = Line(benchmark: new BenchmarkDistribution(P25: 2000m, P50: 1800m, P75: 2100m));

        var result = PricedLineNegotiationCalculator.Compute(EntityId.New(), line, 0, 1, MidQuarterDate);

        Assert.Null(result.OpeningTarget);
        Assert.Contains("insufficient market data", result.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(7, result.Levers.Count);
    }

    // ----- Always all seven levers, in spec §12.1's own order -----

    [Fact]
    public void Always_returns_exactly_the_seven_canonical_levers_in_spec_order()
    {
        var line = Line(benchmark: new BenchmarkDistribution(1500m, 1800m, 2100m));

        var result = PricedLineNegotiationCalculator.Compute(EntityId.New(), line, 0, 1, MidQuarterDate);

        Assert.Equal(
            [
                ContractNegotiationLeverType.Volume,
                ContractNegotiationLeverType.Term,
                ContractNegotiationLeverType.Utilization,
                ContractNegotiationLeverType.Alternatives,
                ContractNegotiationLeverType.QuarterEnd,
                ContractNegotiationLeverType.Bundle,
                ContractNegotiationLeverType.PaymentTerms,
            ],
            result.Levers.Select(l => l.LeverType));
    }

    [Fact]
    public void Volume_lever_cites_the_contract_fact_field_key_when_quantity_is_recorded()
    {
        var contractId = EntityId.New();
        var line = Line(quantity: 250m, benchmark: new BenchmarkDistribution(1500m, 1800m, 2100m));

        var result = PricedLineNegotiationCalculator.Compute(contractId, line, lineIndex: 2, 3, MidQuarterDate);

        var volume = Assert.Single(result.Levers, l => l.LeverType == ContractNegotiationLeverType.Volume);
        Assert.Contains("250", volume.Rationale);
        var key = Assert.Single(volume.CitationKeys);
        Assert.Equal($"fact:{contractId}:priced-line[2].quantity", key);
    }

    [Fact]
    public void Volume_lever_is_honest_and_uncited_when_no_quantity_is_recorded()
    {
        var line = Line(quantity: null, benchmark: new BenchmarkDistribution(1500m, 1800m, 2100m));

        var result = PricedLineNegotiationCalculator.Compute(EntityId.New(), line, 0, 1, MidQuarterDate);

        var volume = Assert.Single(result.Levers, l => l.LeverType == ContractNegotiationLeverType.Volume);
        Assert.Contains("No quantity is recorded", volume.Rationale);
        Assert.Empty(volume.CitationKeys);
    }

    [Fact]
    public void Term_lever_cites_the_contract_fact_field_key_when_term_months_is_recorded()
    {
        var contractId = EntityId.New();
        var line = Line(termMonths: 36, benchmark: new BenchmarkDistribution(1500m, 1800m, 2100m));

        var result = PricedLineNegotiationCalculator.Compute(contractId, line, lineIndex: 0, 1, MidQuarterDate);

        var term = Assert.Single(result.Levers, l => l.LeverType == ContractNegotiationLeverType.Term);
        Assert.Contains("36", term.Rationale);
        Assert.Equal($"fact:{contractId}:priced-line[0].termMonths", Assert.Single(term.CitationKeys));
    }

    [Fact]
    public void Bundle_lever_cites_the_priced_line_count_as_a_calc_key()
    {
        var line = Line(benchmark: new BenchmarkDistribution(1500m, 1800m, 2100m));

        var result = PricedLineNegotiationCalculator.Compute(EntityId.New(), line, 0, 3, MidQuarterDate);

        var bundle = Assert.Single(result.Levers, l => l.LeverType == ContractNegotiationLeverType.Bundle);
        Assert.Contains("3", bundle.Rationale);
        Assert.Equal("calc:priced-line-count", Assert.Single(bundle.CitationKeys));
    }

    [Fact]
    public void Utilization_alternatives_and_payment_terms_levers_are_always_generic_and_uncited()
    {
        var line = Line(benchmark: new BenchmarkDistribution(1500m, 1800m, 2100m));

        var result = PricedLineNegotiationCalculator.Compute(EntityId.New(), line, 0, 1, MidQuarterDate);

        var utilization = Assert.Single(result.Levers, l => l.LeverType == ContractNegotiationLeverType.Utilization);
        var alternatives = Assert.Single(result.Levers, l => l.LeverType == ContractNegotiationLeverType.Alternatives);
        var paymentTerms = Assert.Single(result.Levers, l => l.LeverType == ContractNegotiationLeverType.PaymentTerms);

        Assert.Empty(utilization.CitationKeys);
        Assert.Empty(alternatives.CitationKeys);
        Assert.Empty(paymentTerms.CitationKeys);
    }

    [Fact]
    public void Rejects_a_null_line_argument()
    {
        Assert.Throws<ArgumentNullException>(
            () => PricedLineNegotiationCalculator.Compute(EntityId.New(), null!, 0, 1, MidQuarterDate));
    }

    // ----- Determinism (Appendix C rule 6) -----

    [Fact]
    public void Same_inputs_produce_the_same_result_every_time()
    {
        var contractId = EntityId.New();
        var line = Line(benchmark: new BenchmarkDistribution(1500m, 1800m, 2100m));

        var first = PricedLineNegotiationCalculator.Compute(contractId, line, 0, 2, MidQuarterDate);
        var second = PricedLineNegotiationCalculator.Compute(contractId, line, 0, 2, MidQuarterDate);

        Assert.Equal(first.OpeningTarget, second.OpeningTarget);
        Assert.Equal(first.AcceptableRangeLow, second.AcceptableRangeLow);
        Assert.Equal(first.AcceptableRangeHigh, second.AcceptableRangeHigh);
        Assert.Equal(first.WalkAwayThreshold, second.WalkAwayThreshold);
        Assert.Equal(first.Explanation, second.Explanation);
        Assert.Equal(first.Levers.Select(l => l.LeverType), second.Levers.Select(l => l.LeverType));
        Assert.Equal(first.Levers.Select(l => l.Rationale), second.Levers.Select(l => l.Rationale));
    }
}
