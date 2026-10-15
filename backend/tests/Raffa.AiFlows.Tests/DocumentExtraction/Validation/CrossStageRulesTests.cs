using Raffa.AiFlows.DocumentExtraction.Validation;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.AiFlows.Tests.DocumentExtraction.Validation;

/// <summary>F5-D19: the three cross-stage rules, pure: ok, violation, not applicable and the edges.</summary>
public sealed class CrossStageRulesTests
{
    private static ScalarFactSet Facts(params (string Field, string? Value)[] values) => ScalarFactSet.Of(values);

    // ---- date order ----

    [Fact]
    public void Date_order_passes_when_start_is_before_end_and_effective_is_before_start()
    {
        var result = new DateOrderRule().Evaluate(Facts(
            ("effectiveDate", "2025-12-15"), ("startDate", "2026-01-01"), ("endDate", "2026-12-31")));

        Assert.Equal(RuleStatus.Passed, result.Status);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Date_order_flags_an_end_before_the_start_with_a_stable_id_and_both_fields()
    {
        var result = new DateOrderRule().Evaluate(Facts(("startDate", "2026-06-01"), ("endDate", "2026-01-01")));

        Assert.Equal(RuleStatus.Violated, result.Status);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("date-order.end-before-start", violation.RuleId);
        Assert.Equal(["startDate", "endDate"], violation.Fields);
    }

    [Fact]
    public void Date_order_flags_an_effective_date_after_the_start()
    {
        var result = new DateOrderRule().Evaluate(Facts(("effectiveDate", "2026-02-01"), ("startDate", "2026-01-01")));

        var violation = Assert.Single(result.Violations);
        Assert.Equal("date-order.effective-after-start", violation.RuleId);
        Assert.Equal(["effectiveDate", "startDate"], violation.Fields);
    }

    [Fact]
    public void Date_order_accepts_equal_dates()
    {
        var result = new DateOrderRule().Evaluate(Facts(
            ("effectiveDate", "2026-01-01"), ("startDate", "2026-01-01"), ("endDate", "2026-01-01")));

        Assert.Equal(RuleStatus.Passed, result.Status);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("2026-01-01", null)]
    [InlineData("not a date", "2026-12-31")]
    public void Date_order_is_not_applicable_when_a_date_is_missing_or_unreadable(string? start, string? end)
    {
        var result = new DateOrderRule().Evaluate(Facts(("startDate", start), ("endDate", end)));

        Assert.Equal(RuleStatus.NotApplicable, result.Status);
        Assert.Empty(result.Violations);
    }

    // ---- deadline consistency ----

    [Fact]
    public void Deadline_passes_when_it_is_the_end_date_minus_the_notice_period()
    {
        var result = new DeadlineConsistencyRule().Evaluate(Facts(
            ("endDate", "2026-12-31"), ("cancellationDeadline", "2026-10-02"), ("noticePeriodDays", "90")));

        Assert.Equal(RuleStatus.Passed, result.Status);
    }

    [Fact]
    public void Deadline_passes_without_a_notice_period_when_it_is_not_after_the_end_and_accepts_equality()
    {
        Assert.Equal(
            RuleStatus.Passed,
            new DeadlineConsistencyRule().Evaluate(Facts(("endDate", "2026-12-31"), ("cancellationDeadline", "2026-12-31"))).Status);
        Assert.Equal(
            RuleStatus.Passed,
            new DeadlineConsistencyRule().Evaluate(Facts(("endDate", "2026-12-31"), ("cancellationDeadline", "2026-06-30"))).Status);
    }

    [Fact]
    public void Deadline_flags_a_cancellation_deadline_after_the_end()
    {
        var result = new DeadlineConsistencyRule().Evaluate(Facts(
            ("endDate", "2026-12-31"), ("cancellationDeadline", "2027-01-15"), ("noticePeriodDays", "90")));

        var violation = Assert.Single(result.Violations);
        Assert.Equal("deadline-consistency.cancellation-after-end", violation.RuleId);
        Assert.Equal(["cancellationDeadline", "endDate"], violation.Fields);
    }

    [Fact]
    public void Deadline_flags_a_notice_period_that_does_not_match_the_dates()
    {
        var result = new DeadlineConsistencyRule().Evaluate(Facts(
            ("endDate", "2026-12-31"), ("cancellationDeadline", "2026-10-02"), ("noticePeriodDays", "60")));

        var violation = Assert.Single(result.Violations);
        Assert.Equal("deadline-consistency.notice-period-mismatch", violation.RuleId);
        Assert.Equal(["cancellationDeadline", "endDate", "noticePeriodDays"], violation.Fields);
    }

    [Fact]
    public void Deadline_with_a_zero_day_notice_must_equal_the_end_date()
    {
        Assert.Equal(
            RuleStatus.Passed,
            new DeadlineConsistencyRule().Evaluate(Facts(
                ("endDate", "2026-12-31"), ("cancellationDeadline", "2026-12-31"), ("noticePeriodDays", "0"))).Status);
        Assert.Equal(
            RuleStatus.Violated,
            new DeadlineConsistencyRule().Evaluate(Facts(
                ("endDate", "2026-12-31"), ("cancellationDeadline", "2026-12-01"), ("noticePeriodDays", "0"))).Status);
    }

    [Theory]
    [InlineData("2026-12-31", null)]
    [InlineData(null, "2026-10-02")]
    [InlineData("garbage", "2026-10-02")]
    public void Deadline_is_not_applicable_without_both_dates(string? end, string? deadline)
    {
        var result = new DeadlineConsistencyRule().Evaluate(Facts(
            ("endDate", end), ("cancellationDeadline", deadline), ("noticePeriodDays", "90")));

        Assert.Equal(RuleStatus.NotApplicable, result.Status);
    }

    [Fact]
    public void Deadline_ignores_an_unreadable_or_negative_notice_period()
    {
        Assert.Equal(
            RuleStatus.Passed,
            new DeadlineConsistencyRule().Evaluate(Facts(
                ("endDate", "2026-12-31"), ("cancellationDeadline", "2026-10-02"), ("noticePeriodDays", "ninety"))).Status);
        Assert.Equal(
            RuleStatus.Passed,
            new DeadlineConsistencyRule().Evaluate(Facts(
                ("endDate", "2026-12-31"), ("cancellationDeadline", "2026-10-02"), ("noticePeriodDays", "-5"))).Status);
    }

    // ---- TCV vs annual spend ----

    private static (string, string?)[] Term(params (string, string?)[] amounts) =>
        [("startDate", "2026-01-01"), ("endDate", "2028-12-31"), .. amounts];

    [Fact]
    public void Tcv_passes_when_it_covers_the_annual_spend_and_accepts_equality()
    {
        Assert.Equal(
            RuleStatus.Passed,
            new TcvVersusAnnualSpendRule().Evaluate(Facts(Term(("annualSpend", "100000"), ("totalContractValue", "300000")))).Status);
        Assert.Equal(
            RuleStatus.Passed,
            new TcvVersusAnnualSpendRule().Evaluate(Facts(Term(("annualSpend", "100000"), ("totalContractValue", "100000")))).Status);
    }

    [Fact]
    public void Tcv_flags_a_total_below_the_annual_spend_over_a_multi_year_term()
    {
        var result = new TcvVersusAnnualSpendRule().Evaluate(Facts(Term(("annualSpend", "100000"), ("totalContractValue", "50000"))));

        var violation = Assert.Single(result.Violations);
        Assert.Equal("tcv-vs-annual-spend.tcv-below-annual-spend", violation.RuleId);
        Assert.Contains("totalContractValue", violation.Fields);
        Assert.Contains("annualSpend", violation.Fields);
    }

    [Fact]
    public void Tcv_reads_amounts_in_either_locale()
    {
        // Italian "1.200.000,50" and English "1,200,000.50" are the same number; annual 400.000 is below it.
        Assert.Equal(
            RuleStatus.Passed,
            new TcvVersusAnnualSpendRule().Evaluate(Facts(Term(("annualSpend", "400.000,00"), ("totalContractValue", "1.200.000,50")))).Status);
        Assert.Equal(
            RuleStatus.Violated,
            new TcvVersusAnnualSpendRule().Evaluate(Facts(Term(("annualSpend", "1,200,000.50"), ("totalContractValue", "400,000.00")))).Status);
    }

    [Fact]
    public void Tcv_is_not_applicable_for_a_term_shorter_than_a_year()
    {
        var result = new TcvVersusAnnualSpendRule().Evaluate(Facts(
            ("startDate", "2026-01-01"), ("endDate", "2026-06-30"), ("annualSpend", "100000"), ("totalContractValue", "50000")));

        Assert.Equal(RuleStatus.NotApplicable, result.Status);
    }

    [Fact]
    public void Tcv_treats_an_inclusive_calendar_year_as_a_year()
    {
        var result = new TcvVersusAnnualSpendRule().Evaluate(Facts(
            ("startDate", "2026-01-01"), ("endDate", "2026-12-31"), ("annualSpend", "100000"), ("totalContractValue", "50000")));

        Assert.Equal(RuleStatus.Violated, result.Status);
    }

    [Fact]
    public void Tcv_is_not_applicable_when_the_two_amounts_are_in_different_currencies()
    {
        Assert.Equal(
            RuleStatus.NotApplicable,
            new TcvVersusAnnualSpendRule().Evaluate(Facts(Term(("annualSpend", "EUR 100000"), ("totalContractValue", "USD 50000")))).Status);
        // One amount names a currency other than the contract's: not comparable either.
        Assert.Equal(
            RuleStatus.NotApplicable,
            new TcvVersusAnnualSpendRule().Evaluate(Facts(Term(
                ("currency", "EUR"), ("annualSpend", "100000"), ("totalContractValue", "$50000")))).Status);
    }

    [Fact]
    public void Tcv_compares_amounts_that_share_a_currency_however_it_is_written()
    {
        var result = new TcvVersusAnnualSpendRule().Evaluate(Facts(Term(
            ("currency", "EUR"), ("annualSpend", "€100.000"), ("totalContractValue", "50.000 EUR"))));

        Assert.Equal(RuleStatus.Violated, result.Status);
    }

    [Fact]
    public void Tcv_handles_zero_values()
    {
        Assert.Equal(
            RuleStatus.Passed,
            new TcvVersusAnnualSpendRule().Evaluate(Facts(Term(("annualSpend", "0"), ("totalContractValue", "0")))).Status);
        Assert.Equal(
            RuleStatus.Passed,
            new TcvVersusAnnualSpendRule().Evaluate(Facts(Term(("annualSpend", "0"), ("totalContractValue", "10")))).Status);
        Assert.Equal(
            RuleStatus.Violated,
            new TcvVersusAnnualSpendRule().Evaluate(Facts(Term(("annualSpend", "10"), ("totalContractValue", "0")))).Status);
    }

    [Theory]
    [InlineData(null, "300000", "2026-01-01", "2028-12-31")]
    [InlineData("100000", null, "2026-01-01", "2028-12-31")]
    [InlineData("100000", "300000", null, "2028-12-31")]
    [InlineData("100000", "300000", "2026-01-01", null)]
    [InlineData("n/a", "300000", "2026-01-01", "2028-12-31")]
    [InlineData("100000", "300000", "2028-12-31", "2026-01-01")]
    [InlineData("-5", "300000", "2026-01-01", "2028-12-31")]
    public void Tcv_is_not_applicable_when_an_input_is_missing_unreadable_or_incoherent(
        string? annual, string? total, string? start, string? end)
    {
        var result = new TcvVersusAnnualSpendRule().Evaluate(Facts(
            ("annualSpend", annual), ("totalContractValue", total), ("startDate", start), ("endDate", end)));

        Assert.Equal(RuleStatus.NotApplicable, result.Status);
    }

    // ---- fact set and runner ----

    [Fact]
    public void The_fact_set_only_reads_fields_of_the_stage_allow_list_and_the_last_value_wins()
    {
        var set = new ScalarFactSet(
        [
            new StageFacts.Scalars(ExtractionStage.DatesAndRenewalTerms,
            [
                new ExtractedFact("startDate", "2026-01-01", 1, null, 0.9),
                new ExtractedFact("annualSpend", "5", 1, null, 0.9),
                new ExtractedFact("startDate", "2026-02-01", 1, null, 0.9),
                new ExtractedFact("endDate", "  ", 1, null, 0.9),
            ]),
        ]);

        Assert.Equal("2026-02-01", set.Value("startDate"));
        Assert.Null(set.Value("annualSpend"));
        Assert.Null(set.Value("endDate"));
    }

    [Fact]
    public void The_validator_is_deterministic_and_a_throwing_rule_is_not_applicable()
    {
        var stages = new[]
        {
            new StageFacts.Scalars(ExtractionStage.DatesAndRenewalTerms,
            [
                new ExtractedFact("startDate", "2026-06-01", 1, null, 0.9),
                new ExtractedFact("endDate", "2026-01-01", 1, null, 0.9),
            ]),
        };

        var first = CrossStageValidator.Evaluate(stages);
        var second = CrossStageValidator.Evaluate(stages);
        Assert.Equal(first.Violations.Select(v => v.RuleId), second.Violations.Select(v => v.RuleId));
        Assert.Equal(["date-order.end-before-start"], first.Violations.Select(v => v.RuleId));
        Assert.Equal(
            [RuleStatus.Violated, RuleStatus.NotApplicable, RuleStatus.NotApplicable],
            first.Rules.Select(r => r.Evaluation.Status));

        var faulty = CrossStageValidator.Evaluate(stages, [new ThrowingRule()]);
        Assert.Equal(RuleStatus.NotApplicable, Assert.Single(faulty.Rules).Evaluation.Status);
    }

    [Fact]
    public void Gate_flags_only_the_involved_facts_with_the_rule_id_and_never_edits_a_value()
    {
        var stages = new[]
        {
            new StageFacts.Scalars(ExtractionStage.Metadata, [new ExtractedFact("currency", "EUR", 1, "EUR", 0.9)]),
            new StageFacts.Scalars(ExtractionStage.DatesAndRenewalTerms,
            [
                new ExtractedFact("startDate", "2026-06-01", 1, "s", 0.9),
                new ExtractedFact("endDate", "2026-01-01", 1, "e", 0.9),
                new ExtractedFact("autoRenewal", "true", 1, "a", 0.9),
            ]),
        };

        var report = CrossStageValidator.Evaluate(stages);
        var flagged = CrossStageValidator.ApplyGate(stages, report);

        Assert.Null(flagged[0].Facts[0].ReviewReason);
        var dates = flagged[1].Facts;
        Assert.Equal("cross-stage:date-order.end-before-start", dates[0].ReviewReason);
        Assert.Equal("cross-stage:date-order.end-before-start", dates[1].ReviewReason);
        Assert.Null(dates[2].ReviewReason);
        Assert.Equal(["2026-06-01", "2026-01-01", "true"], dates.Select(f => f.Value));
        Assert.Equal(["s", "e", "a"], dates.Select(f => f.SourceSpan));
    }

    private sealed class ThrowingRule : ICrossStageRule
    {
        public string RuleId => "throws";

        public RuleEvaluation Evaluate(ScalarFactSet facts) => throw new InvalidOperationException("boom");
    }
}
