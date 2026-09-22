using Raffa.Chat.Application.WebResearch;

namespace Raffa.Chat.Tests.WebResearch;

public sealed class WebResearchTopicLexiconTests
{
    [Theory]
    [InlineData("cerca sul web le pratiche di rinnovo SaaS")]
    [InlineData("Cercami su internet le novità su Salesforce")]
    [InlineData("search the web for typical uplift caps on SaaS renewals")]
    [InlineData("can you look it up online?")]
    [InlineData("check online what buyers usually get on notice periods")]
    [InlineData("google it: Salesforce price increase 2026")]
    public void Recognises_an_explicit_web_request_in_both_languages(string question)
    {
        Assert.True(WebResearchTopicLexicon.IsExplicitRequest(question));
    }

    [Theory]
    [InlineData("Which contracts renew in the next 120 days?")]
    [InlineData("Quali leve per risparmiare 20k sul rinnovo Salesforce?")]
    [InlineData("Is my Salesforce contract in line with market?")]
    [InlineData("Did you over all my contract?")]
    public void A_normal_ask_question_is_never_an_explicit_web_request(string question)
    {
        Assert.False(WebResearchTopicLexicon.IsExplicitRequest(question));
    }

    [Theory]
    [InlineData("search the web for news about Salesforce acquisitions", WebResearchTopicLexicon.SupplierNews)]
    [InlineData("cerca sul web le novità sul listino Microsoft", WebResearchTopicLexicon.SupplierNews)]
    [InlineData("what is the typical uplift on SaaS renewals?", WebResearchTopicLexicon.BenchmarkRange)]
    [InlineData("qual è l'aumento dei prezzi tipico per le licenze cloud?", WebResearchTopicLexicon.BenchmarkRange)]
    [InlineData("which negotiation levers work on a cloud renewal?", WebResearchTopicLexicon.NegotiationTactics)]
    [InlineData("quali tattiche di negoziazione usare con un fornitore SaaS?", WebResearchTopicLexicon.NegotiationTactics)]
    [InlineData("what do buyers usually get on SaaS renewals?", WebResearchTopicLexicon.MarketPractice)]
    [InlineData("pratiche di mercato sui contratti di manutenzione", WebResearchTopicLexicon.MarketPractice)]
    public void Classifies_a_procurement_topic_into_one_of_the_four_purposes(string question, string purpose)
    {
        Assert.Equal(purpose, WebResearchTopicLexicon.Classify(question));
    }

    [Theory]
    [InlineData("search the web for the best pizza in Naples")]
    [InlineData("cerca sul web il meteo di domani")]
    [InlineData("look it up online: who won the match yesterday")]
    public void An_off_topic_question_has_no_purpose_so_no_offer_is_ever_made(string question)
    {
        Assert.Null(WebResearchTopicLexicon.Classify(question));
    }

    [Fact]
    public void The_four_purposes_are_the_only_ones()
    {
        Assert.Equal(
            ["MarketPractice", "SupplierNews", "BenchmarkRange", "NegotiationTactics"],
            WebResearchTopicLexicon.Purposes);
    }
}
