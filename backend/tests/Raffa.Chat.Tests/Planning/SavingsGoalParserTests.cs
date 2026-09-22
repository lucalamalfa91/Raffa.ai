using Raffa.Chat.Application.Planning;

namespace Raffa.Chat.Tests.Planning;

/// <summary>
/// Proves <see cref="SavingsGoalParser"/> reads the quantified goal out of a savings question
/// deterministically (amount shorthand, thousands separators, currency signs, percentages, time
/// windows — IT + EN) and never mistakes a year, a bare small count or a percentage for an amount.
/// </summary>
public sealed class SavingsGoalParserTests
{
    [Theory]
    [InlineData("quali leve posso usare per risparmiare 20 k sul rinnovo", 20000)]
    [InlineData("quali leve posso usare per risparmiare 20k sul rinnovo", 20000)]
    [InlineData("come posso salvare 40K sul prossimo quarterly", 40000)]
    [InlineData("voglio risparmiare 20.000 euro", 20000)]
    [InlineData("we need to save 1,250,000 next year", 1250000)]
    [InlineData("target saving 1.5M", 1500000)]
    [InlineData("risparmiare € 20k", 20000)]
    [InlineData("save EUR 35000 on this renewal", 35000)]
    public void Parses_amount_shorthand_and_separators(string question, decimal expected)
    {
        var goal = SavingsGoalParser.Parse(question);

        Assert.Equal(expected, goal.TargetAmount);
        Assert.True(goal.HasTarget);
    }

    [Theory]
    [InlineData("risparmiare € 20k", "EUR")]
    [InlineData("save $40k", "USD")]
    [InlineData("save CHF 12000", "CHF")]
    [InlineData("risparmiare 20k", null)]
    public void Reads_the_currency_when_named(string question, string? expected)
    {
        Assert.Equal(expected, SavingsGoalParser.Parse(question).Currency);
    }

    [Fact]
    public void Reads_a_percentage_target_and_never_treats_it_as_an_amount()
    {
        var goal = SavingsGoalParser.Parse("come taglio il 15% dei costi Salesforce?");

        Assert.Equal(15m, goal.TargetPercent);
        Assert.Null(goal.TargetAmount);
        Assert.True(goal.HasTarget);
    }

    [Theory]
    [InlineData("come posso salvare 40K sul prossimo quarterly", 90)]
    [InlineData("dove risparmio nel prossimo trimestre?", 90)]
    [InlineData("where can we save this quarter?", 90)]
    [InlineData("risparmiare 10k entro un mese", 30)]
    [InlineData("save 50k next year", 365)]
    [InlineData("risparmiare 5k entro 45 giorni", 45)]
    public void Reads_the_window(string question, int expectedDays)
    {
        Assert.Equal(expectedDays, SavingsGoalParser.Parse(question).WindowDays);
    }

    [Theory]
    [InlineData("where are the biggest savings?")]
    [InlineData("dove posso risparmiare sui 3 contratti attivi?")]
    [InlineData("quali contratti si rinnovano nel 2027?")]
    public void A_question_with_nothing_quantified_has_no_target(string question)
    {
        var goal = SavingsGoalParser.Parse(question);

        Assert.False(goal.HasTarget);
        Assert.Null(goal.TargetAmount);
        Assert.Null(goal.WindowDays);
    }
}
