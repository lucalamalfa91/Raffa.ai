using Raffa.Chat.Application.Interview;
using Raffa.Chat.Application.WebResearch;
using Raffa.Chat.Domain;

namespace Raffa.Chat.Tests.WebResearch;

/// <summary>ADR-030: the consent is one question, two options, and only "allow" carries an
/// authorisation.</summary>
public sealed class WebConsentInterviewTests
{
    private static readonly WebResearchRequest Offer = new("typical uplift caps on saas renewals", WebResearchTopicLexicon.BenchmarkRange);

    [Fact]
    public void Allow_carries_the_offer_and_decline_carries_nothing()
    {
        var turn = WebConsentInterview.Build("search the web for typical uplift caps on saas renewals", Offer);

        var question = Assert.Single(turn.Questions);
        Assert.Equal(WebConsentInterview.QuestionKey, question.Key);
        Assert.Equal(InterviewPresentation.Consent, question.Presentation);
        Assert.False(question.AllowFreeText);
        Assert.Contains("typical uplift caps on saas renewals", question.Prompt, StringComparison.Ordinal);
        Assert.Contains("not verified", question.Prompt, StringComparison.Ordinal);

        var allow = question.Options.Single(o => o.Key == WebConsentInterview.AllowOptionKey);
        var decline = question.Options.Single(o => o.Key == WebConsentInterview.DeclineOptionKey);
        Assert.Equal(2, question.Options.Count);

        Assert.Equal(AskIntent.WebResearch, allow.ResolvesTo.Intent);
        Assert.Equal(Offer, allow.ResolvesTo.WebResearch);
        Assert.Equal(Offer, AskTurnHints.From(allow.ResolvesTo).AuthorizedWebResearch);

        Assert.Null(decline.ResolvesTo.Intent);
        Assert.Null(decline.ResolvesTo.WebResearch);
        Assert.Null(AskTurnHints.From(decline.ResolvesTo).AuthorizedWebResearch);
        // The declined turn is the same words without the "search the web" phrase.
        Assert.Equal("for typical uplift caps on saas renewals", decline.ResolvesTo.RewrittenQuestion);
    }

    [Fact]
    public void An_italian_question_gets_an_italian_consent()
    {
        var turn = WebConsentInterview.Build("cerca sul web le pratiche di mercato sui rinnovi saas", Offer);

        Assert.StartsWith("Raffa cercherà sul web pubblico", turn.Prompt, StringComparison.Ordinal);
        Assert.Equal("Sì, cerca sul web", turn.Questions[0].Options[0].Label);
        Assert.Equal("No, resta in Raffa", turn.Questions[0].Options[1].Label);
    }

    [Fact]
    public void The_declined_rewrite_keeps_the_original_when_nothing_usable_remains()
    {
        Assert.Equal("cerca sul web", WebConsentInterview.DeclinedRewrite("cerca sul web"));
        Assert.Equal("Did you over all my contract?", WebConsentInterview.DeclinedRewrite("Did you over all my contract?"));
    }
}
