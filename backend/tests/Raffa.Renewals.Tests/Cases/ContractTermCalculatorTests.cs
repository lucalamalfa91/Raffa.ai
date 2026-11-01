using Raffa.SharedKernel.Contracts;

namespace Raffa.Renewals.Tests.Cases;

/// <summary>The deterministic half of the situation assessment: the term roll-over arithmetic.</summary>
public sealed class ContractTermCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 1);

    private static ContractTermInputs Inputs(string end, int notice, bool auto, int? months, string? deadline = null, string start = "2024-08-07") =>
        new(DateOnly.Parse(start), DateOnly.Parse(end), notice, auto, months, deadline is null ? null : DateOnly.Parse(deadline));

    [Fact]
    public void A_notice_deadline_still_ahead_leaves_the_term_as_read()
    {
        var t = ContractTermCalculator.Compute(Inputs("2027-01-15", 90, true, 12, "2026-11-20"), Today);
        Assert.Equal(ContractTermStatus.Active, t.Status);
        Assert.False(t.RolledOver);
        Assert.Equal(new DateOnly(2026, 11, 20), t.NoticeDeadline);
        Assert.Equal(19, t.NoticeDays);
    }

    [Fact]
    public void An_auto_renewing_contract_whose_notice_passed_rolls_forward_by_its_renewal_term()
    {
        // Splunk: notice deadline 07 Jul 2026 (30 days before the end), renews for 12 months.
        var t = ContractTermCalculator.Compute(Inputs("2026-08-06", 30, true, 12, "2026-07-07"), Today);
        Assert.Equal(ContractTermStatus.RolledOver, t.Status);
        Assert.True(t.RolledOver);
        Assert.Equal(1, t.Cycles);
        Assert.Equal(new DateOnly(2026, 8, 6), t.RenewedOn);
        Assert.Equal(new DateOnly(2026, 7, 7), t.LockedOn);
        Assert.Equal(new DateOnly(2026, 8, 6), t.Start);
        Assert.Equal(new DateOnly(2027, 8, 6), t.End);
        Assert.Equal(new DateOnly(2027, 7, 7), t.NoticeDeadline);
        Assert.Equal(248, t.NoticeDays);
        Assert.True(t.NoticeOpen);
        Assert.Equal(new DateOnly(2026, 7, 7), t.OriginalNoticeDeadline);
        Assert.Equal(new DateOnly(2026, 8, 6), t.OriginalEnd);
    }

    [Fact]
    public void A_long_forgotten_contract_rolls_over_as_many_cycles_as_needed()
    {
        // Ended 31 Mar 2023, 90 days' notice, 12-month renewals: 2024, 2025, 2026 → now runs to 31 Mar 2027.
        var t = ContractTermCalculator.Compute(Inputs("2023-03-31", 90, true, 12, start: "2020-04-01"), Today);
        Assert.Equal(ContractTermStatus.RolledOver, t.Status);
        Assert.Equal(4, t.Cycles);
        Assert.Equal(new DateOnly(2026, 3, 31), t.RenewedOn);
        Assert.Equal(new DateOnly(2027, 3, 31), t.End);
        Assert.Equal(new DateOnly(2026, 12, 31), t.NoticeDeadline);
        Assert.Equal(new DateOnly(2025, 12, 31), t.LockedOn);
        Assert.Equal(60, t.NoticeDays);
    }

    [Fact]
    public void A_passed_notice_on_a_term_not_yet_ended_renews_on_the_old_end_date()
    {
        var t = ContractTermCalculator.Compute(Inputs("2027-01-15", 90, true, 36, "2026-10-18"), Today);
        Assert.Equal(ContractTermStatus.RolledOver, t.Status);
        Assert.Equal(new DateOnly(2027, 1, 15), t.RenewedOn);
        Assert.True(t.DaysSinceRenewal < 0);
        Assert.Equal(new DateOnly(2030, 1, 15), t.End);
        Assert.Equal(new DateOnly(2029, 10, 18), t.NoticeDeadline);
    }

    [Fact]
    public void Without_auto_renewal_a_passed_end_date_is_expired()
    {
        var t = ContractTermCalculator.Compute(Inputs("2026-08-31", 90, false, null), Today);
        Assert.Equal(ContractTermStatus.Expired, t.Status);
        Assert.False(t.RolledOver);
        Assert.False(t.NoticeOpen);
        Assert.Equal(new DateOnly(2026, 8, 31), t.End);
    }

    [Fact]
    public void Without_auto_renewal_a_passed_notice_before_the_end_is_ending()
    {
        var t = ContractTermCalculator.Compute(Inputs("2026-12-31", 90, false, null), Today);
        Assert.Equal(ContractTermStatus.Ending, t.Status);
        Assert.Equal(new DateOnly(2026, 12, 31), t.End);
        Assert.Equal(60, t.EndDays);
    }

    [Fact]
    public void An_unknown_renewal_term_cannot_be_rolled_and_needs_review()
    {
        var t = ContractTermCalculator.Compute(Inputs("2026-08-06", 30, true, null, "2026-07-07"), Today);
        Assert.Equal(ContractTermStatus.NeedsReview, t.Status);
        Assert.False(t.RolledOver);
        Assert.Equal(new DateOnly(2026, 7, 7), t.NoticeDeadline);
    }

    [Fact]
    public void No_dates_at_all_is_unknown()
    {
        var t = ContractTermCalculator.Compute(new ContractTermInputs(null, null, 30, true, 12, null), Today);
        Assert.Equal(ContractTermStatus.Unknown, t.Status);
    }

    [Theory]
    [InlineData("12 months, automatic", 12)]
    [InlineData("36 months", 36)]
    [InlineData("1 year", 12)]
    [InlineData("24 mesi", 24)]
    [InlineData("None — renegotiate", null)]
    public void The_renewal_term_is_read_from_the_key_terms(string text, int? expected)
    {
        var facts = new ContractFacts { Deep = new ContractDeepFacts { Terms = [new ContractTerm("Renewal term", text)] } };
        Assert.Equal(expected, ContractTermCalculator.RenewalMonths(facts));
        Assert.Equal(6, ContractTermCalculator.RenewalMonths(facts with { RenewalTermMonths = 6 }));
    }

    [Fact]
    public void The_rules_copy_cites_the_clause_or_says_it_cannot_and_never_writes_a_negative_count()
    {
        var facts = new ContractFacts
        {
            ContractId = Guid.NewGuid(),
            File = "Splunk_Order.pdf",
            EndDate = new DateOnly(2026, 8, 6),
            NoticeDeadline = new DateOnly(2026, 7, 7),
            Notice = 30,
            Auto = "Yes",
            RenewalTermMonths = 12,
        };
        var a = ContractSituationCopy.Rules(facts, ContractTermCalculator.Compute(facts, Today));
        Assert.Equal(SituationAction.PrepareNextNotice, a.RecommendedAction);
        Assert.Equal("Renewed by itself on 06 Aug 2026", a.Headline.En);
        Assert.Equal("Rinnovato da solo il 06 Ago 2026", a.Headline.It);
        Assert.Contains("I cannot cite the clause", a.Why.En, StringComparison.Ordinal);
        Assert.DoesNotContain("-", a.Why.En.Replace("—", "", StringComparison.Ordinal), StringComparison.Ordinal);

        var cited = facts with
        {
            Evidence = new Dictionary<string, ContractEvidence> { ["sp_renew"] = new("Splunk_Order.pdf", 4, "9.2", "", "renews automatically for 12 months", "") },
        };
        var b = ContractSituationCopy.Rules(cited, ContractTermCalculator.Compute(cited, Today));
        Assert.Equal("sp_renew", b.Source!.Key);
        Assert.Contains("Source: Splunk_Order.pdf, p. 4, § 9.2.", b.Why.En, StringComparison.Ordinal);
        Assert.Contains("Fonte: Splunk_Order.pdf, p. 4, § 9.2.", b.Why.It, StringComparison.Ordinal);
    }
}
