using System.Globalization;
using Raffa.Benchmark.Contracts;

namespace Raffa.Quotes.Application.Assessment;

/// <summary>
/// The deterministic recommended-target-range + potential-saving calculator (task E05/F02/US01/T02,
/// target-saving; parent story us-01-market-assessment AC-2's own "recommended target range +
/// potential saving" half — the above/in-line/below flag is task-01's own, separate scope; see
/// <see cref="MarketAssessmentCalculator"/>'s own doc comment). Pure and synchronous: no database
/// call, no HTTP call, no LLM call anywhere in <see cref="Compute"/> — the same
/// <paramref name="unitPrice"/>/<paramref name="quantity"/>/<paramref name="benchmark"/> triple
/// always produces the same <see cref="LineTargetSaving"/> (Appendix C rule 6), the same determinism
/// convention <see cref="MarketAssessmentCalculator.Classify"/> already established for the
/// analogous above/in-line/below flag.
///
/// <para>
/// Formula mirrors <c>Raffa.Savings.Application.PriceNormalizationCalculator.Compare</c>'s own
/// target/savings-range arithmetic exactly (duplicated, not referenced — ADR-002 forbids
/// <c>Raffa.Quotes</c> from referencing <c>Raffa.Savings</c>; its own allowed Raffa references
/// are exactly <c>[SharedKernel, Benchmark]</c> — see <c>Raffa.Quotes.Domain.MarketConfidenceLevel</c>'s
/// own doc comment for why): <c>RecommendedTargetLow = min(P25, unitPrice)</c>,
/// <c>RecommendedTargetHigh = min(P50, unitPrice)</c> — both clamped through the current price, so
/// this calculator never recommends paying <em>more</em> than the current price, and
/// <c>P25 &lt;= P50</c> (validated below) guarantees <c>RecommendedTargetLow &lt;= RecommendedTargetHigh</c>
/// always. The savings range is the mirror image: <c>SavingsRangeLow = unitPrice - RecommendedTargetHigh</c>
/// (the smaller, conservative saving), <c>SavingsRangeHigh = unitPrice - RecommendedTargetLow</c> (the
/// larger, aggressive saving) — both <c>&gt;= 0</c> by construction. <c>TotalSavingsRange{Low,High}</c>
/// scale the per-unit figures by <paramref name="quantity"/> — spec §11.2's own "Potential saving"
/// row example (<c>CHF 80-110k</c>) is a total, not a per-unit rate.
/// </para>
///
/// <para>
/// Never fabricates: a benchmark with no published distribution, or one whose markers are not
/// well-ordered (<c>P25 &lt;= P50 &lt;= P75</c>), returns a <see cref="LineTargetSaving"/> with every
/// numeric field <see langword="null"/> (plus a named reason) rather than a target/saving computed
/// from data that cannot support it (Appendix C rule 10) — the same defensive check
/// <see cref="MarketAssessmentCalculator.Classify"/> already applies to this exact same distribution
/// shape, duplicated here (not delegated) so this calculator stays independently correct and
/// testable even if <c>Classify</c> were never called first.
/// </para>
///
/// <para>
/// Deliberately does <b>not</b> re-validate <paramref name="unitPrice"/>/<paramref name="quantity"/>
/// positivity: <c>Raffa.Quotes.Application.Assessment.MarketAssessmentQueryBuilder.Build</c>
/// already rejects a <see cref="Raffa.Quotes.Domain.QuoteLine"/> with no positive
/// <c>UnitPrice</c>/<c>Quantity</c> before any benchmark call is even attempted — this calculator's
/// only real caller, <see cref="LineMarketAssessment.TargetSaving"/> (via
/// <see cref="MarketAssessmentService"/>), never reaches here otherwise. The same trust boundary
/// <see cref="MarketAssessmentCalculator.Classify"/> already takes for <paramref name="unitPrice"/>.
/// </para>
/// </summary>
public static class TargetSavingCalculator
{
    /// <summary>
    /// Computes <paramref name="unitPrice"/>'s recommended target range and potential saving against
    /// <paramref name="benchmark"/>, scaling the total figures by <paramref name="quantity"/>. See
    /// this type's own doc comment for the exact formula and its defensive-abstain conditions; every
    /// branch is covered by <c>Raffa.Quotes.Tests.TargetSavingCalculatorTests</c>.
    /// </summary>
    public static LineTargetSaving Compute(decimal unitPrice, decimal quantity, BenchmarkResult benchmark)
    {
        ArgumentNullException.ThrowIfNull(benchmark);

        if (benchmark.Distribution is not { } distribution)
        {
            return new LineTargetSaving(
                null, null, null, null, null, null,
                "Benchmark.Distribution is null (Benchmark.HasSufficientData is false): the " +
                "adapter had too few comparables to publish P25/P50/P75, so a recommended " +
                "target/saving cannot be computed without fabricating a distribution (Appendix C " +
                "rule 10; ADR-001).");
        }

        if (!(distribution.P25 <= distribution.P50 && distribution.P50 <= distribution.P75))
        {
            return new LineTargetSaving(
                null, null, null, null, null, null,
                $"Benchmark.Distribution ({Fmt(distribution.P25)}/{Fmt(distribution.P50)}/" +
                $"{Fmt(distribution.P75)}) is not well-ordered (P25 <= P50 <= P75 does not hold): " +
                "a target/saving computed against it would not be meaningful (Appendix C rule 10).");
        }

        var targetLow = Math.Min(distribution.P25, unitPrice);
        var targetHigh = Math.Min(distribution.P50, unitPrice);
        var savingsLow = unitPrice - targetHigh;
        var savingsHigh = unitPrice - targetLow;
        var totalSavingsLow = savingsLow * quantity;
        var totalSavingsHigh = savingsHigh * quantity;

        return new LineTargetSaving(
            targetLow,
            targetHigh,
            savingsLow,
            savingsHigh,
            totalSavingsLow,
            totalSavingsHigh,
            $"Recommended target range [{Fmt(targetLow)}, {Fmt(targetHigh)}] {benchmark.Currency} " +
            $"per unit; potential saving [{Fmt(savingsLow)}, {Fmt(savingsHigh)}] {benchmark.Currency} " +
            $"per unit (total [{Fmt(totalSavingsLow)}, {Fmt(totalSavingsHigh)}] {benchmark.Currency} " +
            $"across {Fmt(quantity)} units), deterministic arithmetic (Appendix C rule 6).");
    }

    /// <summary>Culture-invariant, unpadded decimal formatting for explanation strings — the same
    /// convention <see cref="MarketAssessmentCalculator"/>'s own private <c>Fmt</c> helper already
    /// established for this module's other explanation text (kept as its own copy here, not shared —
    /// the same "each calculator owns its own formatting helper" pattern
    /// <c>Raffa.Savings.Application.SavingsProvenanceClassifier</c>'s own <c>Fmt</c> already
    /// follows).</summary>
    private static string Fmt(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
