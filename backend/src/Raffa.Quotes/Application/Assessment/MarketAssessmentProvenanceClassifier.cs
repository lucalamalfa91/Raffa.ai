using System.Globalization;
using Raffa.Benchmark.Contracts;
using Raffa.Quotes.Domain;

namespace Raffa.Quotes.Application.Assessment;

/// <summary>
/// Derives <see cref="MarketAssessmentProvenance"/> from an already-fetched
/// <see cref="BenchmarkResult"/> — mirrors
/// <c>Raffa.Savings.Application.SavingsProvenanceClassifier</c> exactly, including its two
/// threshold constants (duplicated, not shared — see <see cref="MarketConfidenceLevel"/>'s own doc
/// comment for why ADR-002 requires the copy). Static and pure: no database call, no HTTP call, no
/// LLM call — the same input always produces the same output (Appendix C rule 6).
/// </summary>
public static class MarketAssessmentProvenanceClassifier
{
    /// <summary>See
    /// <c>Raffa.Savings.Application.SavingsProvenanceClassifier.HighConfidenceThreshold</c>'s own
    /// doc comment for the reasoning — identical value, so a market assessment and a savings
    /// comparison against the same underlying <see cref="BenchmarkResult"/> always report the same
    /// confidence tier.</summary>
    public const double HighConfidenceThreshold = 0.7;

    /// <summary>See
    /// <c>Raffa.Savings.Application.SavingsProvenanceClassifier.MediumConfidenceThreshold</c>'s
    /// own doc comment.</summary>
    public const double MediumConfidenceThreshold = 0.4;

    /// <summary>
    /// Builds the full <see cref="MarketAssessmentProvenance"/> view for <paramref name="benchmark"/>.
    /// Never fetches or recomputes <paramref name="benchmark"/> itself — the same "benchmark data
    /// only ever arrives as an already-known value" convention
    /// <c>Raffa.Savings.Application.PriceComparisonRequest</c>'s own doc comment establishes.
    /// </summary>
    public static MarketAssessmentProvenance FromBenchmark(BenchmarkResult benchmark)
    {
        ArgumentNullException.ThrowIfNull(benchmark);

        var level = Classify(benchmark.Confidence);

        return new MarketAssessmentProvenance(
            level,
            benchmark.Confidence,
            benchmark.Source,
            benchmark.ComparisonDimensions,
            benchmark.SampleSize,
            benchmark.UpdatedAt,
            BuildSummary(benchmark, level));
    }

    /// <summary>Maps a raw <see cref="BenchmarkResult.Confidence"/> score onto a
    /// <see cref="MarketConfidenceLevel"/> tier. Never throws: a value outside the documented
    /// <c>[0, 1]</c> range still resolves to the nearest honest tier rather than crashing the
    /// assessment.</summary>
    public static MarketConfidenceLevel Classify(double confidenceScore) =>
        confidenceScore switch
        {
            >= HighConfidenceThreshold => MarketConfidenceLevel.High,
            >= MediumConfidenceThreshold => MarketConfidenceLevel.Medium,
            _ => MarketConfidenceLevel.Low,
        };

    /// <summary>Deterministic, culture-invariant one-line trace of every
    /// <see cref="MarketAssessmentProvenance"/> field. Never fabricates a dimension or sample size
    /// that is not actually present (Appendix C rule 10).</summary>
    private static string BuildSummary(BenchmarkResult benchmark, MarketConfidenceLevel level)
    {
        var dimensionsText = benchmark.ComparisonDimensions.Count > 0
            ? string.Join(", ", benchmark.ComparisonDimensions)
            : "no comparison dimensions (insufficient market data)";

        var sampleSizeText = benchmark.SampleSize is { } sampleSize
            ? sampleSize.ToString(CultureInfo.InvariantCulture)
            : "unknown";

        var updatedAtText = benchmark.UpdatedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return $"{level} confidence ({Fmt(benchmark.Confidence)}) from '{benchmark.Source}', matched " +
            $"on {dimensionsText}, sample size {sampleSizeText}, data updated {updatedAtText}.";
    }

    private static string Fmt(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
