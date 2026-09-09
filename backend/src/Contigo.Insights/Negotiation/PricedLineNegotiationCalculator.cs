using System.Globalization;
using Contigo.Benchmark.Contracts;
using Contigo.Insights.Contracts;
using Contigo.SharedKernel;

namespace Contigo.Insights.Negotiation;

/// <summary>
/// The generalized priced-line negotiation calculator (task E13/F07/US01/T01, insights-calculators;
/// parent story us-01-insights AC-3/AC-4; R-STR-02). Computes the same opening target / acceptable
/// range / walk-away threshold / seven canonical levers
/// <c>Contigo.Quotes.Application.Strategy.NegotiationStrategyCalculator</c> computes for a quote
/// line, but for any <see cref="PricedLine"/> — a contract line item included — so "a contract line
/// item yields opening target, acceptable range, walk-away and levers exactly as a quote line does"
/// (AC-3). Pure and synchronous: no database/HTTP/LLM call anywhere in <see cref="Compute"/>
/// (Appendix C rule 6).
///
/// <para>
/// <b>Shares its numeric anchor with the Quotes calculator, not its whole implementation</b>: the
/// opening/walk-away stepping step calls <see cref="PricedLineNegotiationMath.StepRange"/> — the
/// same function <c>NegotiationStrategyCalculator.Compute</c> now also calls — so two independently
/// computed target ranges of the same width, from the same unit price, always step to the same
/// opening/walk-away figures (proved directly in
/// <c>Contigo.Insights.Tests.PricedLineNegotiationCalculatorTests</c> by feeding both calculators
/// equivalent inputs). The earlier "raw distribution -&gt; recommended range" step
/// (<see cref="ComputeTargetRange"/> below) is this calculator's own, independent arithmetic — it
/// mirrors <c>Contigo.Quotes.Application.Assessment.TargetSavingCalculator.Compute</c>'s own P25/P50
/// clamped-to-unit-price formula (duplicated, not referenced: <c>Contigo.Insights</c> cannot
/// reference <c>Contigo.Quotes</c> — ADR-002) because <see cref="PricedLine"/> carries a raw
/// <c>BenchmarkDistribution</c>, not an already-computed range, and no task has asked
/// <c>NegotiationStrategyCalculator</c>'s own public signature to change (it still takes a
/// pre-computed <c>LineTargetSaving</c> — "existing quote strategy tests must pass unchanged").
/// </para>
///
/// <para>
/// <b>Levers never abstain, unlike the Quotes calculator</b> (AC-4: "no benchmark match → no
/// targets, levers only, 'insufficient market data' stated"): when
/// <see cref="ComputeTargetRange"/> cannot anchor a range, <see cref="Compute"/> still returns the
/// full seven-lever playbook (the ones that do not need a benchmark — volume, term, quarter-end,
/// bundle — stay grounded in this line's own data; utilization/alternatives/payment-terms stay
/// generic, the same "no source field exists" honesty
/// <c>Contigo.Quotes.Application.Strategy.NegotiationStrategyCalculator.BuildLevers</c>'s own doc
/// comment already establishes) — a deliberate divergence from that calculator's own "no range, no
/// levers" rule, because R-CMP-01 AC-2 makes the identical call for the benchmark-comparison case:
/// "A supplier not in the feed → insufficient data + the levers that do not need a benchmark".
/// </para>
/// </summary>
public static class PricedLineNegotiationCalculator
{
    /// <summary>Same threshold <c>NegotiationStrategyCalculator</c> uses for the quarter-end lever —
    /// see that class's own doc comment for why this is a V1 planning constant, not an ADR/spec
    /// pin.</summary>
    private const int QuarterEndProximityDays = 14;

    /// <summary>
    /// Computes <paramref name="line"/>'s negotiation targets and levers. <paramref name="contractId"/>
    /// + <paramref name="lineIndex"/> (this line's zero-based position among
    /// <paramref name="totalPricedLineCountOnContract"/> siblings, in the caller's own list order)
    /// build this line's own citation keys (<see cref="InsightsCitationKeys.Fact"/>) — a
    /// <see cref="PricedLine"/> carries no id of its own, so the caller's own list position is the
    /// only stable "which line" pointer available. <paramref name="asOfDate"/> is the negotiation-
    /// timing reference date for the quarter-end lever — the caller's own <c>IClock</c>-derived
    /// "today", never read from the system clock here, so this method stays pure and testable.
    /// </summary>
    public static PricedLineNegotiationResult Compute(
        EntityId contractId,
        PricedLine line,
        int lineIndex,
        int totalPricedLineCountOnContract,
        DateOnly asOfDate)
    {
        ArgumentNullException.ThrowIfNull(line);

        var (range, targetExplanation) = ComputeTargetRange(line);
        var levers = BuildLevers(contractId, line, lineIndex, totalPricedLineCountOnContract, asOfDate);

        if (range is not { } targetRange)
        {
            return new PricedLineNegotiationResult(null, null, null, null, levers, targetExplanation);
        }

        var (openingTarget, walkAwayThreshold) =
            PricedLineNegotiationMath.StepRange(targetRange.Low, targetRange.High, line.UnitPrice!.Value);

        var explanation =
            $"{targetExplanation} Opening target {Fmt(openingTarget)}, walk-away threshold " +
            $"{Fmt(walkAwayThreshold)} — opening stepped one range-width below the low end, " +
            "walk-away stepped one range-width above the high end and clamped to the current unit " +
            "price (never escalate past what is already quoted), deterministic arithmetic " +
            "(Appendix C rule 6; spec §12.1).";

        return new PricedLineNegotiationResult(
            openingTarget, targetRange.Low, targetRange.High, walkAwayThreshold, levers, explanation);
    }

    /// <summary>
    /// The "raw distribution -&gt; recommended range" step — see this type's own doc comment for why
    /// it independently mirrors <c>TargetSavingCalculator.Compute</c>'s formula rather than sharing
    /// it. <c>RecommendedLow = min(P25, unitPrice)</c>, <c>RecommendedHigh = min(P50, unitPrice)</c>,
    /// both clamped through the current price so this calculator never recommends paying more than
    /// today's price. Never fabricates: a missing unit price, a missing/insufficient benchmark, or a
    /// not-well-ordered distribution all return <see langword="null"/> with a named,
    /// "insufficient market data" reason (Appendix C rule 10; ADR-001) rather than a range computed
    /// from data that cannot support it.
    /// </summary>
    private static ((decimal Low, decimal High) Range, string Explanation) ComputeTargetRangeCore(
        BenchmarkDistribution distribution, decimal unitPrice)
    {
        var low = Math.Min(distribution.P25, unitPrice);
        var high = Math.Min(distribution.P50, unitPrice);

        return ((low, high),
            $"Recommended target range [{Fmt(low)}, {Fmt(high)}] from the matched benchmark's P25/P50 " +
            $"clamped to the current unit price {Fmt(unitPrice)} (deterministic arithmetic, Appendix C rule 6).");
    }

    private static ((decimal Low, decimal High)? Range, string Explanation) ComputeTargetRange(PricedLine line)
    {
        if (line.UnitPrice is not { } unitPrice)
        {
            return (null,
                "PricedLine.UnitPrice is not recorded — a target range cannot be anchored to a " +
                "current price that does not exist: insufficient market data (Appendix C rule 10).");
        }

        if (line.Benchmark is not { } distribution)
        {
            return (null,
                "PricedLine.Benchmark is null — the matched comparables were too thin to publish " +
                "P25/P50/P75, so a target range cannot be computed without fabricating one: " +
                "insufficient market data (Appendix C rule 10; ADR-001).");
        }

        if (!(distribution.P25 <= distribution.P50 && distribution.P50 <= distribution.P75))
        {
            return (null,
                $"PricedLine.Benchmark ({Fmt(distribution.P25)}/{Fmt(distribution.P50)}/" +
                $"{Fmt(distribution.P75)}) is not well-ordered (P25 <= P50 <= P75 does not hold): " +
                "insufficient market data (Appendix C rule 10).");
        }

        var (range, explanation) = ComputeTargetRangeCore(distribution, unitPrice);
        return (range, explanation);
    }

    private static IReadOnlyList<ContractNegotiationLever> BuildLevers(
        EntityId contractId, PricedLine line, int lineIndex, int totalPricedLineCountOnContract, DateOnly asOfDate) =>
        [
            new ContractNegotiationLever(
                ContractNegotiationLeverType.Volume, VolumeRationale(line), VolumeCitationKeys(contractId, line, lineIndex)),
            new ContractNegotiationLever(
                ContractNegotiationLeverType.Term, TermRationale(line), TermCitationKeys(contractId, line, lineIndex)),
            new ContractNegotiationLever(ContractNegotiationLeverType.Utilization, UtilizationRationale(), []),
            new ContractNegotiationLever(ContractNegotiationLeverType.Alternatives, AlternativesRationale(), []),
            new ContractNegotiationLever(
                ContractNegotiationLeverType.QuarterEnd, QuarterEndRationale(asOfDate),
                [InsightsCitationKeys.Calc("as-of-date")]),
            new ContractNegotiationLever(
                ContractNegotiationLeverType.Bundle, BundleRationale(totalPricedLineCountOnContract),
                [InsightsCitationKeys.Calc("priced-line-count")]),
            new ContractNegotiationLever(ContractNegotiationLeverType.PaymentTerms, PaymentTermsRationale(), []),
        ];

    private static string VolumeRationale(PricedLine line) =>
        line.Quantity is { } quantity && quantity > 0m
            ? $"This line orders {Fmt(quantity)} — cite the order size to request a volume-tier discount."
            : "No quantity is recorded on this line — volume-based leverage cannot be sized without one.";

    private static IReadOnlyList<string> VolumeCitationKeys(EntityId contractId, PricedLine line, int lineIndex) =>
        line.Quantity is { } quantity && quantity > 0m
            ? [InsightsCitationKeys.Fact(contractId, $"priced-line[{lineIndex}].quantity")]
            : [];

    private static string TermRationale(PricedLine line) =>
        line.TermMonths is { } months && months > 0
            ? $"Committed term is {months.ToString(CultureInfo.InvariantCulture)} months — a longer " +
              "commitment is a standard trade for a lower unit rate."
            : "No commitment term is recorded on this line — term-length leverage cannot be sized without one.";

    private static IReadOnlyList<string> TermCitationKeys(EntityId contractId, PricedLine line, int lineIndex) =>
        line.TermMonths is { } months && months > 0
            ? [InsightsCitationKeys.Fact(contractId, $"priced-line[{lineIndex}].termMonths")]
            : [];

    private static string UtilizationRationale() =>
        "No usage/utilization data is captured for this line yet — if actual consumption is below " +
        "the contracted tier, cite it to right-size the commitment down.";

    private static string AlternativesRationale() =>
        "No alternative-supplier quote is captured for this line yet — a competing quote, if one " +
        "exists, is the strongest lever available.";

    private static string QuarterEndRationale(DateOnly asOfDate)
    {
        var daysToQuarterEnd = DaysToNearestCalendarQuarterEnd(asOfDate);
        var asOfText = asOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return daysToQuarterEnd <= QuarterEndProximityDays
            ? $"Today ({asOfText}) is within {daysToQuarterEnd.ToString(CultureInfo.InvariantCulture)} " +
              "day(s) of a calendar quarter-end — suppliers are often more flexible closing before " +
              "their own quarter-end."
            : $"Today ({asOfText}) is {daysToQuarterEnd.ToString(CultureInfo.InvariantCulture)} day(s) " +
              "from the nearest calendar quarter-end — no immediate quarter-end pressure to cite.";
    }

    private static string BundleRationale(int totalPricedLineCountOnContract) =>
        totalPricedLineCountOnContract > 1
            ? $"This contract already bundles {totalPricedLineCountOnContract.ToString(CultureInfo.InvariantCulture)} " +
              "priced lines — consolidate them into one ask for combined-volume terms."
            : "No other priced lines are bundled on this contract — if this supplier sells other " +
              "products/services this tenant already buys, cite them to negotiate a bundle discount.";

    private static string PaymentTermsRationale() =>
        "No payment-term data is captured for this line yet — offering faster payment (e.g. net-15) " +
        "in exchange for a price concession is a standard, always-available lever.";

    /// <summary>Same wrap-safe nearest-calendar-quarter-end distance as
    /// <c>NegotiationStrategyCalculator.DaysToNearestCalendarQuarterEnd</c> — see that method's own
    /// doc comment.</summary>
    private static int DaysToNearestCalendarQuarterEnd(DateOnly date)
    {
        var year = date.Year;
        ReadOnlySpan<DateOnly> quarterEnds =
        [
            new DateOnly(year - 1, 12, 31),
            new DateOnly(year, 3, 31),
            new DateOnly(year, 6, 30),
            new DateOnly(year, 9, 30),
            new DateOnly(year, 12, 31),
            new DateOnly(year + 1, 3, 31),
        ];

        var minDistance = int.MaxValue;
        foreach (var quarterEnd in quarterEnds)
        {
            var distance = Math.Abs(quarterEnd.DayNumber - date.DayNumber);
            if (distance < minDistance)
            {
                minDistance = distance;
            }
        }

        return minDistance;
    }

    /// <summary>Culture-invariant, unpadded decimal formatting for explanation strings — same
    /// convention every other calculator in this codebase already establishes.</summary>
    private static string Fmt(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
