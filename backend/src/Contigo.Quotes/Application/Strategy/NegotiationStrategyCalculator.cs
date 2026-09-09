using System.Globalization;
using Contigo.Benchmark.Contracts;
using Contigo.Quotes.Application.Assessment;
using Contigo.Quotes.Domain;

namespace Contigo.Quotes.Application.Strategy;

/// <summary>
/// The deterministic negotiation-strategy calculator (task E05/F03/US01/T01, negotiation-strategy;
/// parent story us-01-negotiation-strategy AC-1 "Generate opening target, acceptable range,
/// walk-away threshold, levers, rationale"; spec §12.1's own output table). Pure and synchronous: no
/// database call, no HTTP call, no LLM call anywhere in <see cref="Compute"/> — the same
/// <paramref name="targetSaving"/>/<paramref name="line"/>/<paramref name="asOfDate"/> input always
/// produces the same <see cref="LineNegotiationStrategy"/> (AC-3 "Arithmetic (target/saving) is
/// deterministic"; Appendix C rule 6), the same determinism convention
/// <see cref="Assessment.TargetSavingCalculator.Compute"/> and
/// <see cref="Assessment.MarketAssessmentCalculator.Classify"/> already established for this
/// module's other calculators.
///
/// <para>
/// <b>Range anchor, not a fresh benchmark read</b>: <see cref="AcceptableRangeLow"/>/
/// <see cref="AcceptableRangeHigh"/> (on the returned <see cref="LineNegotiationStrategy"/>) echo
/// <paramref name="targetSaving"/>'s own <c>RecommendedTargetLow</c>/<c>RecommendedTargetHigh</c>
/// verbatim rather than re-deriving them from a <c>BenchmarkResult</c> a second time — spec §12.1's
/// "Acceptable target range" row is literally spec §11.2's own "Recommended target" row carried
/// forward into the negotiation-recommendation step (compare the two tables: both examples read
/// "CHF 410-440k"), not a second, independently-computed figure. <see cref="OpeningTarget"/> is
/// stepped one range-width below that anchor, and <see cref="WalkAwayThreshold"/> one range-width
/// above it, clamped to <paramref name="line"/>'s own current <c>UnitPrice</c> (never recommend
/// escalating past what is already quoted — the same clamp
/// <see cref="Assessment.TargetSavingCalculator"/> already applies to its own
/// <c>RecommendedTargetHigh</c>). Spec §12.1's own illustrative example (opening 400k / range
/// 410-440k / walk-away 470k / quote 520k) is exactly reproduced for the walk-away figure by this
/// formula (440k + (440k-410k) = 470k) — illustrative numbers, not a specified formula, the same
/// "example, not a worked formula" treatment <c>TargetSavingCalculator</c>'s own doc comment already
/// gives spec §11.2's numbers.
/// </para>
///
/// <para>
/// <b>Why this stays inside <c>Contigo.Quotes</c>, never calling <c>Contigo.AiGateway</c></b>:
/// <c>Contigo.ArchitectureTests.DependencyDirectionTests</c>' allowed-reference set for this module
/// is exactly <c>[SharedKernel, Benchmark]</c> — the same boundary
/// <see cref="Assessment.TargetSavingCalculator"/>'s own doc comment already cites. AC-3's "only
/// language is LLM" (Appendix C rule 6: prefer deterministic arithmetic to LLM reasoning) is
/// honoured here by keeping every number in this pure calculator; the per-lever
/// <see cref="NegotiationLever.Rationale"/> text is V1 deterministic language, the same
/// "Explanation is always a computed string, never a model call" convention every other calculator
/// in this module already follows for its own explanation text. Task E05/F01/US01/T01
/// (quote-extraction) already established the precedent for how a real AI Gateway `answer`-role call
/// would eventually attach to this module without giving it a direct
/// <c>Contigo.AiGateway</c> reference: <c>Contigo.Api.QuoteExtractionPipeline</c>, the composition
/// root, is the one place that calls both <c>Contigo.AiGateway</c> and <c>Contigo.Quotes</c> for the
/// `extract` role — a future task would wire the `answer` role there the same way, feeding it this
/// calculator's own deterministic facts as evidence, never asking the model to invent the facts
/// themselves. <c>Contigo.AiGateway.Fixtures.FixtureAiGateway.AnswerAsync</c> would today only echo
/// those facts back verbatim (no live grounded-generation model exists yet — see that method's own
/// doc comment), so deferring that wiring loses no real capability today.
/// </para>
///
/// <para>
/// <b>Generalized to a shared priced-line input (task E13/F07/US01/T01, insights-calculators; parent
/// story us-01-insights AC-3)</b>: the opening-target/walk-away-threshold arithmetic below now calls
/// <see cref="PricedLineNegotiationMath.StepRange"/> — the one piece of this formula
/// <c>Contigo.Insights.Negotiation.PricedLineNegotiationCalculator</c> (contract priced lines) must
/// reproduce bit-for-bit for a <c>Contigo.Benchmark.Contracts.PricedLine</c> to "yield opening
/// target, acceptable range, walk-away and levers exactly as a quote line does". That shared step
/// lives in <c>Contigo.Benchmark</c>, not <c>Contigo.Insights</c>: <c>Contigo.Quotes</c>' own
/// allow-list (<c>Contigo.ArchitectureTests.DependencyDirectionTests</c>) is exactly
/// <c>[SharedKernel, Benchmark]</c> — it cannot reference <c>Contigo.Insights</c> — so the shared
/// arithmetic sits in the one project both modules already see (see
/// <c>PricedLineNegotiationMath</c>'s own doc comment; <c>backend/README.md</c> "Insights" records
/// this decision). This method's own public signature, every other line of logic (levers, abstain
/// conditions, explanation text) and its return values are unchanged — the same
/// <see cref="LineTargetSaving"/>/<see cref="Contigo.Quotes.Domain.QuoteLine"/> in still produces the
/// same <see cref="LineNegotiationStrategy"/> out, so every existing
/// <c>Contigo.Quotes.Tests.NegotiationStrategyCalculatorTests</c> assertion holds unchanged (decimal
/// arithmetic is exact, so moving these two lines into a shared function changes nothing about the
/// values they produce).
/// </para>
/// </summary>
public static class NegotiationStrategyCalculator
{
    /// <summary>
    /// A calendar date within this many days of a quarter-end (Mar 31 / Jun 30 / Sep 30 / Dec 31) is
    /// treated as "quarter-end pressure" for the <see cref="NegotiationLeverType.QuarterEnd"/> lever.
    /// Not ADR/spec-pinned — a V1 planning constant, documented here (not hidden) so a future task
    /// can promote it to a configuration option if a real quarter-end cadence needs to differ (e.g. a
    /// supplier on a non-calendar fiscal year).
    /// </summary>
    private const int QuarterEndProximityDays = 14;

    /// <summary>
    /// Computes <paramref name="line"/>'s negotiation strategy from its already-computed
    /// <paramref name="targetSaving"/> (task E05/F02/US01/T02, target-saving), evaluated as of
    /// <paramref name="asOfDate"/> (the negotiation-timing reference date for the
    /// <see cref="NegotiationLeverType.QuarterEnd"/> lever — the caller's <c>IClock</c>-derived
    /// "today", never <see cref="DateTime.Today"/> read directly here, so this method stays pure and
    /// testable). <paramref name="totalLineCountOnQuote"/> is how many <c>QuoteLine</c> rows exist on
    /// this line's own quote (including this line itself) — the
    /// <see cref="NegotiationLeverType.Bundle"/> lever's only input beyond <paramref name="line"/>
    /// itself. Never fabricates: when <paramref name="targetSaving"/> has no usable range, or
    /// <paramref name="line"/> has no current <c>UnitPrice</c> to clamp a walk-away threshold to,
    /// returns a <see cref="LineNegotiationStrategy"/> with every numeric field
    /// <see langword="null"/> and an empty lever list, plus a named reason (Appendix C rule 10) —
    /// the same honest-abstain shape <see cref="Assessment.TargetSavingCalculator.Compute"/> already
    /// established for the identical "no usable benchmark distribution" condition.
    /// </summary>
    public static LineNegotiationStrategy Compute(
        QuoteLine line,
        int totalLineCountOnQuote,
        LineTargetSaving? targetSaving,
        DateOnly asOfDate)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (targetSaving is not { RecommendedTargetLow: { } rangeLow, RecommendedTargetHigh: { } rangeHigh })
        {
            return new LineNegotiationStrategy(
                line.Id, null, null, null, null, [],
                "No recommended target range is available for this line " +
                (targetSaving is null
                    ? "(no target-saving was computed at all — see the market assessment's own " +
                      "status/explanation for why)"
                    : $"(LineTargetSaving.Explanation: \"{targetSaving.Explanation}\")") +
                " — a negotiation strategy cannot be anchored without one (Appendix C rule 10).");
        }

        if (line.UnitPrice is not { } unitPrice)
        {
            return new LineNegotiationStrategy(
                line.Id, null, null, null, null, [],
                "QuoteLine.UnitPrice is not recorded — a walk-away threshold cannot be clamped to a " +
                "current price that does not exist (Appendix C rule 10).");
        }

        var (openingTarget, walkAwayThreshold) =
            PricedLineNegotiationMath.StepRange(rangeLow, rangeHigh, unitPrice);

        var levers = BuildLevers(line, totalLineCountOnQuote, asOfDate);

        var explanation =
            $"Opening target {Fmt(openingTarget)}, acceptable range [{Fmt(rangeLow)}, {Fmt(rangeHigh)}] " +
            $"(from LineTargetSaving.RecommendedTarget{{Low,High}}, task E05/F02/US01/T02), walk-away " +
            $"threshold {Fmt(walkAwayThreshold)} — opening stepped one range-width below the low end, " +
            "walk-away stepped one range-width above the high end and clamped to the current unit " +
            $"price {Fmt(unitPrice)} (never escalate past what is already quoted), deterministic " +
            "arithmetic (Appendix C rule 6; spec §12.1).";

        return new LineNegotiationStrategy(
            line.Id, openingTarget, rangeLow, rangeHigh, walkAwayThreshold, levers, explanation);
    }

    /// <summary>
    /// Always returns exactly the seven canonical <see cref="NegotiationLeverType"/> values, in
    /// spec §12.1's own listed order — see <see cref="NegotiationLever"/>'s own doc comment for why
    /// a variable-length subset would be a worse default. Three levers ground themselves in this
    /// line/quote's own recorded data when it exists (<see cref="NegotiationLeverType.Volume"/> via
    /// <c>QuoteLine.Quantity</c>, <see cref="NegotiationLeverType.Term"/> via <c>QuoteLine.Term</c>,
    /// <see cref="NegotiationLeverType.Bundle"/> via <paramref name="totalLineCountOnQuote"/>) plus
    /// one date-derived lever (<see cref="NegotiationLeverType.QuarterEnd"/> via
    /// <paramref name="asOfDate"/>); the remaining three
    /// (<see cref="NegotiationLeverType.Utilization"/>, <see cref="NegotiationLeverType.Alternatives"/>,
    /// <see cref="NegotiationLeverType.PaymentTerms"/>) have no source field anywhere in this module's
    /// schema today, so their rationale honestly says so rather than fabricating a specific this-quote
    /// fact (Appendix C rule 10).
    ///
    /// <para>
    /// Task E05/F03/US01/T02 (strategy-evidence, AC-2): the same four grounded levers also carry a
    /// non-empty <see cref="NegotiationLever.Evidence"/> — see each lever's own <c>*Evidence</c>
    /// helper (<see cref="VolumeEvidence"/>, <see cref="TermEvidence"/>,
    /// <see cref="QuarterEndEvidence"/>, <see cref="BundleEvidence"/>) — while the remaining three
    /// stay evidence-empty for the identical "no source field exists" reason their rationale
    /// already gives.
    /// </para>
    /// </summary>
    private static IReadOnlyList<NegotiationLever> BuildLevers(
        QuoteLine line, int totalLineCountOnQuote, DateOnly asOfDate) =>
        [
            new NegotiationLever(NegotiationLeverType.Volume, VolumeRationale(line), VolumeEvidence(line)),
            new NegotiationLever(NegotiationLeverType.Term, TermRationale(line), TermEvidence(line)),
            new NegotiationLever(NegotiationLeverType.Utilization, UtilizationRationale(), []),
            new NegotiationLever(NegotiationLeverType.Alternatives, AlternativesRationale(), []),
            new NegotiationLever(
                NegotiationLeverType.QuarterEnd, QuarterEndRationale(asOfDate), QuarterEndEvidence(asOfDate)),
            new NegotiationLever(
                NegotiationLeverType.Bundle, BundleRationale(totalLineCountOnQuote), BundleEvidence(totalLineCountOnQuote)),
            new NegotiationLever(NegotiationLeverType.PaymentTerms, PaymentTermsRationale(), []),
        ];

    private static string VolumeRationale(QuoteLine line) =>
        line.Quantity is { } quantity && quantity > 0m
            ? $"This line orders {Fmt(quantity)}{(string.IsNullOrWhiteSpace(line.Unit) ? string.Empty : " " + line.Unit)} " +
              "— cite the order size to request a volume-tier discount."
            : "No quantity is recorded on this line — volume-based leverage cannot be sized without one.";

    /// <summary>
    /// Structured evidence for <see cref="VolumeRationale"/> (task E05/F03/US01/T02,
    /// strategy-evidence; AC-2) — <c>QuoteLine.Quantity</c> (plus <c>QuoteLine.Unit</c>, when
    /// recorded), each carrying this same line's own extraction <c>SourceSpan</c>/<c>SourcePage</c>/
    /// <c>Confidence</c> (Appendix C rule 2's "confidence metadata"): both fields are ones the AI
    /// Gateway `extract` role originally proposed for this line, and a <see cref="QuoteLine"/> row
    /// is one extraction event covering the whole row (see that type's own doc comment). Empty
    /// under the identical "no quantity recorded" condition that already makes
    /// <see cref="VolumeRationale"/> read as ungrounded (Appendix C rule 10 — never cite evidence
    /// for a fact that is not actually there).
    /// </summary>
    private static IReadOnlyList<NegotiationLeverEvidence> VolumeEvidence(QuoteLine line)
    {
        if (line.Quantity is not { } quantity || quantity <= 0m)
        {
            return [];
        }

        var evidence = new List<NegotiationLeverEvidence>
        {
            new NegotiationLeverEvidence(
                $"{nameof(QuoteLine)}.{nameof(QuoteLine.Quantity)}",
                Fmt(quantity),
                line.SourceSpan,
                line.SourcePage,
                line.Confidence),
        };

        if (!string.IsNullOrWhiteSpace(line.Unit))
        {
            evidence.Add(new NegotiationLeverEvidence(
                $"{nameof(QuoteLine)}.{nameof(QuoteLine.Unit)}",
                line.Unit,
                line.SourceSpan,
                line.SourcePage,
                line.Confidence));
        }

        return evidence;
    }

    private static string TermRationale(QuoteLine line) =>
        !string.IsNullOrWhiteSpace(line.Term)
            ? $"Quoted term is \"{line.Term}\"" +
              (line.NormalizedTermMonths is { } months
                  ? $" ({months.ToString(CultureInfo.InvariantCulture)} months)"
                  : string.Empty) +
              " — a longer commitment is a standard trade for a lower unit rate."
            : "No commitment term is recorded on this line — term-length leverage cannot be sized without one.";

    /// <summary>
    /// Structured evidence for <see cref="TermRationale"/> (task E05/F03/US01/T02,
    /// strategy-evidence; AC-2) — <c>QuoteLine.Term</c>, carrying this line's own extraction
    /// <c>SourceSpan</c>/<c>SourcePage</c>/<c>Confidence</c> (same reasoning as
    /// <see cref="VolumeEvidence"/>), plus <c>QuoteLine.NormalizedTermMonths</c> when present — with
    /// no span/page/confidence of its own, because it is not a second, independently-extracted
    /// fact: <c>Contigo.Quotes.Application.Normalization.QuoteLineNormalizationService</c> derives
    /// it deterministically from <see cref="QuoteLine.Term"/> in code (Appendix C rule 6), so its
    /// only real evidence is the <see cref="QuoteLine.Term"/> citation immediately before it. Empty
    /// under the identical "no term recorded" condition that already makes
    /// <see cref="TermRationale"/> read as ungrounded.
    /// </summary>
    private static IReadOnlyList<NegotiationLeverEvidence> TermEvidence(QuoteLine line)
    {
        if (string.IsNullOrWhiteSpace(line.Term))
        {
            return [];
        }

        var evidence = new List<NegotiationLeverEvidence>
        {
            new NegotiationLeverEvidence(
                $"{nameof(QuoteLine)}.{nameof(QuoteLine.Term)}",
                line.Term,
                line.SourceSpan,
                line.SourcePage,
                line.Confidence),
        };

        if (line.NormalizedTermMonths is { } months)
        {
            evidence.Add(new NegotiationLeverEvidence(
                $"{nameof(QuoteLine)}.{nameof(QuoteLine.NormalizedTermMonths)}",
                months.ToString(CultureInfo.InvariantCulture),
                SourceSpan: null,
                SourcePage: null,
                Confidence: null));
        }

        return evidence;
    }

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

    /// <summary>
    /// Structured evidence for <see cref="QuarterEndRationale"/> (task E05/F03/US01/T02,
    /// strategy-evidence; AC-2) — <paramref name="asOfDate"/> itself, the negotiation-timing
    /// reference date both <see cref="QuarterEndRationale"/> branches name. Always populated, never
    /// empty like <see cref="VolumeEvidence"/>/<see cref="TermEvidence"/> can be: unlike a
    /// quote/line field that may or may not have been recorded, <paramref name="asOfDate"/> is
    /// always known — the caller's own <c>IClock</c>-derived "today" (see this type's own doc
    /// comment) — never a document extraction, so <c>SourceSpan</c>/<c>SourcePage</c>/
    /// <c>Confidence</c> do not apply here.
    /// </summary>
    private static IReadOnlyList<NegotiationLeverEvidence> QuarterEndEvidence(DateOnly asOfDate) =>
        [
            new NegotiationLeverEvidence(
                $"{nameof(NegotiationStrategyCalculator)}.AsOfDate",
                asOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                SourceSpan: null,
                SourcePage: null,
                Confidence: null),
        ];

    private static string BundleRationale(int totalLineCountOnQuote) =>
        totalLineCountOnQuote > 1
            ? $"This quote already bundles {totalLineCountOnQuote.ToString(CultureInfo.InvariantCulture)} " +
              "line items — consolidate them into one ask for combined-volume terms."
            : "No other line items are bundled on this quote — if this supplier sells other " +
              "products/services this tenant already buys, cite them to negotiate a bundle discount.";

    /// <summary>
    /// Structured evidence for <see cref="BundleRationale"/> (task E05/F03/US01/T02,
    /// strategy-evidence; AC-2) — <paramref name="totalLineCountOnQuote"/> itself, always populated
    /// (the sibling-line count is always a known fact, whether it is 1 or many — the same "cite the
    /// real count either way, honesty over omission" posture <see cref="BundleRationale"/> already
    /// takes for its own two branches). Not a single <see cref="QuoteLine"/> field, so
    /// <c>SourceSpan</c>/<c>SourcePage</c>/<c>Confidence</c> do not apply — this is a count over
    /// already-persisted sibling rows computed by
    /// <see cref="NegotiationStrategyService.GenerateAsync"/>, not a document extraction.
    /// </summary>
    private static IReadOnlyList<NegotiationLeverEvidence> BundleEvidence(int totalLineCountOnQuote) =>
        [
            new NegotiationLeverEvidence(
                "Quote.LineCount",
                totalLineCountOnQuote.ToString(CultureInfo.InvariantCulture),
                SourceSpan: null,
                SourcePage: null,
                Confidence: null),
        ];

    private static string PaymentTermsRationale() =>
        "No payment-term data is captured for this line yet — offering faster payment (e.g. net-15) " +
        "in exchange for a price concession is a standard, always-available lever.";

    /// <summary>
    /// Days from <paramref name="date"/> to the nearest calendar-quarter-end boundary (Mar 31 / Jun
    /// 30 / Sep 30 / Dec 31), wrapping correctly across a year boundary (e.g. Jan 3 is 3 days from
    /// the *previous* Dec 31, not ~87 days from the *next* Mar 31) by comparing against every
    /// quarter-end from the prior year through the next year and taking the minimum
    /// <see cref="DateOnly.DayNumber"/> distance.
    /// </summary>
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
    /// convention <see cref="Assessment.TargetSavingCalculator"/>'s own private <c>Fmt</c> helper
    /// already established, kept as its own copy here (each calculator owns its own formatting
    /// helper).</summary>
    private static string Fmt(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
