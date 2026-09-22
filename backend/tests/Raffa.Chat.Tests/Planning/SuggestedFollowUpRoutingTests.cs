using Raffa.AiGateway.Contracts;
using Raffa.Chat.Application.Answering;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Planning;
using Raffa.Chat.Domain;
using Raffa.Chat.Tests.TestSupport;

namespace Raffa.Chat.Tests.Planning;

/// <summary>
/// A suggested follow-up is only a way forward if clicking it reaches a real answer. Proves every
/// chip <see cref="GroundedFallbackAnswer"/> offers — on an abstain and after a composed answer, in
/// English and Italian — plans to a specific intent rather than falling through to the
/// <see cref="AskIntent.StructuredFact"/> catch-all (which, for a question it cannot handle, answers
/// with an unrelated contract snapshot).
/// </summary>
public sealed class SuggestedFollowUpRoutingTests
{
    // Two chips that are real answers without a specific planner intent: the renewal-window
    // question is answered by the deterministic structured-fact handler itself, and "How do I
    // upload a contract?" is a capability question the domain gate answers before planning.
    private static readonly HashSet<string> AnsweredOutsideThePlanner =
    [
        "Which contracts renew in the next 120 days?",
        "How do I upload a contract?",
    ];

    private readonly IntentPlanner _planner = new();

    public static TheoryData<string, string?> AreasAndLanguages()
    {
        var data = new TheoryData<string, string?>();
        foreach (var question in new[] { "Where can I save the most this quarter?", "Dove possiamo risparmiare?" })
        {
            foreach (var key in new string?[]
                     {
                         CapabilityCatalog.SavingsKey, CapabilityCatalog.RenewalsKey, CapabilityCatalog.QuoteCheckKey,
                         CapabilityCatalog.DocumentsKey, CapabilityCatalog.AskKey, null,
                     })
            {
                data.Add(question, key);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AreasAndLanguages))]
    public void Every_abstain_chip_routes_to_a_real_answer(string question, string? capabilityKey)
    {
        var chips = GroundedFallbackAnswer.SuggestedQuestions(question, capabilityKey);

        Assert.NotEmpty(chips);
        foreach (var chip in chips.Where(chip => !AnsweredOutsideThePlanner.Contains(chip)))
        {
            Assert.NotEqual(AskIntent.StructuredFact, _planner.Plan(chip, namedSupplier: null).Intent);
        }
    }

    [Theory]
    [InlineData("Where can I save the most this quarter?")]
    [InlineData("Dove posso risparmiare di più questo trimestre?")]
    public void Every_chip_after_a_composed_savings_answer_routes_to_a_real_answer(string question)
    {
        var metadata = new AiCallMetadata("fixture", "v1", AnswerPromptV2.Version, DateTimeOffset.UnixEpoch, "00");
        var answer = GroundedFallbackAnswer.Compose(question, ScreenshotSavingsPack.Build(), metadata)!;

        Assert.Equal(2, answer.FollowUps!.Count);
        Assert.All(answer.FollowUps!, chip =>
            Assert.NotEqual(AskIntent.StructuredFact, _planner.Plan(chip, namedSupplier: null).Intent));
    }
}
