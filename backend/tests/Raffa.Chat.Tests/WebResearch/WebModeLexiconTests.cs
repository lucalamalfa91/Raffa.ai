using Raffa.Chat.Application.WebResearch;

namespace Raffa.Chat.Tests.WebResearch;

/// <summary>
/// ADR-031: with the web-search toggle on, the only question Raffa does not research is the plainly
/// personal or leisure one — and a leisure word next to a work word is a work question.
/// </summary>
public sealed class WebModeLexiconTests
{
    [Theory]
    [InlineData("dimmi la ricetta della carbonara")]
    [InlineData("Give me a recipe for lasagna")]
    [InlineData("chi ha vinto lo scudetto quest'anno?")]
    [InlineData("who won the match last night?")]
    [InlineData("che tempo fa domani a Milano?")]
    [InlineData("meteo weekend")]
    [InlineData("raccontami una barzelletta")]
    [InlineData("tell me a joke")]
    [InlineData("oroscopo di oggi per il leone")]
    [InlineData("film da vedere stasera")]
    [InlineData("cosa vedere a Lisbona in due giorni")]
    [InlineData("search the web for the best tiramisù")]
    public void A_plainly_personal_or_leisure_question_is_off_context(string question) =>
        Assert.True(WebModeLexicon.IsOffContext(question));

    [Theory]
    [InlineData("latest news on Salesforce price increases")]
    [InlineData("cerca sul web le nuove regole UE per i fornitori cloud")]
    [InlineData("What does the EU AI Act require from software vendors?")]
    [InlineData("inflazione prevista in Italia nel prossimo anno")]
    [InlineData("chi sono i principali concorrenti di Allianz nelle assicurazioni aziendali?")]
    [InlineData("partita IVA e sede legale di Zurich Italia")]
    public void A_work_question_is_never_off_context(string question) =>
        Assert.False(WebModeLexicon.IsOffContext(question));

    [Theory]
    [InlineData("catering supplier for the company canteen: recipes and prices")]
    [InlineData("quanto costa sponsorizzare una squadra di Champions League per un'azienda")]
    [InlineData("impatto del meteo sulla logistica dei fornitori")]
    public void A_leisure_word_next_to_a_work_word_stays_a_work_question(string question) =>
        Assert.False(WebModeLexicon.IsOffContext(question));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_question_is_not_off_context(string question) =>
        Assert.False(WebModeLexicon.IsOffContext(question));
}
