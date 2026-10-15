using Raffa.AiFlows.Ask.Orchestration;
using Raffa.Chat.Domain;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.AiFlows.Tests.Ask.Orchestration;

/// <summary>Pins the per-intent pure rules of <see cref="IntentPolicy"/> (F1 Ask split, PR-4).</summary>
public sealed class IntentPolicyTests
{
    private static PortfolioListItem Item() =>
        new(Guid.NewGuid(), Guid.NewGuid(), ContractDocumentType.Msa, 1000m, "EUR", null, null,
            null, null, false, "completed", null, null, null);

    private static PortfolioPage Page(params PortfolioListItem[] items) => new(items, 1, 25, items.Length);

    [Theory]
    [InlineData(AskIntent.Savings, true)]
    [InlineData(AskIntent.PortfolioSavingsTarget, true)]
    [InlineData(AskIntent.Clause, false)]
    [InlineData(AskIntent.MarketCompare, false)]
    public void Savings_intents_convene_the_council_with_or_without_a_named_contract(AskIntent intent, bool expected)
    {
        Assert.Equal(expected, IntentPolicy.IsCouncilIntent(intent, null));
        Assert.Equal(expected, IntentPolicy.IsCouncilIntent(intent, Item()));
    }

    [Fact]
    public void Renewal_strategy_convenes_the_council_only_for_a_named_contract()
    {
        Assert.False(IntentPolicy.IsCouncilIntent(AskIntent.RenewalStrategy, null));
        Assert.True(IntentPolicy.IsCouncilIntent(AskIntent.RenewalStrategy, Item()));
    }

    [Theory]
    [InlineData(AskIntent.DocumentStatus, true)]
    [InlineData(AskIntent.Clause, true)]
    [InlineData(AskIntent.StructuredFact, false)]
    [InlineData(AskIntent.Savings, false)]
    public void Only_document_status_and_clause_turns_skip_the_data_check(AskIntent intent, bool expected) =>
        Assert.Equal(expected, IntentPolicy.SkipsDataCheck(intent));

    [Fact]
    public void Market_research_needs_a_non_empty_portfolio_and_not_a_document_status_turn()
    {
        Assert.False(IntentPolicy.RunsMarketResearch(AskIntent.Savings, Page()));
        Assert.True(IntentPolicy.RunsMarketResearch(AskIntent.Savings, Page(Item())));
        Assert.False(IntentPolicy.RunsMarketResearch(AskIntent.DocumentStatus, Page(Item())));
    }

    [Fact]
    public void The_Q3_persist_turn_is_renewal_strategy_with_a_named_contract()
    {
        Assert.True(IntentPolicy.IsQ3PersistTurn(AskIntent.RenewalStrategy, Item()));
        Assert.False(IntentPolicy.IsQ3PersistTurn(AskIntent.RenewalStrategy, null));
        Assert.False(IntentPolicy.IsQ3PersistTurn(AskIntent.Savings, Item()));
    }

    [Theory]
    [InlineData(AskIntent.Savings, true)]
    [InlineData(AskIntent.PortfolioSavingsTarget, true)]
    [InlineData(AskIntent.RenewalStrategy, false)]
    [InlineData(AskIntent.PortfolioStrategy, false)]
    public void Savings_actions_are_injected_only_on_savings_intents(AskIntent intent, bool expected) =>
        Assert.Equal(expected, IntentPolicy.InjectsSavingsActions(intent));
}
