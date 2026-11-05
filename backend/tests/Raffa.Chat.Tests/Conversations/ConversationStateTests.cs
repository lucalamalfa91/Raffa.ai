using Raffa.Chat.Application.Conversations;

namespace Raffa.Chat.Tests.Conversations;

public sealed class ConversationStateTests
{
    [Fact]
    public void State_round_trips_without_losing_artifact_or_missing_inputs()
    {
        var state = new ConversationState(
            ActiveContractId: Guid.NewGuid().ToString(), Goal: "reduce renewal cost",
            CurrentArtifactId: "artifact-1", PendingAction: "OPEN_ACTION_PANEL",
            MissingInputs: ["usage"], LastSuccessfulTool: "artifact.compose",
            PrimarySupplier: "Zscaler", ComparisonSuppliers: ["Palo Alto"], LastIntent: "Savings",
            GoalAmount: 10000, GoalCurrency: "CHF", Constraints: ["non menzionare il benchmark"]);

        var restored = ConversationState.Parse(state.Serialize());
        Assert.Equal(state.ActiveContractId, restored.ActiveContractId);
        Assert.Equal(state.Goal, restored.Goal);
        Assert.Equal(state.CurrentArtifactId, restored.CurrentArtifactId);
        Assert.Equal(state.PendingAction, restored.PendingAction);
        Assert.Equal(state.MissingInputs, restored.MissingInputs);
        Assert.Equal(state.LastSuccessfulTool, restored.LastSuccessfulTool);
        Assert.Equal(state.PrimarySupplier, restored.PrimarySupplier);
        Assert.Equal(state.ComparisonSuppliers, restored.ComparisonSuppliers);
        Assert.Equal(state.LastIntent, restored.LastIntent);
        Assert.Equal(state.GoalAmount, restored.GoalAmount);
        Assert.Equal(state.Constraints, restored.Constraints);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-json")]
    public void Missing_or_legacy_state_degrades_to_empty(string? value) =>
        Assert.Equal(ConversationState.Empty, ConversationState.Parse(value));
}
