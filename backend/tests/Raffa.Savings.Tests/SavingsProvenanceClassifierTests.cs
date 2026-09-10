using Raffa.Benchmark.Contracts;
using Raffa.Savings.Application;
using Raffa.Savings.Domain;

namespace Raffa.Savings.Tests;

/// <summary>
/// Proves task E04/F02/US01/T02's execution step: <see cref="SavingsProvenanceClassifier"/> derives
/// a deterministic <see cref="SavingsProvenance"/> — confidence tier + provenance fields — from an
/// already-fetched <see cref="BenchmarkResult"/>, parent story us-01-price-normalization AC-3 ("Show
/// confidence + provenance on the comparison").
/// </summary>
public sealed class SavingsProvenanceClassifierTests
{
    private static readonly DateTimeOffset UpdatedAt = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private static BenchmarkResult BenchmarkWith(
        double confidence,
        string source = "fixture",
        int? sampleSize = 340,
        IReadOnlyCollection<BenchmarkComparisonDimension>? dimensions = null) =>
        new(
            Distribution: new BenchmarkDistribution(390m, 430m, 470m),
            Metric: "per seat / year",
            Currency: "USD",
            Confidence: confidence,
            Source: source,
            UpdatedAt: UpdatedAt,
            ComparisonDimensions: dimensions ??
                [BenchmarkComparisonDimension.Supplier, BenchmarkComparisonDimension.Product],
            SampleSize: sampleSize);

    // ----- Classify: boundary values (matches FixtureBenchmarkAdapter's own catalog spread) -----

    [Theory]
    [InlineData(1.0, SavingsConfidenceLevel.High)]
    [InlineData(0.7, SavingsConfidenceLevel.High)]   // exactly the High boundary
    [InlineData(0.69, SavingsConfidenceLevel.Medium)]
    [InlineData(0.6, SavingsConfidenceLevel.Medium)] // Zoom's real fixture confidence (30 of 50 sample)
    [InlineData(0.4, SavingsConfidenceLevel.Medium)] // exactly the Medium boundary
    [InlineData(0.39, SavingsConfidenceLevel.Low)]
    [InlineData(0.36, SavingsConfidenceLevel.Low)]   // Snowflake's real fixture confidence (18 of 50 sample)
    [InlineData(0.0, SavingsConfidenceLevel.Low)]
    public void Classify_maps_the_confidence_score_onto_the_expected_tier(
        double confidence, SavingsConfidenceLevel expected)
    {
        Assert.Equal(expected, SavingsProvenanceClassifier.Classify(confidence));
    }

    [Fact]
    public void Classify_never_throws_for_a_score_outside_the_documented_zero_to_one_range()
    {
        Assert.Equal(SavingsConfidenceLevel.High, SavingsProvenanceClassifier.Classify(1.5));
        Assert.Equal(SavingsConfidenceLevel.Low, SavingsProvenanceClassifier.Classify(-0.5));
    }

    // ----- FromBenchmark: field passthrough -----

    [Fact]
    public void FromBenchmark_echoes_every_benchmark_provenance_field_unchanged()
    {
        BenchmarkComparisonDimension[] dimensions =
            [BenchmarkComparisonDimension.Supplier, BenchmarkComparisonDimension.Geography];
        var benchmark = BenchmarkWith(confidence: 0.82, source: "fixture", sampleSize: 340, dimensions: dimensions);

        var provenance = SavingsProvenanceClassifier.FromBenchmark(benchmark);

        Assert.Equal(SavingsConfidenceLevel.High, provenance.ConfidenceLevel);
        Assert.Equal(0.82, provenance.ConfidenceScore);
        Assert.Equal("fixture", provenance.Source);
        Assert.Equal(dimensions, provenance.ComparisonDimensions);
        Assert.Equal(340, provenance.SampleSize);
        Assert.Equal(UpdatedAt, provenance.UpdatedAt);
    }

    [Fact]
    public void FromBenchmark_rejects_a_null_benchmark_argument()
    {
        Assert.Throws<ArgumentNullException>(() => SavingsProvenanceClassifier.FromBenchmark(null!));
    }

    // ----- Summary: never fabricates an absent dimension or sample size (Appendix C rule 10) -----

    [Fact]
    public void Summary_names_the_actual_dimensions_and_sample_size_when_present()
    {
        var benchmark = BenchmarkWith(confidence: 0.9, sampleSize: 780,
            dimensions: [BenchmarkComparisonDimension.Supplier, BenchmarkComparisonDimension.Product]);

        var provenance = SavingsProvenanceClassifier.FromBenchmark(benchmark);

        Assert.Contains("High", provenance.Summary);
        Assert.Contains("fixture", provenance.Summary);
        Assert.Contains("780", provenance.Summary);
        Assert.Contains("Supplier", provenance.Summary);
        Assert.Contains("Product", provenance.Summary);
    }

    [Fact]
    public void Summary_reports_no_dimensions_and_unknown_sample_size_honestly_rather_than_fabricating()
    {
        var benchmark = BenchmarkWith(confidence: 0.0, sampleSize: null, dimensions: []);

        var provenance = SavingsProvenanceClassifier.FromBenchmark(benchmark);

        Assert.Contains("no comparison dimensions", provenance.Summary);
        Assert.Contains("unknown", provenance.Summary);
        Assert.Contains("Low", provenance.Summary);
    }

    // ----- Determinism (Appendix C rule 6) -----

    [Fact]
    public void Same_benchmark_produces_the_same_provenance_every_time()
    {
        var benchmark = BenchmarkWith(confidence: 0.55);

        var first = SavingsProvenanceClassifier.FromBenchmark(benchmark);
        var second = SavingsProvenanceClassifier.FromBenchmark(benchmark);

        Assert.Equal(first, second);
    }
}
