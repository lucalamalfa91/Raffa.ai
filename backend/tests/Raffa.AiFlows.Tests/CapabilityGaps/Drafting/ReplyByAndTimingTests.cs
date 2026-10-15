using Raffa.AiFlows.CapabilityGaps.Drafting;
using Raffa.AiFlows.CapabilityGaps.Guards;
using Raffa.AiFlows.Shared.Guards;
using Raffa.AiFlows.Shared.Pack;

namespace Raffa.AiFlows.Tests.CapabilityGaps.Drafting;

/// <summary>F4-D14 (the reply-by calculator) and F4-D09 (timing to the type of communication).</summary>
public sealed class ReplyByAndTimingTests
{
    // ---------------------------------------------------------------- Easter and the holidays

    [Theory]
    [InlineData(2024, 3, 31)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    public void Easter_Sunday_is_the_gregorian_one(int year, int month, int day) =>
        Assert.Equal(new DateOnly(year, month, day), ReplyByCalculator.EasterSunday(year));

    [Fact]
    public void Each_jurisdiction_has_its_national_holidays_and_an_unknown_one_has_none()
    {
        var it = ReplyByCalculator.Holidays(2026, "IT");
        var fr = ReplyByCalculator.Holidays(2026, "fr-FR");
        var es = ReplyByCalculator.Holidays(2026, "ES");
        var de = ReplyByCalculator.Holidays(2026, "de");

        Assert.Contains(new DateOnly(2026, 4, 6), it);   // Easter Monday
        Assert.Contains(new DateOnly(2026, 6, 2), it);   // Festa della Repubblica
        Assert.DoesNotContain(new DateOnly(2026, 4, 3), it); // Good Friday is not a holiday in Italy
        Assert.Contains(new DateOnly(2026, 5, 14), fr);  // Ascension
        Assert.Contains(new DateOnly(2026, 5, 25), fr);  // Whit Monday
        Assert.Contains(new DateOnly(2026, 7, 14), fr);
        Assert.Contains(new DateOnly(2026, 4, 3), es);   // Good Friday
        Assert.Contains(new DateOnly(2026, 10, 12), es);
        Assert.Contains(new DateOnly(2026, 4, 3), de);
        Assert.Contains(new DateOnly(2026, 10, 3), de);
        Assert.Empty(ReplyByCalculator.Holidays(2026, "GB"));
        Assert.Empty(ReplyByCalculator.Holidays(2026, null));
    }

    // ---------------------------------------------------------------- Calculate

    [Fact]
    public void Business_days_skip_weekends_and_the_jurisdictions_holidays()
    {
        var sent = new DateOnly(2026, 4, 30); // Thursday

        Assert.Equal(new DateOnly(2026, 5, 6), ReplyByCalculator.Calculate(sent, 3, "IT"));   // 1 May is a holiday
        Assert.Equal(new DateOnly(2026, 5, 5), ReplyByCalculator.Calculate(sent, 3, null));   // weekends only
        Assert.Equal(new DateOnly(2026, 12, 29), ReplyByCalculator.Calculate(new DateOnly(2026, 12, 23), 3, "DE"));
        Assert.Equal(new DateOnly(2026, 12, 28), ReplyByCalculator.Calculate(new DateOnly(2026, 12, 23), 3, null));
    }

    [Fact]
    public void Zero_days_is_the_first_business_day_on_or_after_the_day_it_is_sent()
    {
        Assert.Equal(new DateOnly(2026, 5, 4), ReplyByCalculator.Calculate(new DateOnly(2026, 5, 1), 0, "IT"));   // holiday, then weekend
        Assert.Equal(new DateOnly(2026, 5, 1), ReplyByCalculator.Calculate(new DateOnly(2026, 5, 1), 0, null));
        Assert.Equal(new DateOnly(2026, 5, 6), ReplyByCalculator.Calculate(new DateOnly(2026, 5, 6), 0, "IT"));
    }

    [Fact]
    public void The_count_crosses_a_year_end_and_never_returns_a_non_business_day()
    {
        var start = new DateOnly(2026, 1, 1);
        for (var offset = 0; offset < 400; offset += 7)
        {
            foreach (var jurisdiction in new[] { "IT", "FR", "ES", "DE", null })
            {
                var result = ReplyByCalculator.Calculate(start.AddDays(offset), 5, jurisdiction);

                Assert.True(ReplyByCalculator.IsBusinessDay(result, jurisdiction), $"{jurisdiction} {start.AddDays(offset)} -> {result}");
                Assert.True(result > start.AddDays(offset));
            }
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(ReplyByCalculator.MaxBusinessDays + 1)]
    public void An_out_of_range_span_is_refused(int days) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ReplyByCalculator.Calculate(new DateOnly(2026, 5, 1), days, "IT"));

    // ---------------------------------------------------------------- the pack item

    [Fact]
    public void The_pack_item_grounds_a_draft_that_quotes_the_date_and_the_span()
    {
        var item = ReplyByCalculator.BuildItem(new DateOnly(2026, 4, 30), 3, "IT");

        Assert.Equal(ReplyByCalculator.CitationKey, item.CitationKey);
        Assert.Equal(PackCorpus.Calc, item.Corpus);
        Assert.Contains(item.Values, v => v is { Key: "replyBy", Value: "2026-05-06", Kind: PackValueKind.Date });
        Assert.Contains("IT national public holidays", item.Snippet, StringComparison.Ordinal);

        var quoted = NumericGuard.Validate("Please reply by 2026-05-06, within 3 business days.", [item]);
        Assert.True(quoted.Passed, quoted.Violation);
        Assert.False(NumericGuard.Validate("Please reply by 2026-05-07.", [item]).Passed);

        var elsewhere = ReplyByCalculator.BuildItem(new DateOnly(2026, 4, 30), 3, "GB");
        Assert.Contains("weekends excluded", elsewhere.Snippet, StringComparison.Ordinal);
        Assert.DoesNotContain("holidays", elsewhere.Snippet, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- timing

    private static readonly DateOnly Today = new(2026, 9, 1);

    [Theory]
    [InlineData(-1, DraftWindow.Passed, DraftType.Renegotiation)]
    [InlineData(0, DraftWindow.LastDay, DraftType.CancellationNonRenewal)]
    [InlineData(1, DraftWindow.Urgent, DraftType.CancellationNonRenewal)]
    [InlineData(DraftTimingAdvisor.UrgentDays, DraftWindow.Urgent, DraftType.CancellationNonRenewal)]
    [InlineData(DraftTimingAdvisor.UrgentDays + 1, DraftWindow.Open, DraftType.Renegotiation)]
    [InlineData(400, DraftWindow.Open, DraftType.Renegotiation)]
    public void The_days_to_the_notice_deadline_pick_the_window_and_the_suggested_type(int days, DraftWindow window, DraftType type)
    {
        var advice = DraftTimingAdvisor.Advise(Today, Today.AddDays(days), Today.AddDays(days + 90));

        Assert.Equal(window, advice.Window);
        Assert.Equal(days, advice.DaysToDeadline);
        Assert.Equal(type, advice.SuggestedType);
    }

    [Fact]
    public void Without_a_deadline_the_window_is_unknown_unless_the_contract_has_already_ended()
    {
        Assert.Equal(DraftWindow.Unknown, DraftTimingAdvisor.Advise(Today, null, null).Window);
        Assert.Equal(DraftWindow.Unknown, DraftTimingAdvisor.Advise(Today, null, Today.AddDays(10)).Window);
        Assert.Equal(DraftWindow.Passed, DraftTimingAdvisor.Advise(Today, null, Today.AddDays(-1)).Window);
        Assert.Null(DraftTimingAdvisor.Advise(Today, null, null).DaysToDeadline);
        Assert.Equal(DraftType.Renegotiation, DraftTimingAdvisor.Advise(Today, null, null).SuggestedType);
    }
}
