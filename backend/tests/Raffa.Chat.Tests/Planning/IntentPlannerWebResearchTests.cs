using Raffa.Chat.Application.Planning;
using Raffa.Chat.Domain;

namespace Raffa.Chat.Tests.Planning;

/// <summary>ADR-030: an explicit "search the web" request is its own intent, checked before every
/// other lexicon, and never appears for an ordinary question.</summary>
public sealed class IntentPlannerWebResearchTests
{
    private readonly IntentPlanner _planner = new();

    [Theory]
    [InlineData("cerca sul web le pratiche di mercato sui rinnovi SaaS")]
    [InlineData("search the web for typical uplift caps on Salesforce renewals")]
    [InlineData("can you look it up online? which negotiation levers work on cloud renewals")]
    public void An_explicit_web_request_wins_over_the_benchmark_and_strategy_lexicons(string question)
    {
        var result = _planner.Plan(question, question.Contains("Salesforce", StringComparison.Ordinal) ? "Salesforce" : null);

        Assert.Equal(AskIntent.WebResearch, result.Intent);
        Assert.Equal(IntentPlanBasis.Lexicon, result.Basis);
        Assert.Contains(AskIntent.WebResearch, result.Candidates!);
        Assert.Equal(AskIntent.WebResearch, result.Candidates![0]);
    }

    [Theory]
    [InlineData("Is my Salesforce contract in line with market?", "Salesforce", AskIntent.MarketCompare)]
    [InlineData("Which contracts renew in the next 120 days?", null, AskIntent.StructuredFact)]
    [InlineData("How should I approach the Salesforce renewal?", "Salesforce", AskIntent.RenewalStrategy)]
    [InlineData("Did you over all my contract?", null, AskIntent.Clause)]
    public void An_ordinary_question_never_becomes_web_research(string question, string? supplier, AskIntent expected)
    {
        var result = _planner.Plan(question, supplier);

        Assert.Equal(expected, result.Intent);
        Assert.DoesNotContain(AskIntent.WebResearch, result.Candidates!);
    }

    [Fact]
    public void A_forced_web_research_plan_is_marked_forced()
    {
        var result = _planner.Plan("Did you over all my contract?", null, null, AskIntent.WebResearch);

        Assert.Equal(AskIntent.WebResearch, result.Intent);
        Assert.Equal(IntentPlanBasis.Forced, result.Basis);
    }
}
