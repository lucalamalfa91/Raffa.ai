using Raffa.Benchmark.Contracts;
using Raffa.Savings.Domain;

namespace Raffa.Savings.Application;

/// <summary>
/// Confidence + provenance shown "on the comparison" (parent story us-01-price-normalization AC-3;
/// task E04/F02/US01/T02, the wave-spec's <c>savings-provenance</c> artifact) — a display-ready
/// projection of the same <see cref="BenchmarkResult"/> that <see cref="PriceComparisonResult"/>
/// already echoes unchanged (task E04/F02/US01/T01), so a caller never has to interpret a bare
/// <see cref="BenchmarkResult.Confidence"/> decimal or reach into
/// <see cref="PriceComparisonResult.Benchmark"/> itself to answer "how much should I trust this
/// comparison, and where did it come from" (product spec §4.3 "Show benchmark confidence and
/// provenance"; §11.3 "Benchmark trust ... a precise-looking number from weak comparables is more
/// dangerous than an explicit 'insufficient market data' result").
///
/// <para>
/// Always derivable, regardless of <see cref="PriceComparisonResult.Status"/> —
/// <see cref="BenchmarkResult"/>'s own doc comment guarantees confidence/source/updated-at/
/// comparison-dimensions are populated "independent of whether a distribution is available, so a
/// caller can always show provenance even for an insufficient-data result." This type never stores
/// its own copy of those fields separately from <see cref="BenchmarkResult"/>: see
/// <see cref="PriceComparisonResult.Provenance"/>, a computed property deriving this fresh on every
/// access via <see cref="SavingsProvenanceClassifier.FromBenchmark"/>, so the two can never drift
/// apart.
/// </para>
///
/// <para>
/// Deliberately excludes <see cref="BenchmarkResult.LicenseRestrictions"/> and
/// <see cref="BenchmarkResult.Metric"/>: spec §10.3 marks license restrictions "store internally
/// where relevant" (not a required user-facing field), and metric is contextual to the price/currency
/// figures already on <see cref="PriceComparisonResult"/> itself — both remain reachable via
/// <see cref="PriceComparisonResult.Benchmark"/> for a caller that needs either.
/// </para>
/// </summary>
/// <param name="ConfidenceLevel">The qualitative tier <see cref="SavingsProvenanceClassifier.Classify"/>
/// deterministically derives from <paramref name="ConfidenceScore"/> — the High/Medium/Low vocabulary
/// spec's own UI examples use, never re-derived by a caller.</param>
/// <param name="ConfidenceScore">Echoes <see cref="BenchmarkResult.Confidence"/> unchanged — Raffa's
/// own <c>[0, 1]</c> score (spec §10.3), for a caller that wants the precise number alongside the
/// tier.</param>
/// <param name="Source">Echoes <see cref="BenchmarkResult.Source"/> unchanged — which provider/adapter
/// this comparison's benchmark came from (spec §10.3 "Source/provider — Yes").</param>
/// <param name="ComparisonDimensions">Echoes <see cref="BenchmarkResult.ComparisonDimensions"/>
/// unchanged — which dimensions actually drove the match, never just supplier name alone (spec
/// §10.4).</param>
/// <param name="SampleSize">Echoes <see cref="BenchmarkResult.SampleSize"/> unchanged — null when the
/// adapter could not report one (spec §10.3 "Sample size — If available").</param>
/// <param name="UpdatedAt">Echoes <see cref="BenchmarkResult.UpdatedAt"/> unchanged — when the
/// underlying comparable data was last refreshed (spec §10.3 "Updated at — Yes").</param>
/// <param name="Summary">Human-readable, deterministic one-line trace of every field above, in
/// prose — not meant to replace a UI's own rendering, but enough for a test (or a developer, or a
/// first-cut UI) to show something truthful without re-deriving the sentence, the same role
/// <see cref="PriceComparisonResult.Explanation"/> plays for the arithmetic half of this
/// comparison.</param>
public sealed record SavingsProvenance(
    SavingsConfidenceLevel ConfidenceLevel,
    double ConfidenceScore,
    string Source,
    IReadOnlyCollection<BenchmarkComparisonDimension> ComparisonDimensions,
    int? SampleSize,
    DateTimeOffset UpdatedAt,
    string Summary);
