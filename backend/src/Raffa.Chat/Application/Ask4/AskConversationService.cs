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
    private const int RecentTurnsLimit = 8;

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
        var conversationId = request.ConversationId;
        ConversationDetailResult? existingConversation = null;
        if (conversationId is { } existing)
        {
            existingConversation = await conversations.GetAsync(tenantId, userId, existing, cancellationToken).ConfigureAwait(false);
            if (existingConversation is null)
            {
                return new AskResult(false, false, null);
            }
        }

        // The chat remembers: a follow-up is read against the turns so far, and in the contract the
        // conversation is already about (so "e la penale?" never asks "which contract?" again).
        var contractId = request.ContractId ?? existingConversation?.ScopeContractId;
        var history = existingConversation is null
            ? null
            : existingConversation.Messages
                .Where(m => !string.IsNullOrWhiteSpace(m.Markdown))
                .TakeLast(RecentTurnsLimit)
                .Select(m => (Role: m.Role == ConversationRole.You ? "you" : "raffa", m.Markdown))
                .ToList();
        // The picker's second half: the question is already the last turn — do not hand it over twice.
        if (request.AnswerOnly && history is { Count: > 0 } && history[^1].Role == "you" && history[^1].Markdown.Trim() == question)
        {
            history.RemoveAt(history.Count - 1);
        }

        var answer = await answers
            .AnswerAsync(tenantId, userId, question, contractId, request.Lang, history, cancellationToken)
            .ConfigureAwait(false);

        if (conversationId is null)
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
