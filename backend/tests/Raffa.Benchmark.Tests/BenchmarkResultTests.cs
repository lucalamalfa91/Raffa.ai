using Raffa.Benchmark.Contracts;

namespace Raffa.Benchmark.Tests;

public class BenchmarkResultTests
{
    private static readonly DateTimeOffset UpdatedAt = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyCollection<BenchmarkComparisonDimension> MultiDimensionMatch =
    [
        BenchmarkComparisonDimension.Supplier,
        BenchmarkComparisonDimension.Product,
        BenchmarkComparisonDimension.Geography,
    ];

    [Fact]
    public void Has_sufficient_data_when_a_distribution_is_present()
    {
        var result = new BenchmarkResult(
            Distribution: new BenchmarkDistribution(90m, 100m, 120m),
            Metric: "per seat / year",
            Currency: "USD",
            Confidence: 0.8,
            Source: "fixture",
            UpdatedAt: UpdatedAt,
            ComparisonDimensions: MultiDimensionMatch);

        Assert.True(result.HasSufficientData);
        Assert.NotNull(result.Distribution);
    }

    /// <summary>
    /// ADR-001: the first R3/R4 demo must render a benchmark result as either confident or an
    /// explicit "insufficient market data" outcome — never a bare precise-looking number without
    /// provenance. A null <see cref="BenchmarkResult.Distribution"/> is that explicit outcome, and
    /// every other provenance field (metric/currency/confidence/source/updated/comparison) must
    /// still be populated so the caller can show why.
    /// </summary>
    [Fact]
    public void Reports_insufficient_data_explicitly_without_dropping_provenance()
    {
        var result = new BenchmarkResult(
            Distribution: null,
            Metric: "per seat / year",
            Currency: "USD",
            Confidence: 0.1,
            Source: "fixture",
            UpdatedAt: UpdatedAt,
            ComparisonDimensions: MultiDimensionMatch);

        Assert.False(result.HasSufficientData);
        Assert.Equal("per seat / year", result.Metric);
        Assert.Equal("USD", result.Currency);
        Assert.Equal(0.1, result.Confidence);
        Assert.Equal("fixture", result.Source);
        Assert.Equal(UpdatedAt, result.UpdatedAt);
        Assert.NotEmpty(result.ComparisonDimensions);
    }

    [Fact]
    public void Sample_size_and_license_restrictions_default_to_null()
    {
        var result = new BenchmarkResult(
            Distribution: new BenchmarkDistribution(90m, 100m, 120m),
            Metric: "per seat / year",
            Currency: "USD",
            Confidence: 0.8,
            Source: "fixture",
            UpdatedAt: UpdatedAt,
            ComparisonDimensions: MultiDimensionMatch);

        Assert.Null(result.SampleSize);
        Assert.Null(result.LicenseRestrictions);
    }

    [Fact]
    public void Sample_size_and_license_restrictions_are_settable_when_known()
    {
        var result = new BenchmarkResult(
            Distribution: new BenchmarkDistribution(90m, 100m, 120m),
            Metric: "per seat / year",
            Currency: "USD",
            Confidence: 0.8,
            Source: "fixture",
            UpdatedAt: UpdatedAt,
            ComparisonDimensions: MultiDimensionMatch,
            SampleSize: 42,
            LicenseRestrictions: "internal use only");

        Assert.Equal(42, result.SampleSize);
        Assert.Equal("internal use only", result.LicenseRestrictions);
    }
}
