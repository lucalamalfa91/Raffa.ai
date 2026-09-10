using System.Globalization;
using Raffa.Benchmark.Contracts;
using Raffa.Savings.Domain;

namespace Raffa.Savings.Application;

/// <summary>
/// Derives <see cref="SavingsProvenance"/> — confidence + provenance "on the comparison" (parent
/// story us-01-price-normalization AC-3, task E04/F02/US01/T02, the wave-spec's
/// <c>savings-provenance</c> artifact) — from an already-fetched <see cref="BenchmarkResult"/>.
/// Static and pure: no database call, no HTTP call, no LLM call, the same input always produces the
/// same output (Appendix C rule 6) — the same determinism convention
/// <see cref="PriceNormalizationCalculator"/> and
/// <c>Raffa.Renewals.Application.RenewalOpportunityGenerator.FromCalculation</c> already
/// established. The whole of this classifier's business rule lives in <see cref="Classify"/> and
/// <see cref="FromBenchmark"/>, nowhere else.
/// </summary>
public static class SavingsProvenanceClassifier
{
    /// <summary>
    /// <see cref="BenchmarkResult.Confidence"/> at or above this value classifies as
    /// <see cref="SavingsConfidenceLevel.High"/>. Spec §10.3 defines confidence as "Raffa's own
    /// score", not a provider-defined one, so — the same "this module's own documented, deliberately
    /// simple heuristic, refined later" convention
    /// <c>Raffa.Benchmark.Fixtures.FixtureBenchmarkAdapter.FullConfidenceSampleSize</c> already
    /// established for the score itself — these thresholds are this classifier's own, not a
    /// council-locked figure. Chosen so <c>FixtureBenchmarkAdapter</c>'s own catalog already spans
    /// all three tiers under its own <c>ComputeConfidence</c> formula: a full-dimension match on a
    /// full sample (confidence 1.0 — e.g. AWS US/EU, Salesforce, Slack) is
    /// <see cref="SavingsConfidenceLevel.High"/>; a full-dimension match on a thinner sample (Zoom,
    /// 30 of 50 comparables → confidence 0.6) is <see cref="SavingsConfidenceLevel.Medium"/>; a
    /// full-dimension match on a very thin sample (Snowflake, 18 of 50 → confidence 0.36) or a weak,
    /// supplier+product-only match (confidence ~0.29) is <see cref="SavingsConfidenceLevel.Low"/> —
    /// an honest signal that a precise-looking number still rests on weak comparables (spec §11.3's
    /// benchmark-trust rule).
    /// </summary>
    public const double HighConfidenceThreshold = 0.7;

    /// <summary>
    /// <see cref="BenchmarkResult.Confidence"/> at or above this value (and below
    /// <see cref="HighConfidenceThreshold"/>) classifies as
    /// <see cref="SavingsConfidenceLevel.Medium"/>; below it classifies as
    /// <see cref="SavingsConfidenceLevel.Low"/>. See <see cref="HighConfidenceThreshold"/>'s own doc
    /// comment for the reasoning behind both thresholds.
    /// </summary>
    public const double MediumConfidenceThreshold = 0.4;

    /// <summary>
    /// Builds the full <see cref="SavingsProvenance"/> view for <paramref name="benchmark"/> — the
    /// only place <see cref="Classify"/>'s tier and <see cref="BenchmarkResult"/>'s own provenance
    /// fields are assembled into the shape <see cref="PriceComparisonResult.Provenance"/> exposes.
    /// Never fetches or recomputes <paramref name="benchmark"/> itself — same "benchmark data only
    /// ever arrives as an already-known value" convention <see cref="PriceComparisonRequest"/>'s own
    /// doc comment establishes, so this can never become an accidental provider call (Appendix C
    /// rule 3).
    /// </summary>
    public static SavingsProvenance FromBenchmark(BenchmarkResult benchmark)
    {
        ArgumentNullException.ThrowIfNull(benchmark);

        var level = Classify(benchmark.Confidence);

        return new SavingsProvenance(
            level,
            benchmark.Confidence,
            benchmark.Source,
            benchmark.ComparisonDimensions,
            benchmark.SampleSize,
            benchmark.UpdatedAt,
            BuildSummary(benchmark, level));
    }

    /// <summary>
    /// Maps a raw <see cref="BenchmarkResult.Confidence"/> score onto a
    /// <see cref="SavingsConfidenceLevel"/> tier — see <see cref="HighConfidenceThreshold"/>'s own
    /// doc comment for the thresholds and their reasoning. Never throws: a value outside the
    /// documented <c>[0, 1]</c> range (for example from a future, differently-scored adapter) still
    /// resolves to the nearest honest tier rather than crashing the comparison.
    /// </summary>
    public static SavingsConfidenceLevel Classify(double confidenceScore) =>
        confidenceScore switch
        {
            >= HighConfidenceThreshold => SavingsConfidenceLevel.High,
            >= MediumConfidenceThreshold => SavingsConfidenceLevel.Medium,
            _ => SavingsConfidenceLevel.Low,
        };

    /// <summary>
    /// Deterministic, culture-invariant one-line trace of every <see cref="SavingsProvenance"/>
    /// field — see that type's own doc comment for <see cref="SavingsProvenance.Summary"/>'s role.
    /// Never fabricates a dimension or sample size that is not actually present (Appendix C rule
    /// 10): an empty <see cref="BenchmarkResult.ComparisonDimensions"/> or a null
    /// <see cref="BenchmarkResult.SampleSize"/> is reported as exactly that, in words, never silently
    /// dropped from the sentence.
    /// </summary>
    private static string BuildSummary(BenchmarkResult benchmark, SavingsConfidenceLevel level)
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

    /// <summary>Culture-invariant, unpadded decimal formatting for <see cref="BuildSummary"/> — the
    /// same convention <see cref="PriceNormalizationCalculator"/>'s own private <c>Fmt</c> helper
    /// already established for this module's other explanation text (kept as its own copy here,
    /// not shared, the same "each calculator owns its own formatting helper" pattern that class
    /// itself follows).</summary>
    private static string Fmt(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
