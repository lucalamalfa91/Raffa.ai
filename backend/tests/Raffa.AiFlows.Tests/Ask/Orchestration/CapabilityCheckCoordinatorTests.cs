using Raffa.AiFlows.Ask.Interview;
using Raffa.AiFlows.Ask.Orchestration;

namespace Raffa.AiFlows.Tests.Ask.Orchestration;

/// <summary>Pins the pure rules the capability-check coordinator took from <c>AskCopilotService</c>
/// (F1 Ask split, PR-6) and the INV-06 hand-off of the conversation's earlier user questions.</summary>
public sealed class CapabilityCheckCoordinatorTests
{
    [Fact]
    public void The_investigator_context_is_the_users_earlier_questions_oldest_first()
    {
        IReadOnlyList<(string Role, string Markdown)> turns =
        [
            ("you", "first question"),
            ("raffa", "an answer that must never become investigator context"),
            ("user", "second question"),
            ("Raffa", "another answer"),
        ];

        var questions = CapabilityCheckCoordinator.UserQuestions(turns);

        Assert.Equal(["first question", "second question"], questions);
    }

    [Fact]
    public void A_new_conversation_has_no_investigator_context() =>
        Assert.Empty(CapabilityCheckCoordinator.UserQuestions([]));

    [Fact]
    public void A_typed_turn_is_fresh_and_a_resolved_by_key_turn_is_not()
    {
        Assert.True(CapabilityCheckCoordinator.IsFreshTurn(AskTurnHints.None));
        Assert.False(CapabilityCheckCoordinator.IsFreshTurn(AskTurnHints.None with { ForcedSupplierName = "Acme" }));
        Assert.False(CapabilityCheckCoordinator.IsFreshTurn(AskTurnHints.None with { DeclinedWebResearch = true }));
        Assert.False(CapabilityCheckCoordinator.IsFreshTurn(AskTurnHints.None with { ForcedContractId = new Raffa.SharedKernel.EntityId(Guid.NewGuid()) }));
    }
}
