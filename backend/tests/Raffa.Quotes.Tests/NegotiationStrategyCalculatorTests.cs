using Raffa.Quotes.Application.Assessment;
using Raffa.Quotes.Application.Strategy;
using Raffa.Quotes.Domain;
using Raffa.SharedKernel;

namespace Raffa.Quotes.Tests;

/// <summary>
/// Proves task E05/F03/US01/T01's execution step: <see cref="NegotiationStrategyCalculator"/>
/// computes a deterministic opening target/acceptable range/walk-away threshold plus the seven
/// canonical levers with rationale — parent story us-01-negotiation-strategy AC-1 — with no
/// database, no HTTP call and no LLM call anywhere in the path. Mirrors
/// <c>Raffa.Quotes.Tests.TargetSavingCalculatorTests</c>'s own shape/style (this module's other
/// "pure calculator, honest abstain, determinism" test).
/// </summary>
public sealed class NegotiationStrategyCalculatorTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    // Far from any calendar quarter-end (Mar 31/Jun 30/Sep 30/Dec 31), so the QuarterEnd lever's
    // "no immediate pressure" branch is the one under test unless a case says otherwise.
    private static readonly DateOnly MidQuarterDate = new(2026, 2, 10);

    // Task E05/F03/US01/T02 (strategy-evidence): non-null defaults so a grounded-lever test gets
    // this line's own extraction provenance to assert against without every call site having to
    // supply it; the "no fact recorded" tests below explicitly pass null for the fact itself
    // (quantity/term), which makes VolumeEvidence/TermEvidence return an empty list regardless of
    // these provenance defaults.
    private static QuoteLine Line(
        decimal? quantity = 100m,
        string? unit = "seats",
        string? term = "12 months",
        int? normalizedTermMonths = 12,
        decimal? unitPrice = 2300m,
        string? sourceSpan = "Qty 100 seats, 12 month term, USD 2,300/seat/year",
        int? sourcePage = 3,
        double? confidence = 0.92) =>
        new()
        {
            TenantId = TenantId.New(),
            QuoteId = EntityId.New(),
            Description = "Sales Cloud Enterprise",
            Quantity = quantity,
            Unit = unit,
            Term = term,
            NormalizedTermMonths = normalizedTermMonths,
            UnitPrice = unitPrice,
            SourceSpan = sourceSpan,
            SourcePage = sourcePage,
            Confidence = confidence,
            CreatedAt = CreatedAt,
        };

    private static LineTargetSaving TargetSaving(decimal? low = 1500m, decimal? high = 1800m) =>
        new(low, high, null, null, null, null, "test-fixture target-saving");

    // ----- AC-1: opening target / acceptable range / walk-away threshold -----

    [Fact]
    public void Opening_target_steps_one_range_width_below_the_low_end()
    {
        var result = NegotiationStrategyCalculator.Compute(
            Line(unitPrice: 2300m), totalLineCountOnQuote: 1, TargetSaving(1500m, 1800m), MidQuarterDate);

        // range width = 1800 - 1500 = 300; opening = 1500 - 300 = 1200
        Assert.Equal(1200m, result.OpeningTarget);
        Assert.Equal(1500m, result.AcceptableRangeLow);
        Assert.Equal(1800m, result.AcceptableRangeHigh);
    }

    [Fact]
    public void Opening_target_never_goes_negative()
    {
        // range width = 1800 - 100 = 1700; naive opening = 100 - 1700 = -1600, clamped to 0.
        var result = NegotiationStrategyCalculator.Compute(
            Line(unitPrice: 2300m), totalLineCountOnQuote: 1, TargetSaving(100m, 1800m), MidQuarterDate);

        Assert.Equal(0m, result.OpeningTarget);
    }

    [Fact]
    public void Walk_away_threshold_steps_one_range_width_above_the_high_end()
    {
        // Reproduces spec §12.1's own illustrative example: range [410,440] -> walk-away 470
        // (440 + (440-410) = 470), current price (520) never binds the clamp.
        var result = NegotiationStrategyCalculator.Compute(
            Line(unitPrice: 520_000m), totalLineCountOnQuote: 1, TargetSaving(410_000m, 440_000m), MidQuarterDate);

        Assert.Equal(470_000m, result.WalkAwayThreshold);
    }

    [Fact]
    public void Walk_away_threshold_never_exceeds_the_current_unit_price()
    {
        // Naive walk-away = 1800 + 300 = 2100, but the current price is only 1900.
        var result = NegotiationStrategyCalculator.Compute(
            Line(unitPrice: 1900m), totalLineCountOnQuote: 1, TargetSaving(1500m, 1800m), MidQuarterDate);

        Assert.Equal(1900m, result.WalkAwayThreshold);
    }

    [Fact]
    public void Zero_width_range_still_produces_a_coherent_strategy()
    {
        var result = NegotiationStrategyCalculator.Compute(
            Line(unitPrice: 1500m), totalLineCountOnQuote: 1, TargetSaving(1500m, 1500m), MidQuarterDate);

        Assert.Equal(1500m, result.OpeningTarget);
        Assert.Equal(1500m, result.WalkAwayThreshold);
    }

    [Fact]
    public void Explanation_names_the_target_range_and_walk_away_and_cites_the_rules()
    {
        var result = NegotiationStrategyCalculator.Compute(
            Line(unitPrice: 2300m), totalLineCountOnQuote: 1, TargetSaving(1500m, 1800m), MidQuarterDate);

        Assert.Contains("1200", result.Explanation);
        Assert.Contains("1500", result.Explanation);
        Assert.Contains("1800", result.Explanation);
        Assert.Contains("2100", result.Explanation);
        Assert.Contains("Appendix C rule 6", result.Explanation);
        Assert.Contains("§12.1", result.Explanation);
    }

    // ----- AC-1: levers, always all seven, in spec §12.1's own order -----

    [Fact]
    public void Always_returns_exactly_the_seven_canonical_levers_in_spec_order()
    {
        var result = NegotiationStrategyCalculator.Compute(
            Line(), totalLineCountOnQuote: 1, TargetSaving(), MidQuarterDate);

        Assert.Equal(
            [
                NegotiationLeverType.Volume,
                NegotiationLeverType.Term,
                NegotiationLeverType.Utilization,
                NegotiationLeverType.Alternatives,
                NegotiationLeverType.QuarterEnd,
                NegotiationLeverType.Bundle,
                NegotiationLeverType.PaymentTerms,
            ],
            result.Levers.Select(l => l.LeverType));
    }

    [Fact]
    public void Volume_lever_cites_the_line_quantity_and_unit_when_recorded()
    {
        var result = NegotiationStrategyCalculator.Compute(
            Line(quantity: 250m, unit: "licenses"), totalLineCountOnQuote: 1, TargetSaving(), MidQuarterDate);

        var volume = Assert.Single(result.Levers, l => l.LeverType == NegotiationLeverType.Volume);
        Assert.Contains("250", volume.Rationale);
        Assert.Contains("licenses", volume.Rationale);
    }

    // ----- AC-2: structured evidence per lever (task E05/F03/US01/T02, strategy-evidence) -----

    [Fact]
    public void Volume_lever_evidence_cites_the_quantity_and_unit_fields_with_this_lines_provenance()
    {
        var result = NegotiationStrategyCalculator.Compute(
            Line(quantity: 250m, unit: "licenses", sourceSpan: "Qty: 250 licenses", sourcePage: 2, confidence: 0.81),
            totalLineCountOnQuote: 1, TargetSaving(), MidQuarterDate);

        var volume = Assert.Single(result.Levers, l => l.LeverType == NegotiationLeverType.Volume);

        Assert.Collection(
            volume.Evidence,
            quantity =>
            {
                Assert.Equal("QuoteLine.Quantity", quantity.FieldName);
                Assert.Equal("250", quantity.Value);
                Assert.Equal("Qty: 250 licenses", quantity.SourceSpan);
                Assert.Equal(2, quantity.SourcePage);
                Assert.Equal(0.81, quantity.Confidence);
            },
            unit =>
            {
                Assert.Equal("QuoteLine.Unit", unit.FieldName);
                Assert.Equal("licenses", unit.Value);
                Assert.Equal("Qty: 250 licenses", unit.SourceSpan);
                Assert.Equal(2, unit.SourcePage);
                Assert.Equal(0.81, unit.Confidence);
            });

        // The citation and the prose can never silently disagree (NegotiationLeverEvidence's own
        // doc comment) — every cited value is a substring of the Rationale it backs.
        foreach (var evidence in volume.Evidence)
        {
            Assert.Contains(evidence.Value, volume.Rationale);
        }
    }

    [Fact]
    public void Volume_lever_evidence_omits_the_unit_citation_when_no_unit_is_recorded()
    {
        var result = NegotiationStrategyCalculator.Compute(
            Line(quantity: 250m, unit: null), totalLineCountOnQuote: 1, TargetSaving(), MidQuarterDate);

        var volume = Assert.Single(result.Levers, l => l.LeverType == NegotiationLeverType.Volume);
        var evidence = Assert.Single(volume.Evidence);
        Assert.Equal("QuoteLine.Quantity", evidence.FieldName);
    }

    [Fact]
    public void Volume_lever_is_honest_when_no_quantity_is_recorded()
    {
        var result = NegotiationStrategyCalculator.Compute(
            Line(quantity: null), totalLineCountOnQuote: 1, TargetSaving(), MidQuarterDate);

        var volume = Assert.Single(result.Levers, l => l.LeverType == NegotiationLeverType.Volume);
        Assert.Contains("No quantity is recorded", volume.Rationale);
        Assert.Empty(volume.Evidence);
    }

    [Fact]
    public void Term_lever_cites_the_recorded_term_and_normalized_months()
    {
        var result = NegotiationStrategyCalculator.Compute(
            Line(term: "36 months", normalizedTermMonths: 36), totalLineCountOnQuote: 1, TargetSaving(), MidQuarterDate);

        var term = Assert.Single(result.Levers, l => l.LeverType == NegotiationLeverType.Term);
        Assert.Contains("36 months", term.Rationale);
    }

    [Fact]
    public void Term_lever_evidence_cites_the_term_field_with_provenance_and_the_normalized_months_without()
    {
        var result = NegotiationStrategyCalculator.Compute(
            Line(
                term: "36 months", normalizedTermMonths: 36,
                sourceSpan: "Term: 36 months", sourcePage: 4, confidence: 0.77),
            totalLineCountOnQuote: 1, TargetSaving(), MidQuarterDate);

        var term = Assert.Single(result.Levers, l => l.LeverType == NegotiationLeverType.Term);

        Assert.Collection(
            term.Evidence,
            rawTerm =>
            {
                Assert.Equal("QuoteLine.Term", rawTerm.FieldName);
                Assert.Equal("36 months", rawTerm.Value);
                Assert.Equal("Term: 36 months", rawTerm.SourceSpan);
                Assert.Equal(4, rawTerm.SourcePage);
                Assert.Equal(0.77, rawTerm.Confidence);
            },
            normalizedMonths =>
            {
                // Derived deterministically from the Term field above (Appendix C rule 6), not a
                // second independently-extracted fact — no span/page/confidence of its own.
                Assert.Equal("QuoteLine.NormalizedTermMonths", normalizedMonths.FieldName);
                Assert.Equal("36", normalizedMonths.Value);
                Assert.Null(normalizedMonths.SourceSpan);
                Assert.Null(normalizedMonths.SourcePage);
                Assert.Null(normalizedMonths.Confidence);
            });
    }

    [Fact]
    public void Term_lever_evidence_omits_the_normalized_months_citation_when_it_is_absent()
    {
        var result = NegotiationStrategyCalculator.Compute(
            Line(term: "Annual", normalizedTermMonths: null), totalLineCountOnQuote: 1, TargetSaving(), MidQuarterDate);

        var term = Assert.Single(result.Levers, l => l.LeverType == NegotiationLeverType.Term);
        var evidence = Assert.Single(term.Evidence);
        Assert.Equal("QuoteLine.Term", evidence.FieldName);
    }

    [Fact]
    public void Term_lever_is_honest_when_no_term_is_recorded()
    {
        var result = NegotiationStrategyCalculator.Compute(
            Line(term: null, normalizedTermMonths: null), totalLineCountOnQuote: 1, TargetSaving(), MidQuarterDate);

        var term = Assert.Single(result.Levers, l => l.LeverType == NegotiationLeverType.Term);
        Assert.Contains("No commitment term is recorded", term.Rationale);
        Assert.Empty(term.Evidence);
    }

    [Fact]
    public void Bundle_lever_cites_the_sibling_line_count_when_more_than_one_line_is_on_the_quote()
    {
        var result = NegotiationStrategyCalculator.Compute(
            Line(), totalLineCountOnQuote: 4, TargetSaving(), MidQuarterDate);

        var bundle = Assert.Single(result.Levers, l => l.LeverType == NegotiationLeverType.Bundle);
        Assert.Contains("4", bundle.Rationale);

        // AC-2: the sibling-line count is always cited as structured evidence, not just prose —
        // and it is not a QuoteLine field, so it carries no document span/page/confidence.
        var evidence = Assert.Single(bundle.Evidence);
        Assert.Equal("Quote.LineCount", evidence.FieldName);
        Assert.Equal("4", evidence.Value);
        Assert.Null(evidence.SourceSpan);
        Assert.Null(evidence.SourcePage);
        Assert.Null(evidence.Confidence);
    }

    [Fact]
    public void Bundle_lever_is_honest_when_this_is_the_only_line_on_the_quote()
    {
        var result = NegotiationStrategyCalculator.Compute(
            Line(), totalLineCountOnQuote: 1, TargetSaving(), MidQuarterDate);

        var bundle = Assert.Single(result.Levers, l => l.LeverType == NegotiationLeverType.Bundle);
        Assert.Contains("No other line items are bundled", bundle.Rationale);

        // Still cites the real count (1) as evidence — honesty over silently omitting it just
        // because the count happens to be unfavorable to the lever (Appendix C rule 10).
        var evidence = Assert.Single(bundle.Evidence);
        Assert.Equal("Quote.LineCount", evidence.FieldName);
        Assert.Equal("1", evidence.Value);
    }

    [Theory]
    [InlineData("2026-12-20", true)]   // 11 days before Dec 31
    [InlineData("2027-01-03", true)]   // 3 days after Dec 31 (previous year) — wraps correctly
    [InlineData("2026-02-10", false)]  // 41 days from the previous Dec 31, 49 from the next Mar 31
    public void QuarterEnd_lever_reflects_proximity_to_a_calendar_quarter_end(string asOfDateText, bool expectPressure)
    {
        var asOfDate = DateOnly.Parse(asOfDateText);

        var result = NegotiationStrategyCalculator.Compute(
            Line(), totalLineCountOnQuote: 1, TargetSaving(), asOfDate);

        var quarterEnd = Assert.Single(result.Levers, l => l.LeverType == NegotiationLeverType.QuarterEnd);
        Assert.Equal(expectPressure, quarterEnd.Rationale.Contains("is within", StringComparison.Ordinal));

        // AC-2: the as-of date backing both branches is always cited as evidence — never a
        // document extraction, so no span/page/confidence.
        var evidence = Assert.Single(quarterEnd.Evidence);
        Assert.Equal("NegotiationStrategyCalculator.AsOfDate", evidence.FieldName);
        Assert.Equal(asOfDateText, evidence.Value);
        Assert.Null(evidence.SourceSpan);
        Assert.Null(evidence.SourcePage);
        Assert.Null(evidence.Confidence);
    }

    [Fact]
    public void Utilization_alternatives_and_payment_terms_levers_are_always_generic_today()
    {
        var result = NegotiationStrategyCalculator.Compute(
            Line(), totalLineCountOnQuote: 1, TargetSaving(), MidQuarterDate);

        var utilization = Assert.Single(result.Levers, l => l.LeverType == NegotiationLeverType.Utilization);
        var alternatives = Assert.Single(result.Levers, l => l.LeverType == NegotiationLeverType.Alternatives);
        var paymentTerms = Assert.Single(result.Levers, l => l.LeverType == NegotiationLeverType.PaymentTerms);

        Assert.Contains("No usage/utilization data", utilization.Rationale);
        Assert.Contains("No alternative-supplier quote", alternatives.Rationale);
        Assert.Contains("No payment-term data", paymentTerms.Rationale);

        // AC-2 / Appendix C rule 10: no source field exists for any of these three today, so their
        // evidence stays honestly empty rather than fabricating a citation.
        Assert.Empty(utilization.Evidence);
        Assert.Empty(alternatives.Evidence);
        Assert.Empty(paymentTerms.Evidence);
    }

    // ----- Honest abstain: no usable target range / no current price (Appendix C rule 10) -----

    [Fact]
    public void Abstains_with_no_levers_when_target_saving_itself_is_null()
    {
        var result = NegotiationStrategyCalculator.Compute(
            Line(), totalLineCountOnQuote: 1, targetSaving: null, MidQuarterDate);

        Assert.Null(result.OpeningTarget);
        Assert.Null(result.AcceptableRangeLow);
        Assert.Null(result.AcceptableRangeHigh);
        Assert.Null(result.WalkAwayThreshold);
        Assert.Empty(result.Levers);
        Assert.Contains("Appendix C rule 10", result.Explanation);
    }

    [Fact]
    public void Abstains_when_target_saving_has_no_recommended_range()
    {
        // The shape TargetSavingCalculator itself returns for insufficient benchmark data: every
        // numeric field null, only Explanation populated.
        var insufficientData = new LineTargetSaving(null, null, null, null, null, null, "insufficient data");

        var result = NegotiationStrategyCalculator.Compute(
            Line(), totalLineCountOnQuote: 1, insufficientData, MidQuarterDate);

        Assert.Null(result.OpeningTarget);
        Assert.Empty(result.Levers);
        Assert.Contains("insufficient data", result.Explanation);
    }

    [Fact]
    public void Abstains_when_the_line_has_no_current_unit_price()
    {
        var result = NegotiationStrategyCalculator.Compute(
            Line(unitPrice: null), totalLineCountOnQuote: 1, TargetSaving(), MidQuarterDate);

        Assert.Null(result.OpeningTarget);
        Assert.Null(result.WalkAwayThreshold);
        Assert.Empty(result.Levers);
        Assert.Contains("UnitPrice is not recorded", result.Explanation);
    }

    [Fact]
    public void Rejects_a_null_line_argument()
    {
        Assert.Throws<ArgumentNullException>(
            () => NegotiationStrategyCalculator.Compute(null!, 1, TargetSaving(), MidQuarterDate));
    }

    // ----- Determinism (Appendix C rule 6) -----

    [Fact]
    public void Same_inputs_produce_the_same_result_every_time()
    {
        var line = Line();
        var targetSaving = TargetSaving();

        var first = NegotiationStrategyCalculator.Compute(line, 2, targetSaving, MidQuarterDate);
        var second = NegotiationStrategyCalculator.Compute(line, 2, targetSaving, MidQuarterDate);

        Assert.Equal(first.OpeningTarget, second.OpeningTarget);
        Assert.Equal(first.AcceptableRangeLow, second.AcceptableRangeLow);
        Assert.Equal(first.AcceptableRangeHigh, second.AcceptableRangeHigh);
        Assert.Equal(first.WalkAwayThreshold, second.WalkAwayThreshold);
        Assert.Equal(first.Explanation, second.Explanation);
        // Asserted field-by-field, not via NegotiationLever's own (record-generated) Equals:
        // Evidence is declared as IReadOnlyList<NegotiationLeverEvidence>, an interface with no
        // structural equality of its own, so two independently-built NegotiationLever instances
        // that are otherwise identical would compare unequal if compared as a single sequence two
        // levels deep (Levers[i].Evidence's own two List<T> instances are always reference-distinct
        // across two separate Compute calls). Comparing each field's own sequence directly sidesteps
        // that gap: LeverType/Rationale are plain enums/strings, and NegotiationLeverEvidence's own
        // fields are all directly-equatable primitives, so a per-lever Evidence sequence compares
        // correctly on its own.
        Assert.Equal(first.Levers.Select(l => l.LeverType), second.Levers.Select(l => l.LeverType));
        Assert.Equal(first.Levers.Select(l => l.Rationale), second.Levers.Select(l => l.Rationale));
        Assert.Equal(first.Levers.Count, second.Levers.Count);
        for (var i = 0; i < first.Levers.Count; i++)
        {
            Assert.Equal(first.Levers[i].Evidence, second.Levers[i].Evidence);
        }
    }
}
