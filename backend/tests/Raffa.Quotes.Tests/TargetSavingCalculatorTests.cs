using Raffa.Benchmark.Contracts;
using Raffa.Quotes.Application.Assessment;

namespace Raffa.Quotes.Tests;

/// <summary>
/// Proves task E05/F02/US01/T02's execution step: <see cref="TargetSavingCalculator"/> computes a
/// deterministic recommended target range + potential saving for a quote line's price against an
/// already-fetched <see cref="BenchmarkResult"/> — parent story us-01-market-assessment AC-2's own
/// "recommended target range + potential saving" half — with no database, no HTTP call and no LLM
/// call anywhere in the path. Mirrors <c>Raffa.Quotes.Tests.MarketAssessmentCalculatorTests</c>
/// (the analogous above/in-line/below flag, task-01) and
/// <c>Raffa.Savings.Tests.PriceNormalizationCalculatorTests</c> (the identical target/savings-range
/// formula in the Savings module) own shape/style.
/// </summary>
public sealed class TargetSavingCalculatorTests
{
    private static readonly DateTimeOffset UpdatedAt = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static BenchmarkResult BenchmarkWith(BenchmarkDistribution? distribution, double confidence = 0.8) =>
        new(
            Distribution: distribution,
            Metric: "per seat / year",
            Currency: "USD",
            Confidence: confidence,
            Source: "fixture",
            UpdatedAt: UpdatedAt,
            ComparisonDimensions:
            [
                BenchmarkComparisonDimension.Supplier,
                BenchmarkComparisonDimension.Product,
                BenchmarkComparisonDimension.Geography,
            ],
            SampleSize: 340);

    // Same Salesforce-shaped comparable Raffa.Quotes.Tests.MarketAssessmentCalculatorTests and
    // Raffa.Quotes.Tests.MarketAssessmentServiceTests already use (P25=1500, P50=1800, P75=2100
    // per seat/year) — a target/saving test and a market-position test against the identical
    // comparable agree on what the numbers mean.
    private static readonly BenchmarkDistribution Distribution = new(1500m, 1800m, 2100m);

    // ----- AC-2: recommended target range + potential saving -----

    [Fact]
    public void Above_market_price_gets_a_P25_to_P50_target_and_a_positive_savings_range()
    {
        // Same figures Raffa.Quotes.Tests.MarketAssessmentServiceTests' own "above market" fixture
        // line uses (unit price 2300, quantity 100) — a target/saving test and the end-to-end
        // service test against the identical scenario agree on what the numbers are.
        var result = TargetSavingCalculator.Compute(unitPrice: 2300m, quantity: 100m, BenchmarkWith(Distribution));

        Assert.Equal(1500m, result.RecommendedTargetLow);
        Assert.Equal(1800m, result.RecommendedTargetHigh);
        Assert.Equal(500m, result.SavingsRangeLow);  // 2300 - 1800
        Assert.Equal(800m, result.SavingsRangeHigh); // 2300 - 1500
        Assert.Equal(50_000m, result.TotalSavingsRangeLow);  // 500 * 100
        Assert.Equal(80_000m, result.TotalSavingsRangeHigh); // 800 * 100
    }

    [Fact]
    public void Price_already_below_P25_never_recommends_paying_more_and_reports_zero_savings()
    {
        var result = TargetSavingCalculator.Compute(unitPrice: 1400m, quantity: 10m, BenchmarkWith(Distribution));

        Assert.Equal(1400m, result.RecommendedTargetLow);
        Assert.Equal(1400m, result.RecommendedTargetHigh);
        Assert.Equal(0m, result.SavingsRangeLow);
        Assert.Equal(0m, result.SavingsRangeHigh);
        Assert.Equal(0m, result.TotalSavingsRangeLow);
        Assert.Equal(0m, result.TotalSavingsRangeHigh);
    }

    [Fact]
    public void Price_between_P25_and_P50_targets_down_to_P25_at_most_and_never_above_current()
    {
        var result = TargetSavingCalculator.Compute(unitPrice: 1650m, quantity: 1m, BenchmarkWith(Distribution));

        Assert.Equal(1500m, result.RecommendedTargetLow);
        Assert.Equal(1650m, result.RecommendedTargetHigh); // capped at current price, not P50 (1800)
        Assert.Equal(0m, result.SavingsRangeLow);
        Assert.Equal(150m, result.SavingsRangeHigh);
    }

    [Fact]
    public void TotalSavingsRange_multiplies_the_per_unit_range_by_quantity()
    {
        var result = TargetSavingCalculator.Compute(unitPrice: 2300m, quantity: 5m, BenchmarkWith(Distribution));

        Assert.Equal(500m, result.SavingsRangeLow);
        Assert.Equal(800m, result.SavingsRangeHigh);
        Assert.Equal(2_500m, result.TotalSavingsRangeLow);  // 500 * 5
        Assert.Equal(4_000m, result.TotalSavingsRangeHigh); // 800 * 5
    }

    [Fact]
    public void Explanation_names_the_target_range_and_the_saving()
    {
        var result = TargetSavingCalculator.Compute(unitPrice: 2300m, quantity: 100m, BenchmarkWith(Distribution));

        Assert.Contains("1500", result.Explanation);
        Assert.Contains("1800", result.Explanation);
        Assert.Contains("500", result.Explanation);
        Assert.Contains("800", result.Explanation);
        Assert.Contains("Appendix C rule 6", result.Explanation);
    }

    // ----- Honest abstain: insufficient / malformed benchmark data -----

    [Fact]
    public void InsufficientBenchmarkData_when_the_benchmark_has_no_distribution()
    {
        var result = TargetSavingCalculator.Compute(unitPrice: 1950m, quantity: 10m, BenchmarkWith(distribution: null));

        Assert.Null(result.RecommendedTargetLow);
        Assert.Null(result.RecommendedTargetHigh);
        Assert.Null(result.SavingsRangeLow);
        Assert.Null(result.SavingsRangeHigh);
        Assert.Null(result.TotalSavingsRangeLow);
        Assert.Null(result.TotalSavingsRangeHigh);
        Assert.Contains("Appendix C rule 10", result.Explanation);
    }

    [Theory]
    [MemberData(nameof(MalformedDistributions))]
    public void InsufficientBenchmarkData_when_the_distribution_is_not_well_ordered(BenchmarkDistribution malformed)
    {
        var result = TargetSavingCalculator.Compute(unitPrice: 1950m, quantity: 10m, BenchmarkWith(malformed));

        Assert.Null(result.RecommendedTargetLow);
        Assert.Null(result.TotalSavingsRangeLow);
        Assert.Contains("not well-ordered", result.Explanation);
    }

    public static TheoryData<BenchmarkDistribution> MalformedDistributions() =>
    [
        new BenchmarkDistribution(1800m, 1500m, 2100m), // P50 < P25
        new BenchmarkDistribution(1500m, 2100m, 1800m), // P75 < P50
        new BenchmarkDistribution(2100m, 1800m, 1500m), // fully inverted
    ];

    // ----- Invariants across scenarios -----

    [Theory]
    [MemberData(nameof(AssessedScenarios))]
    public void Recommended_target_never_exceeds_the_current_price_and_savings_are_never_negative(
        LineTargetSaving result)
    {
        Assert.NotNull(result.RecommendedTargetLow);
        Assert.True(result.RecommendedTargetLow <= result.RecommendedTargetHigh);
        Assert.True(result.SavingsRangeLow >= 0m);
        Assert.True(result.SavingsRangeHigh >= result.SavingsRangeLow);
        Assert.Equal(result.SavingsRangeLow! * 10m, result.TotalSavingsRangeLow);
        Assert.Equal(result.SavingsRangeHigh! * 10m, result.TotalSavingsRangeHigh);
    }

    public static TheoryData<LineTargetSaving> AssessedScenarios()
    {
        var prices = new[] { 1000m, 1500m, 1650m, 1800m, 1950m, 2100m, 3000m };

        var data = new TheoryData<LineTargetSaving>();
        foreach (var price in prices)
        {
            data.Add(TargetSavingCalculator.Compute(price, quantity: 10m, BenchmarkWith(Distribution)));
        }

        return data;
    }

    // ----- Determinism (Appendix C rule 6) -----

    [Fact]
    public void Same_inputs_produce_the_same_result_every_time()
    {
        var benchmark = BenchmarkWith(Distribution);

        var first = TargetSavingCalculator.Compute(1950m, 10m, benchmark);
        var second = TargetSavingCalculator.Compute(1950m, 10m, benchmark);

        Assert.Equal(first, second);
    }

    // ----- Null-argument handling -----

    [Fact]
    public void Rejects_a_null_benchmark_argument()
    {
        Assert.Throws<ArgumentNullException>(() => TargetSavingCalculator.Compute(100m, 1m, null!));
    }
}
