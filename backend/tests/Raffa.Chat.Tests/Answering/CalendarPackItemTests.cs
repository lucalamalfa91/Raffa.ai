using Raffa.Chat.Application.Guards;
using Raffa.Chat.Application.Pack;

namespace Raffa.Chat.Tests.Answering;

/// <summary>
/// The calendar item that lets a period question ("Qual è la data esatta di fine quarter?") be
/// answered from the pack: exact quarter dates as pack values, so NumericGuard accepts the model
/// stating them and still rejects any other date.
/// </summary>
public sealed class CalendarPackItemTests
{
    [Theory]
    [InlineData("Where can I save the most this quarter?")]
    [InlineData("Qual è la data esatta di fine quarter che intendi?")]
    [InlineData("come posso salvare 40K sul prossimo quarterly?")]
    [InlineData("Cosa scade entro fine trimestre?")]
    [InlineData("What renews in Q4?")]
    [InlineData("Cosa devo fare entro fine anno?")]
    [InlineData("Quanto spendiamo quest'anno?")]
    public void A_question_about_a_period_gets_the_calendar(string question)
    {
        Assert.True(CalendarPackItem.IsPeriodQuestion(question));
    }

    [Theory]
    [InlineData("mi aiuti a creare una mail che posso inviare per il rinnovo?")]
    [InlineData("What liability do we have with AWS?")]
    [InlineData("how much did we pay in legal fees last year?")]
    public void Any_other_question_does_not(string question)
    {
        Assert.False(CalendarPackItem.IsPeriodQuestion(question));
    }

    [Fact]
    public void The_item_carries_the_current_and_next_quarter_as_exact_dates()
    {
        var item = CalendarPackItem.Build(new DateOnly(2026, 9, 22));

        Assert.Equal(CalendarPackItem.CitationKey, item.CitationKey);
        Assert.Equal(PackCorpus.Calc, item.Corpus);
        Assert.Contains("Q3 2026, from 2026-07-01 to 2026-09-30 (8 days left)", item.Snippet, StringComparison.Ordinal);
        Assert.Contains("Q4 2026, from 2026-10-01 to 2026-12-31", item.Snippet, StringComparison.Ordinal);

        var values = item.Values.ToDictionary(v => v.Key, v => v.Value);
        Assert.Equal("2026-09-22", values["today"]);
        Assert.Equal("2026-07-01", values["quarterStart"]);
        Assert.Equal("2026-09-30", values["quarterEnd"]);
        Assert.Equal("8", values["daysLeftInQuarter"]);
        Assert.Equal("2026-12-31", values["nextQuarterEnd"]);
        Assert.Equal("2026-12-31", values["yearEnd"]);
    }

    [Fact]
    public void The_fourth_quarter_rolls_over_into_next_year()
    {
        var item = CalendarPackItem.Build(new DateOnly(2026, 12, 31));

        Assert.Contains("Q4 2026, from 2026-10-01 to 2026-12-31 (0 days left)", item.Snippet, StringComparison.Ordinal);
        Assert.Contains("Q1 2027, from 2027-01-01 to 2027-03-31", item.Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void The_quarter_end_is_grounded_for_the_numeric_guard_in_either_date_form_and_nothing_else_is()
    {
        var pack = new[] { CalendarPackItem.Build(new DateOnly(2026, 9, 22)) };

        Assert.True(NumericGuard.Validate("Il trimestre in corso termina il 30 settembre 2026 [1].", pack).Passed);
        Assert.True(NumericGuard.Validate("The quarter ends on 2026-09-30 [1].", pack).Passed);
        Assert.False(NumericGuard.Validate("The quarter ends on 2026-10-15 [1].", pack).Passed);
    }
}
