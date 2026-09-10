using System.Globalization;
using Raffa.Benchmark.Contracts;
using Raffa.Savings.Domain;

namespace Raffa.Savings.Application;

/// <summary>
/// The deterministic price-normalization and benchmark-comparison calculator (task
/// E04/F02/US01/T01, the wave-spec's <c>savings-normalization</c> artifact; parent story
/// us-01-price-normalization). Pure and synchronous: no database call, no HTTP call, no LLM call
/// anywhere in <see cref="Compare"/> — the same <see cref="PriceComparisonRequest"/> always
/// produces the same <see cref="PriceComparisonResult"/> (Appendix C rule 6 — "prefer deterministic
/// arithmetic... to LLM reasoning"; product spec §4.3 "Normalize current unit price and compare
/// with benchmark P25/P50/P75... Calculate current percentile, recommended target and savings
/// range"), the same determinism convention
/// <c>Raffa.Renewals.Application.RenewalEngine</c> / <c>PriorityScoreCalculator</c> already
/// established for this solution's other deterministic calculators.
///
/// <para>
/// AC-1 ("normalize current unit price — currency/quantity/term — before comparison"): quantity
/// normalization divides <see cref="PriceComparisonRequest.CurrentTotalCost"/> by
/// <see cref="BenchmarkQuery.Quantity"/> to get a per-unit price; currency normalization requires
/// <see cref="BenchmarkQuery.Currency"/> to equal <see cref="BenchmarkResult.Currency"/> exactly —
/// this codebase has no exchange-rate service, so a mismatch is reported as an honest
/// <see cref="PriceComparisonStatus.CurrencyMismatch"/> rather than a fabricated conversion
/// (Appendix C rule 10); term normalization is the Benchmark Service's own matching responsibility
/// (see <see cref="PriceComparisonRequest.Query"/>'s own doc comment) — trusted, not re-derived,
/// here.
/// </para>
///
/// <para>
/// AC-2 ("compute percentile, recommended target and savings range deterministically"): see
/// <see cref="ComputePercentileRank"/> for the interpolation this calculator uses between the three
/// known <see cref="BenchmarkDistribution"/> markers, and <see cref="PriceComparisonResult"/>'s own
/// per-parameter doc comments for the target/savings-range formulas. Never fabricates: a benchmark
/// with no published distribution, or one whose markers are not well-ordered
/// (<c>P25 &lt;= P50 &lt;= P75</c>), comes back with an explicit
/// <see cref="PriceComparisonStatus.InsufficientBenchmarkData"/> and no percentile/target/savings,
/// rather than a number computed from data that cannot support it (Appendix C rule 10; product spec
/// §11.3 "Do not present a provider percentile without provenance/confidence" / "Do not generate a
/// savings target if... normalization is unresolved" — the same guardrail spirit applied to a
/// comparison against a portfolio contract instead of a new quote).
/// </para>
/// </summary>
public sealed class PriceNormalizationCalculator
{
    /// <summary>
    /// Normalizes <paramref name="request"/>'s current price and, when the benchmark supports it,
    /// computes its percentile rank plus a recommended target and savings range. See the decision
    /// order in this method's body comments; every branch is covered by
    /// <c>Raffa.Savings.Tests.PriceNormalizationCalculatorTests</c>.
    /// </summary>
    public PriceComparisonResult Compare(PriceComparisonRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = request.Query;
        var benchmark = request.Benchmark;

        // Quantity must be positive before anything else can be normalized — AC-1's "quantity"
        // normalization is a division, and invalid structured data is treated the same as missing
        // data, never guessed at (Appendix C rule 10).
        if (query.Quantity <= 0m)
        {
            return new PriceComparisonResult(
                PriceComparisonStatus.InvalidQuantity,
                null, null, null, null, null, null, null, null,
                benchmark,
                $"Query.Quantity ({Fmt(query.Quantity)}) is not a positive amount: the current " +
                "unit price cannot be normalized by dividing total cost by a zero or negative " +
                "quantity (Appendix C rule 10).");
        }

        var normalizedUnitPrice = request.CurrentTotalCost / query.Quantity;

        // Currency normalization: an exact match is required. No exchange-rate service exists
        // anywhere in this codebase (Appendix C rule 10 — converting would fabricate a rate this
        // solution has no way to actually know), so a mismatch stops here, after the unit price
        // (still meaningful in its own currency) but before any benchmark comparison.
        if (!string.Equals(query.Currency, benchmark.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return new PriceComparisonResult(
                PriceComparisonStatus.CurrencyMismatch,
                normalizedUnitPrice,
                null, null, null, null, null, null, null,
                benchmark,
                $"Query.Currency ({query.Currency}) does not match Benchmark.Currency " +
                $"({benchmark.Currency}): no currency-conversion service exists in this codebase, " +
                "so this calculator reports the normalized unit price in its own currency but does " +
                "not compare it to this benchmark (Appendix C rule 10).");
        }

        // ADR-001's explicit "insufficient market data" outcome: too few comparables to publish a
        // distribution at all.
        if (benchmark.Distribution is not { } distribution)
        {
            return new PriceComparisonResult(
                PriceComparisonStatus.InsufficientBenchmarkData,
                normalizedUnitPrice,
                null, null, null, null, null, null, null,
                benchmark,
                "Benchmark.Distribution is null (Benchmark.HasSufficientData is false): the " +
                "adapter had too few comparables to publish P25/P50/P75, so percentile/target/" +
                "savings cannot be computed without fabricating a distribution (Appendix C rule 10; " +
                "ADR-001).");
        }

        // A malformed/inverted distribution (a data-quality problem in the adapter's own output,
        // not this calculator's to silently paper over) would make percentile interpolation and
        // the target/savings-range formulas produce misleading numbers rather than merely fail —
        // worse than an honest "cannot determine" (Appendix C rule 10).
        if (!(distribution.P25 <= distribution.P50 && distribution.P50 <= distribution.P75))
        {
            return new PriceComparisonResult(
                PriceComparisonStatus.InsufficientBenchmarkData,
                normalizedUnitPrice,
                null, null, null, null, null, null, null,
                benchmark,
                $"Benchmark.Distribution ({Fmt(distribution.P25)}/{Fmt(distribution.P50)}/" +
                $"{Fmt(distribution.P75)}) is not well-ordered (P25 <= P50 <= P75 does not hold): " +
                "a percentile/target/savings comparison against it would not be meaningful " +
                "(Appendix C rule 10).");
        }

        var (percentile, percentileNote) = ComputePercentileRank(normalizedUnitPrice, distribution);

        // Recommended target range: never above the current price (this calculator never
        // recommends paying more than the current price), so both ends are clamped through the
        // current price itself. P25 <= P50 is guaranteed by the well-ordered check above, so
        // RecommendedTargetLow <= RecommendedTargetHigh always holds.
        var targetLow = Math.Min(distribution.P25, normalizedUnitPrice);
        var targetHigh = Math.Min(distribution.P50, normalizedUnitPrice);

        // Savings range: reaching only the conservative end (targetHigh) is the smaller saving;
        // reaching the aggressive end (targetLow) is the larger one. Both are >= 0 by construction
        // (targetHigh and targetLow are each <= normalizedUnitPrice).
        var savingsLow = normalizedUnitPrice - targetHigh;
        var savingsHigh = normalizedUnitPrice - targetLow;

        return new PriceComparisonResult(
            PriceComparisonStatus.Compared,
            normalizedUnitPrice,
            percentile,
            targetLow,
            targetHigh,
            savingsLow,
            savingsHigh,
            savingsLow * query.Quantity,
            savingsHigh * query.Quantity,
            benchmark,
            $"Normalized unit price {Fmt(normalizedUnitPrice)} {benchmark.Currency} is " +
            $"{percentileNote} (percentile {Fmt(percentile)}). Recommended target range " +
            $"[{Fmt(targetLow)}, {Fmt(targetHigh)}] {benchmark.Currency} per unit; savings range " +
            $"[{Fmt(savingsLow)}, {Fmt(savingsHigh)}] {benchmark.Currency} per unit (deterministic " +
            "arithmetic, Appendix C rule 6).");
    }

    /// <summary>Convenience batch form of <see cref="Compare"/>, mirroring
    /// <c>Raffa.Renewals.Application.RenewalEngine.CalculateMany</c> — one result per
    /// <paramref name="requests"/> entry, in the same order, no aggregation.</summary>
    public IReadOnlyList<PriceComparisonResult> CompareMany(IEnumerable<PriceComparisonRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        return requests.Select(Compare).ToList();
    }

    /// <summary>
    /// Interpolates <paramref name="price"/>'s position against <paramref name="distribution"/>'s
    /// three known markers, on a 0-100 scale: P25→25, P50→50, P75→75, linearly interpolated
    /// between adjacent markers. <paramref name="price"/> at or below P25 reports exactly 25; at or
    /// above P75 reports exactly 75 — this calculator only interpolates <em>between</em> real
    /// observed markers, never extrapolates a guess <em>beyond</em> the last one (Appendix C rule
    /// 10). The caller (<see cref="Compare"/>) already validated
    /// <c>distribution.P25 &lt;= distribution.P50 &lt;= distribution.P75</c>, and reaching either
    /// interpolation branch below requires <paramref name="price"/> to sit <em>strictly</em>
    /// between the two markers it interpolates (both boundary comparisons above already handle
    /// equality), so the divisions below can never divide by zero.
    /// </summary>
    private static (decimal Percentile, string Note) ComputePercentileRank(
        decimal price, BenchmarkDistribution distribution)
    {
        if (price <= distribution.P25)
        {
            return (25m, $"at or below the observed 25th percentile (P25 = {Fmt(distribution.P25)})");
        }

        if (price >= distribution.P75)
        {
            return (75m, $"at or above the observed 75th percentile (P75 = {Fmt(distribution.P75)})");
        }

        if (price <= distribution.P50)
        {
            var fraction = (price - distribution.P25) / (distribution.P50 - distribution.P25);
            return (25m + fraction * 25m,
                $"interpolated between P25 ({Fmt(distribution.P25)}) and P50 ({Fmt(distribution.P50)})");
        }

        var fractionAboveMedian = (price - distribution.P50) / (distribution.P75 - distribution.P50);
        return (50m + fractionAboveMedian * 25m,
            $"interpolated between P50 ({Fmt(distribution.P50)}) and P75 ({Fmt(distribution.P75)})");
    }

    /// <summary>Culture-invariant, unpadded decimal formatting for explanation strings — so
    /// <see cref="PriceComparisonResult.Explanation"/> text (and any test asserting against it) is
    /// stable regardless of the running culture, the same convention
    /// <c>Raffa.Renewals.Application.PriorityScoreCalculator.Fmt</c> already established.</summary>
    private static string Fmt(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
