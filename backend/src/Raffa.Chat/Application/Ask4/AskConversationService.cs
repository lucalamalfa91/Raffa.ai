using System.Text.Json;
using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Domain.Conversations;
using Raffa.SharedKernel;

namespace Raffa.Chat.Application.Ask4;

/// <summary>
/// AskRaffa 4.0 Chiedi turn (prototype <c>askGlobal</c> / <c>convPush</c> / the "Per quale
/// contratto?" picker): asks <see cref="IAskAnswerService"/>, creates the conversation on the first
/// question (title "Supplier · question"), appends the user's question and the sourced answer, and
/// reopens an archived conversation by writing in it.
/// </summary>
public sealed class AskConversationService(ConversationService conversations, IAskAnswerService answers)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <param name="AnswerOnly">The picker's second half: the question is already in the
    /// conversation; answer it for <see cref="ContractId"/> and append only the answer.</param>
    public sealed record AskRequest(
        EntityId? ConversationId,
        string Question,
        EntityId? ContractId,
        bool AnswerOnly,
        string Lang);

    public sealed record AskResult(bool Found, bool NeedsContract, ConversationDetailResult? Conversation);

    public async Task<AskResult> AskAsync(
        TenantId tenantId, string userId, AskRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var question = request.Question.Trim();
        var answer = await answers
            .AnswerAsync(tenantId, userId, question, request.ContractId, request.Lang, cancellationToken)
            .ConfigureAwait(false);

        var conversationId = request.ConversationId;
        if (conversationId is { } existing)
        {
            var current = await conversations.GetAsync(tenantId, userId, existing, cancellationToken).ConfigureAwait(false);
            if (current is null)
            {
                return new AskResult(false, false, null);
            }
        }
        else
        {
            var created = await conversations
                .CreateTitledAsync(tenantId, userId, AskRules.Title(question, answer.Supplier), answer.ContractId, null, cancellationToken)
                .ConfigureAwait(false);
            conversationId = created.ConversationId;
        }

        if (!request.AnswerOnly)
        {
            await conversations.AppendMessageAsync(
                    tenantId, userId, conversationId.Value,
                    new AppendConversationMessageRequest(ConversationRole.You, ConversationMessageKind.Answer, question, "[]", "[]"),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!answer.NeedsContract)
        {
            await conversations.AppendMessageAsync(
                    tenantId, userId, conversationId.Value, ToRaffaMessage(answer), cancellationToken)
                .ConfigureAwait(false);
        }

        var detail = await conversations.GetAsync(tenantId, userId, conversationId.Value, cancellationToken).ConfigureAwait(false);
        return new AskResult(true, answer.NeedsContract, detail);
    }

    /// <summary>The answer as a conversation message: the source is its one citation, the CTA its one action.</summary>
    public static AppendConversationMessageRequest ToRaffaMessage(AskAnswer answer)
    {
        var refusal = answer.Source is AskRules.MarketSourceIt or AskRules.MarketSourceEn;
        var citations = answer.Source is null
            ? "[]"
            : JsonSerializer.Serialize(new[] { new { n = 1, corpus = "askraffa4", title = answer.Source, snippet = "" } }, Json);
        var actions = answer.Cta is null
            ? "[]"
            : JsonSerializer.Serialize(new[] { new { kind = answer.Cta.Kind, contractId = answer.Cta.ContractId?.Value } }, Json);
        return new AppendConversationMessageRequest(
            ConversationRole.Raffa,
            refusal ? ConversationMessageKind.Refusal : ConversationMessageKind.Answer,
            answer.Text ?? "",
            citations,
            actions);
    }
}
