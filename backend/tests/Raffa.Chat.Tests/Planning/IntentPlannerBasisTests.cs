using Raffa.Chat.Application.Planning;
using Raffa.Chat.Domain;

namespace Raffa.Chat.Tests.Planning;

/// <summary>ADR-030: the structured "how was this decided" signal the interview reads.</summary>
public sealed class IntentPlannerBasisTests
{
    private readonly IntentPlanner _planner = new();

    [Fact]
    public void Nothing_matching_anywhere_is_the_fallback_with_no_candidates()
    {
        var result = _planner.Plan("Did you over all my contract?", null);

        Assert.Equal(AskIntent.Clause, result.Intent);
        Assert.Equal(IntentPlanBasis.Fallback, result.Basis);
        Assert.NotNull(result.Candidates);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void A_legacy_router_keyword_is_told_apart_from_the_fallback()
    {
        var structured = _planner.Plan("When does a contract expire?", null);
        var semantic = _planner.Plan("What liability do we have?", null);

        Assert.Equal(AskIntent.StructuredFact, structured.Intent);
        Assert.Equal(IntentPlanBasis.LegacyRouter, structured.Basis);
        Assert.Equal(AskIntent.Clause, semantic.Intent);
        Assert.Equal(IntentPlanBasis.LegacyRouter, semantic.Basis);
    }

    [Fact]
    public void A_lexicon_match_is_the_default_basis()
    {
        var result = _planner.Plan("Where can I save the most?", null);

        Assert.Equal(AskIntent.PortfolioStrategy, result.Intent);
        Assert.Equal(IntentPlanBasis.Lexicon, result.Basis);
        Assert.Equal([AskIntent.PortfolioStrategy], result.Candidates);
    }

    [Fact]
    public void A_bare_follow_up_is_marked_as_such()
    {
        var result = _planner.Plan("e quindi?", null, "Where can I save the most?");

        Assert.Equal(AskIntent.PortfolioStrategy, result.Intent);
        Assert.Equal(IntentPlanBasis.FollowUp, result.Basis);
    }

    [Fact]
    public void A_forced_intent_bypasses_every_lexicon()
    {
        var result = _planner.Plan("Did you over all my contract?", null, null, AskIntent.PortfolioStrategy);

        Assert.Equal(AskIntent.PortfolioStrategy, result.Intent);
        Assert.Equal(IntentPlanBasis.Forced, result.Basis);
        Assert.Equal([AskIntent.PortfolioStrategy], result.Candidates);
    }

    [Fact]
    public void Candidates_list_every_reading_in_planner_order()
    {
        var candidates = _planner.Candidates("Where can we save and how do we compare with the market?", null);

        Assert.Equal([AskIntent.PortfolioStrategy, AskIntent.QuoteRoute], candidates);
    }

    [Fact]
    public void Candidates_never_include_the_routers_own_default()
    {
        Assert.Empty(_planner.Candidates("Tell me everything", null));
    }
}
