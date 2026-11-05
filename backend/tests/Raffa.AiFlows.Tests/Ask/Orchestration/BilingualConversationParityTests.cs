using Raffa.AiFlows.Ask.Artifacts;
using Raffa.AiFlows.Ask.Interview;
using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.Ask.Planning;
using Raffa.AiFlows.Shared.Planning;
using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Domain;

namespace Raffa.AiFlows.Tests.Ask.Orchestration;

public sealed class BilingualConversationParityTests
{
    private static IntentPlanResult Plan(AskIntent intent, string? supplier = null, SavingsGoal? goal = null) =>
        new(intent, "bilingual regression", supplier, goal);

    [Fact]
    public void English_multiturn_focus_goal_constraint_and_artifact_commands_match_the_italian_flow()
    {
        var state = ConversationStateReducer.Reduce(ConversationState.Empty, "Give me an overview of the Zscaler contract",
            Plan(AskIntent.StructuredFact, "Zscaler"), "Zscaler", null, null);
        state = ConversationStateReducer.Reduce(state, "And compared with Palo Alto?",
            Plan(AskIntent.MarketCompare, "Palo Alto"), "Palo Alto", null, null);
        Assert.Equal("Zscaler", state.PrimarySupplier);
        Assert.Equal(["Palo Alto"], state.ComparisonSuppliers);

        state = ConversationStateReducer.Reduce(state, "Leave Palo Alto out",
            Plan(AskIntent.MarketCompare, "Palo Alto"), "Palo Alto", null, null);
        Assert.Empty(state.ComparisonSuppliers!);

        state = ConversationStateReducer.Reduce(state, "I want CHF 15k in savings",
            Plan(AskIntent.Savings, "Zscaler", new SavingsGoal(15000, null, "CHF", null, null)), "Zscaler", null, null);
        state = ConversationStateReducer.Reduce(state, "Make it CHF 10k without mentioning the benchmark",
            Plan(AskIntent.Savings, "Zscaler", new SavingsGoal(10000, null, "CHF", null, null)), "Zscaler", null, null);
        Assert.Equal(10000, state.GoalAmount);
        Assert.Contains(state.Constraints!, x => x.Contains("without mentioning", StringComparison.OrdinalIgnoreCase));

        var history = new[] { ("you", "Prepare an action plan for the Zscaler renewal"), ("raffa", "I can prepare a persistent action plan.") };
        var create = ArtifactCommandResolver.Resolve("Create it", history, null, ["Zscaler", "Palo Alto"], state);
        Assert.Equal(ArtifactCommandKind.Create, create.Command);
        Assert.Equal("document", create.Kind);
        Assert.Equal("Zscaler", create.Supplier);

        var current = new CurrentArtifactHint("artifact-authorized", "document", "Zscaler action plan", "Zscaler", null, 1);
        var update = ArtifactCommandResolver.Resolve("Add the supplier email to it", history, current, ["Zscaler"], state);
        Assert.Equal(ArtifactCommandKind.ProposeUpdate, update.Command);
        var pending = state with { PendingArtifactInstruction = update.Instruction };
        Assert.Equal(ArtifactCommandKind.ConfirmUpdate, ArtifactCommandResolver.Resolve("Yes, go ahead", history, current, ["Zscaler"], pending).Command);
        Assert.Equal(ArtifactCommandKind.Open, ArtifactCommandResolver.Resolve("Open it", history, current, ["Zscaler"], pending).Command);
    }

    [Theory]
    [InlineData("Create it")]
    [InlineData("Open it")]
    [InlineData("This contract")]
    [InlineData("And the other one?")]
    public void English_ellipses_are_explicitly_recognized(string question) =>
        Assert.True(ConversationStateReducer.ShouldInheritPrimary(question));
}
