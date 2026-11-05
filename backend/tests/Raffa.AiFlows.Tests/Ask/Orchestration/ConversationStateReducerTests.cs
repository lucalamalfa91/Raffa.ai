using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.Ask.Planning;
using Raffa.AiFlows.Shared.Planning;
using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Domain;

namespace Raffa.AiFlows.Tests.Ask.Orchestration;

public sealed class ConversationStateReducerTests
{
    private static IntentPlanResult Plan(AskIntent intent, SavingsGoal? goal = null, string? supplier = null) =>
        new(intent, "test", supplier, goal);

    [Fact]
    public void Comparison_does_not_erase_primary_supplier_and_dismissal_returns_to_it()
    {
        var initial = ConversationState.Empty with { PrimarySupplier = "Zscaler" };
        var compared = ConversationStateReducer.Reduce(initial, "E rispetto a Palo Alto?",
            Plan(AskIntent.MarketCompare, supplier: "Palo Alto"), "Palo Alto", null, null);

        Assert.Equal("Zscaler", compared.PrimarySupplier);
        Assert.Equal(["Palo Alto"], compared.ComparisonSuppliers);

        var dismissed = ConversationStateReducer.Reduce(compared, "Lascia stare Palo Alto",
            Plan(AskIntent.MarketCompare, supplier: "Palo Alto"), "Palo Alto", null, null);
        Assert.Equal("Zscaler", dismissed.PrimarySupplier);
        Assert.Empty(dismissed.ComparisonSuppliers!);
    }

    [Fact]
    public void Revised_saving_goal_replaces_the_current_amount_and_keeps_constraints()
    {
        var initial = ConversationState.Empty with { PrimarySupplier = "Zscaler", GoalAmount = 15000, GoalCurrency = "CHF" };
        var revised = ConversationStateReducer.Reduce(initial, "Facciamo CHF 10k, senza menzionare il benchmark",
            Plan(AskIntent.Savings, new SavingsGoal(10000, null, "CHF", null, null), "Zscaler"),
            "Zscaler", null, null);

        Assert.Equal(10000, revised.GoalAmount);
        Assert.Equal("CHF", revised.GoalCurrency);
        Assert.Contains(revised.Constraints!, x => x.Contains("senza menzionare", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("È caro?")]
    [InlineData("E quello?")]
    [InlineData("Come?")]
    [InlineData("This contract?")]
    [InlineData("And the other one?")]
    [InlineData("Create it")]
    [InlineData("Open it")]
    public void Explicit_ellipsis_can_inherit_primary(string question) =>
        Assert.True(ConversationStateReducer.ShouldInheritPrimary(question));

    [Theory]
    [InlineData("the other one")]
    [InlineData("And the other contract?")]
    [InlineData("l'altro")]
    public void Other_reference_selects_the_previous_server_owned_focus(string question) =>
        Assert.True(ConversationStateReducer.RefersToPreviousPrimary(question));

    [Fact]
    public void Portfolio_question_does_not_inherit_primary() =>
        Assert.False(ConversationStateReducer.ShouldInheritPrimary("Quali contratti scadono prima?"));
}
