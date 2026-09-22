using Raffa.Chat.Application.WebResearch;

namespace Raffa.Chat.Tests.WebResearch;

/// <summary>ADR-030: the query that leaves Raffa carries the user's words and nothing that looks
/// like a tenant figure.</summary>
public sealed class WebQuerySanitizerTests
{
    [Fact]
    public void Drops_the_explicit_web_phrase_and_keeps_the_topic_and_the_supplier()
    {
        var query = WebQuerySanitizer.Sanitize("Cerca sul web le pratiche di mercato sui rinnovi Salesforce", 300);

        Assert.Equal("le pratiche di mercato sui rinnovi Salesforce", query);
    }

    [Theory]
    [InlineData("search the web: is a CHF 140,000 uplift of 7% normal for a 2027-01-15 renewal?", "CHF")]
    [InlineData("search the web: is a CHF 140,000 uplift of 7% normal for a 2027-01-15 renewal?", "140")]
    [InlineData("search the web: is a CHF 140,000 uplift of 7% normal for a 2027-01-15 renewal?", "7%")]
    [InlineData("search the web: is a CHF 140,000 uplift of 7% normal for a 2027-01-15 renewal?", "2027")]
    [InlineData("look it up online: we pay €12.500 and 40k on 15 January 2027 for licences", "12")]
    [InlineData("look it up online: we pay €12.500 and 40k on 15 January 2027 for licences", "40k")]
    [InlineData("look it up online: we pay €12.500 and 40k on 15 January 2027 for licences", "January")]
    [InlineData("search online for news about acme, contact john@acme.example or https://acme.example/x", "@")]
    [InlineData("search online for news about acme, contact john@acme.example or https://acme.example/x", "https")]
    [InlineData("search the web for contract 20240917 pricing practice", "20240917")]
    [InlineData("search the web for is 999 normal for licences", "999")]
    [InlineData("search the web for is 250 per seat normal", "250")]
    public void Strips_amounts_percentages_dates_shorthand_emails_urls_and_long_numbers(string question, string forbidden)
    {
        var query = WebQuerySanitizer.Sanitize(question, 300);

        Assert.NotNull(query);
        Assert.DoesNotContain(forbidden, query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Keeps_the_words_around_the_removed_figures()
    {
        var query = WebQuerySanitizer.Sanitize("search the web: is a CHF 140,000 uplift of 7% normal for a renewal?", 300);

        Assert.Equal("is a uplift of normal for a renewal", query);
    }

    [Fact]
    public void Caps_at_a_word_boundary()
    {
        var query = WebQuerySanitizer.Sanitize("search the web for saas renewal uplift market practice in europe", 30);

        Assert.NotNull(query);
        Assert.True(query!.Length <= 30);
        Assert.False(query.EndsWith(' '));
        Assert.Equal("for saas renewal uplift market", query);
    }

    [Theory]
    [InlineData("cerca sul web")]
    [InlineData("search the web for CHF 1,000")]
    [InlineData("   ")]
    public void Returns_null_when_fewer_than_two_words_survive(string question)
    {
        Assert.Null(WebQuerySanitizer.Sanitize(question, 300));
    }
}
