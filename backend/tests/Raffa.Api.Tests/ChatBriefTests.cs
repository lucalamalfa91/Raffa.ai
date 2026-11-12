using Raffa.Api;
using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Domain.Conversations;
using Raffa.Renewals.Application.Cases;
using Raffa.SharedKernel;

namespace Raffa.Api.Tests;

/// <summary>
/// A renewal case started from a chat carries that chat over: what the user set there (their goals and constraints)
/// and Raffa's latest answers. A long answer is cut, never dropped, and the bare start command is left out.
/// </summary>
public sealed class ChatBriefTests
{
    private static readonly EntityId Chat = EntityId.New();

    private static ConversationMessageResult Message(ConversationRole role, string markdown, string? payload = null) =>
        new(EntityId.New(), Chat, role, ConversationMessageKind.Answer, markdown, "[]", "[]", null, null, null, DateTimeOffset.UtcNow, payload);

    private static ConversationDetailResult Conversation(params ConversationMessageResult[] messages) =>
        new(Chat, "Workday", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, messages);

    [Fact]
    public void The_user_constraints_and_raffa_levers_both_reach_the_case()
    {
        var brief = ChatBrief.From(Conversation(
            Message(ConversationRole.You, "How should we approach this renewal?"),
            Message(ConversationRole.Raffa, "**Levers**\n- Standalone renewal discount of 27.7% [1]\n- Net 60 days [2]"),
            Message(ConversationRole.You, "avvia il rinnovo con questi termini, tenendo conto che possiamo ridurre anche il numero di licenze del 10 %"),
            Message(ConversationRole.Raffa, "Avvio il rinnovo di **Workday**.", """{"actionProposal":{"kind":"start_renewal"}}"""),
            Message(ConversationRole.You, "aprilo in rinnovi")));

        Assert.NotNull(brief);
        Assert.StartsWith("Cosa hai indicato in chat:", brief);
        Assert.Contains("ridurre anche il numero di licenze del 10 %", brief);
        Assert.Contains("• Standalone renewal discount of 27.7%", brief);
        Assert.DoesNotContain("[1]", brief);
        Assert.DoesNotContain("aprilo in rinnovi", brief);
        Assert.DoesNotContain("Avvio il rinnovo di", brief);
    }

    [Fact]
    public void A_long_answer_is_cut_to_the_case_limit_not_dropped()
    {
        var longAnswer = string.Join(" ", Enumerable.Repeat("The discount lever is worth negotiating first.", 200));

        var brief = ChatBrief.From(Conversation(Message(ConversationRole.Raffa, longAnswer)), italian: false);

        Assert.NotNull(brief);
        Assert.StartsWith("Levers and strategy:", brief);
        Assert.True(brief!.Length <= RenewalCaseEngine.BriefMaxChars, brief.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.EndsWith("…", brief);
    }

    [Fact]
    public void An_empty_chat_carries_nothing() =>
        Assert.Null(ChatBrief.From(Conversation(Message(ConversationRole.You, "avvia il rinnovo"))));
}
