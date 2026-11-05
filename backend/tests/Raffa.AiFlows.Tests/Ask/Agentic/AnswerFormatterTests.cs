using Raffa.AiFlows.Ask.Agentic;
using Raffa.AiFlows.Shared.Pack;

namespace Raffa.AiFlows.Tests.Ask.Agentic;

public sealed class AnswerFormatterTests
{
    [Theory]
    [InlineData("it", "Scade il 2026-04-15, costa CHF 281000 (sconto 25.4%).", "Scade il 15 aprile 2026, costa CHF 281.000 (sconto 25,4%).")]
    [InlineData("en", "Ends on 2026-04-15, costs CHF 281000 (discount 25.4%).", "Ends on 15 April 2026, costs CHF 281,000 (discount 25.4%).")]
    [InlineData("it", "Totale 843000 CHF e 1,250,000.75 EUR.", "Totale CHF 843.000 e EUR 1.250.000,75.")]
    [InlineData("en", "Totale 843.000 CHF e 281.000,50 EUR.", "Totale CHF 843,000 e EUR 281,000.50.")]
    [InlineData("it", "€ 1200 al mese", "EUR 1.200 al mese")]
    public void Dates_amounts_and_percentages_follow_the_interface_language(string language, string input, string expected) =>
        Assert.Equal(expected, AnswerFormatter.Format(input, language));

    [Theory]
    [InlineData("it", "Nel 2026 il contratto [1] ha 3 clausole e il codice è 12345.")]
    [InlineData("en", "Net 30 days, 99% uptime, version 2.6, see [2].")]
    public void Bare_numbers_years_markers_and_versions_are_left_alone(string language, string text) =>
        Assert.Equal(text, AnswerFormatter.Format(text, language));

    [Fact]
    public void Formatting_is_idempotent()
    {
        var once = AnswerFormatter.Format("Scade il 2026-04-15, costa CHF 281000 (25.4%).", "it");
        Assert.Equal(once, AnswerFormatter.Format(once, "it"));
    }

    [Theory]
    [InlineData("it", "CHF 281.000")]
    [InlineData("en", "CHF 281,000")]
    public void A_pack_amount_displays_with_its_currency_in_the_language(string language, string expected) =>
        Assert.Equal(expected, AnswerFormatter.Display(new PackValue("annualSpend", "281000", PackValueKind.Amount, "CHF"), language));

    [Theory]
    [InlineData("it", "15 aprile 2026")]
    [InlineData("en", "15 April 2026")]
    public void A_pack_date_displays_as_a_long_date(string language, string expected) =>
        Assert.Equal(expected, AnswerFormatter.Display(new PackValue("endDate", "2026-04-15", PackValueKind.Date), language));

    [Fact]
    public void A_pack_percentage_displays_with_the_locale_decimal_separator() =>
        Assert.Equal("25,4%", AnswerFormatter.Display(new PackValue("discount", "25.4", PackValueKind.Percentage), "it"));
}
