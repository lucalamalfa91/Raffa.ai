using Raffa.Benchmark.Contracts;
using Raffa.Savings.Domain;

namespace Raffa.Savings.Application;

/// <summary>
/// The outcome of <see cref="PriceNormalizationCalculator.Compare"/> — parent story
/// us-01-price-normalization AC-1 ("normalize current unit price") and AC-2 ("compute percentile,
/// recommended target and savings range deterministically") made concrete and testable. Every
/// numeric field is either pure arithmetic over the <see cref="PriceComparisonRequest"/> the caller
/// supplied, or an explicit null with <see cref="Explanation"/> saying why (never a fabricated
/// value — Appendix C rule 10), the same convention
/// <c>Raffa.Renewals.Application.RenewalCalculationResult</c> already established.
/// </summary>
/// <param name="Status">Which outcome this is — see <see cref="PriceComparisonStatus"/>'s own doc
/// comments.</param>
/// <param name="NormalizedUnitPrice"><c>PriceComparisonRequest.CurrentTotalCost</c> divided by
/// <c>PriceComparisonRequest.Query.Quantity</c> — AC-1's "normalize... quantity". Null only for
/// <see cref="PriceComparisonStatus.InvalidQuantity"/>; computed for every other status
/// (independent of whether a percentile/target/savings comparison could also be made), because
/// "what is the current per-unit price" does not depend on the benchmark at all.</param>
/// <param name="PercentileRank">Where <see cref="NormalizedUnitPrice"/> falls against
/// <c>Benchmark.Distribution</c>, on a 0-100 scale interpolated between the three known markers
/// (P25→25, P50→50, P75→75) and clamped at the ends (at-or-below-P25 reports exactly 25;
/// at-or-above-P75 reports exactly 75 — this calculator only ever interpolates <em>between</em>
/// real observed markers, never extrapolates <em>beyond</em> the last one, per Appendix C rule 10).
/// Null unless <see cref="Status"/> is <see cref="PriceComparisonStatus.Compared"/>.</param>
/// <param name="RecommendedTargetLow">The more aggressive end of the recommended target range —
/// <c>min(P25, NormalizedUnitPrice)</c> — never higher than the current price (this calculator
/// never recommends paying <em>more</em> than the current price). Null unless
/// <see cref="Status"/> is <see cref="PriceComparisonStatus.Compared"/>.</param>
/// <param name="RecommendedTargetHigh">The more conservative end of the recommended target range —
/// <c>min(P50, NormalizedUnitPrice)</c>. Always <c>&gt;= RecommendedTargetLow</c> (both are a
/// <c>min</c> against the current price, and <c>P25 &lt;= P50</c> is validated before this status
/// is reached). Null unless <see cref="Status"/> is
/// <see cref="PriceComparisonStatus.Compared"/>.</param>
/// <param name="SavingsRangeLow">Per-unit saving from reaching only
/// <see cref="RecommendedTargetHigh"/> — <c>NormalizedUnitPrice - RecommendedTargetHigh</c>, always
/// <c>&gt;= 0</c>. Null unless <see cref="Status"/> is
/// <see cref="PriceComparisonStatus.Compared"/>.</param>
/// <param name="SavingsRangeHigh">Per-unit saving from reaching the more aggressive
/// <see cref="RecommendedTargetLow"/> — <c>NormalizedUnitPrice - RecommendedTargetLow</c>, always
/// <c>&gt;= SavingsRangeLow</c>. Null unless <see cref="Status"/> is
/// <see cref="PriceComparisonStatus.Compared"/>.</param>
/// <param name="TotalSavingsRangeLow"><see cref="SavingsRangeLow"/> times
/// <c>PriceComparisonRequest.Query.Quantity</c> — the total (not per-unit) saving across every
/// purchased unit. Null unless <see cref="Status"/> is
/// <see cref="PriceComparisonStatus.Compared"/>.</param>
/// <param name="TotalSavingsRangeHigh"><see cref="SavingsRangeHigh"/> times
/// <c>PriceComparisonRequest.Query.Quantity</c>. Null unless <see cref="Status"/> is
/// <see cref="PriceComparisonStatus.Compared"/>.</param>
/// <param name="Benchmark">Echoes <c>PriceComparisonRequest.Benchmark</c> unchanged, regardless of
/// <see cref="Status"/> — <see cref="BenchmarkResult.Confidence"/>,
/// <see cref="BenchmarkResult.Source"/>, <see cref="BenchmarkResult.ComparisonDimensions"/> and the
/// rest of the Benchmark Service's own provenance (product spec §10.3) are always reachable from
/// this result without this calculator re-declaring or duplicating a single one of those fields.
/// See <see cref="Provenance"/> for task-02's (confidence + provenance propagation) display-ready
/// projection of this same value.</param>
/// <param name="Explanation">Human-readable trace of what this calculator computed and why — not
/// meant to be shown to an end user as-is, but enough for a test (or a developer) to see why a
/// result has the shape it does without re-deriving it, the same role
/// <c>Raffa.Renewals.Application.RenewalCalculationResult.Explanation</c> plays there.</param>
public sealed record PriceComparisonResult(
    PriceComparisonStatus Status,
    decimal? NormalizedUnitPrice,
    decimal? PercentileRank,
    decimal? RecommendedTargetLow,
    decimal? RecommendedTargetHigh,
    decimal? SavingsRangeLow,
    decimal? SavingsRangeHigh,
    decimal? TotalSavingsRangeLow,
    decimal? TotalSavingsRangeHigh,
    BenchmarkResult Benchmark,
    string Explanation)
{
    /// <summary>
    /// Confidence + provenance "on the comparison" (parent story us-01-price-normalization AC-3,
    /// task E04/F02/US01/T02, the wave-spec's <c>savings-provenance</c> artifact) —
    /// <see cref="SavingsProvenanceClassifier.FromBenchmark"/> applied to <see cref="Benchmark"/>.
    /// Computed fresh on every access, not a stored field: <see cref="Benchmark"/> is this result's
    /// one and only source of provenance truth, so this property can never drift from it or be
    /// constructed with a mismatched value. Available regardless of <see cref="Status"/> — see
    /// <see cref="SavingsProvenance"/>'s own doc comment for why that is always safe.
    /// </summary>
    public SavingsProvenance Provenance => SavingsProvenanceClassifier.FromBenchmark(Benchmark);
}
