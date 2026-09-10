using Raffa.Benchmark.Contracts;
using Raffa.Quotes.Application.Assessment;
using Raffa.Quotes.Domain;

namespace Raffa.Quotes.Tests;

/// <summary>
/// Proves task E05/F02/US01/T01's execution step: <see cref="MarketAssessmentCalculator"/> flags a
/// quote line's price as above/in-line/below market against an already-fetched
/// <see cref="BenchmarkResult"/> — parent story us-01-market-assessment AC-2's own "flag" half —
/// with no database, no HTTP call and no LLM call anywhere in the path. Mirrors
/// <c>Raffa.Savings.Tests.PriceNormalizationCalculatorTests</c>'s own shape/style for the
/// analogous Savings comparison.
/// </summary>
public sealed class MarketAssessmentCalculatorTests
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

    // Salesforce-shaped comparable: P25=1500, P50=1800, P75=2100 (per seat/year) — the same figures
    // FixtureBenchmarkAdapter's own catalog + Raffa.IntegrationTests.R3EndToEndTests already use,
    // so a market-assessment test and a savings-comparison test against the identical comparable
    // agree on what "above/in-line/below" means for the same numbers.
    private static readonly BenchmarkDistribution Distribution = new(1500m, 1800m, 2100m);

    // ----- AC-2: the three-way flag -----

    [Theory]
    [InlineData(1000)]  // well below P25
    [InlineData(1500)]  // exactly P25 -> still below (boundary is inclusive of "below")
    public void Price_at_or_below_P25_is_BelowMarket(decimal unitPrice)
    {
        var result = MarketAssessmentCalculator.Classify(unitPrice, BenchmarkWith(Distribution));

        Assert.Equal(MarketAssessmentStatus.Assessed, result.Status);
        Assert.Equal(MarketPosition.BelowMarket, result.Position);
    }

    [Theory]
    [InlineData(1500.01)]
    [InlineData(1800)]   // exactly P50 -> in line
    [InlineData(2099.99)]
    public void Price_strictly_between_P25_and_P75_is_InLine(decimal unitPrice)
    {
        var result = MarketAssessmentCalculator.Classify(unitPrice, BenchmarkWith(Distribution));

        Assert.Equal(MarketAssessmentStatus.Assessed, result.Status);
        Assert.Equal(MarketPosition.InLine, result.Position);
    }

    [Theory]
    [InlineData(2100)]   // exactly P75 -> above
    [InlineData(3000)]   // well above P75
    public void Price_at_or_above_P75_is_AboveMarket(decimal unitPrice)
    {
        var result = MarketAssessmentCalculator.Classify(unitPrice, BenchmarkWith(Distribution));

        Assert.Equal(MarketAssessmentStatus.Assessed, result.Status);
        Assert.Equal(MarketPosition.AboveMarket, result.Position);
    }

    [Fact]
    public void Assessed_explanation_names_the_market_range_and_the_price()
    {
        var result = MarketAssessmentCalculator.Classify(1950m, BenchmarkWith(Distribution));

        Assert.Contains("1950", result.Explanation);
        Assert.Contains("1500", result.Explanation);
        Assert.Contains("2100", result.Explanation);
        Assert.Contains("Appendix C rule 6", result.Explanation);
    }

    // ----- Honest abstain: insufficient / malformed benchmark data -----

    [Fact]
    public void InsufficientBenchmarkData_when_the_benchmark_has_no_distribution()
    {
        var result = MarketAssessmentCalculator.Classify(1950m, BenchmarkWith(distribution: null));

        Assert.Equal(MarketAssessmentStatus.InsufficientBenchmarkData, result.Status);
        Assert.Null(result.Position);
        Assert.Contains("Appendix C rule 10", result.Explanation);
    }

    [Theory]
    [MemberData(nameof(MalformedDistributions))]
    public void InsufficientBenchmarkData_when_the_distribution_is_not_well_ordered(BenchmarkDistribution malformed)
    {
        var result = MarketAssessmentCalculator.Classify(1950m, BenchmarkWith(malformed));

        Assert.Equal(MarketAssessmentStatus.InsufficientBenchmarkData, result.Status);
        Assert.Null(result.Position);
        Assert.Contains("not well-ordered", result.Explanation);
    }

    public static TheoryData<BenchmarkDistribution> MalformedDistributions() =>
    [
        new BenchmarkDistribution(1800m, 1500m, 2100m), // P50 < P25
        new BenchmarkDistribution(1500m, 2100m, 1800m), // P75 < P50
        new BenchmarkDistribution(2100m, 1800m, 1500m), // fully inverted
    ];

    // ----- Determinism (Appendix C rule 6) -----

    [Fact]
    public void Same_inputs_produce_the_same_result_every_time()
    {
        var benchmark = BenchmarkWith(Distribution);

        var first = MarketAssessmentCalculator.Classify(1950m, benchmark);
        var second = MarketAssessmentCalculator.Classify(1950m, benchmark);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Rejects_a_null_benchmark_argument()
    {
        Assert.Throws<ArgumentNullException>(() => MarketAssessmentCalculator.Classify(100m, null!));
    }
}
