using Raffa.AiFlows.Ask.Interview;
using Raffa.AiFlows.Ask.Orchestration;
using Raffa.Chat.Application.Ask4;
using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Application.Memory;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Domain.Conversations;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Renewals;

namespace Raffa.Api;

/// <summary>
/// The renewal case composer on the full agentic Ask engine (strategy, negotiation council, drafting,
/// capability check), in one conversation per case and person so a follow-up keeps its context. The
/// scripted <see cref="CaseQuestionAnswerer"/> stays the answer wherever the engine is not the production
/// one (fixtures, parity), when the market question meets the 4.0 no-benchmark limit, or if the engine fails.
/// </summary>
internal sealed class AgenticCaseQuestionAnswerer(
    CaseQuestionAnswerer scripted,
    IAskAnswerService answerService,
    AskAnswerOptions options,
    IServiceProvider services,
    ILogger<AgenticCaseQuestionAnswerer> logger) : ICaseQuestionAnswerer
{
    public Task<CaseQuestionAnswer> AnswerAsync(
        TenantId tenantId, EntityId contractId, string text, string? lang = null, CancellationToken cancellationToken = default) =>
        scripted.AnswerAsync(tenantId, contractId, text, lang, cancellationToken);

    public async Task<CaseQuestionAnswer> AnswerInCaseAsync(
        TenantId tenantId, string? userId, Guid caseId, EntityId contractId, string text, string? lang = null, CancellationToken cancellationToken = default)
    {
        if (answerService is not Raffa.AiFlows.Ask.V4.CopilotAskAnswerService
            || string.IsNullOrWhiteSpace(userId)
            || (options.NoBenchmark && AskRules.IsMarketQuestion(text)))
        {
            return await scripted.AnswerAsync(tenantId, contractId, text, lang, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var language = Raffa.SharedKernel.Language.ResponseLanguage.Use(lang == "en" ? "en" : "it");
            var conversations = services.GetRequiredService<ConversationService>();
            var caseKey = new EntityId(caseId);
            var existing = (await conversations
                    .ListByStateAsync(tenantId, userId, ConversationListState.Case, cancellationToken: cancellationToken)
                    .ConfigureAwait(false))
                .FirstOrDefault(c => c.CaseId == caseKey);
            var conversationId = existing?.ConversationId
                ?? (await conversations
                    .CreateTitledAsync(tenantId, userId, AskRules.Title(text, null), contractId, caseKey, cancellationToken)
                    .ConfigureAwait(false)).ConversationId;
            var conversation = await conversations.GetAsync(tenantId, userId, conversationId, cancellationToken).ConfigureAwait(false);
            if (conversation is null)
            {
                return await scripted.AnswerAsync(tenantId, contractId, text, lang, cancellationToken).ConfigureAwait(false);
            }

            await ConversationsEndpointExtensions.AskAndAppendAsync(
                    services.GetRequiredService<AskCopilotService>(),
                    conversations,
                    services.GetRequiredService<CapabilityFollowUpAppender>(),
                    services.GetRequiredService<UserProcurementMemoryService>(),
                    tenantId, userId, conversationId, conversation,
                    text, text, AskTurnHints.None, null, cancellationToken)
                .ConfigureAwait(false);

            var after = await conversations.GetAsync(tenantId, userId, conversationId, cancellationToken).ConfigureAwait(false);
            var reply = after?.Messages.LastOrDefault(m => m.Role == ConversationRole.Raffa);
            if (reply is null || string.IsNullOrWhiteSpace(reply.Markdown))
            {
                return await scripted.AnswerAsync(tenantId, contractId, text, lang, cancellationToken).ConfigureAwait(false);
            }

            var body = reply.Markdown.Trim();
            // A drafted email is part of the answer here: the case shows it as text, ready to copy.
            var draft = ReplyPayloadJson.Deserialize(reply.PayloadJson)?.Draft;
            if (draft is not null && !body.Contains(draft.Body.Trim(), StringComparison.Ordinal))
            {
                body += "\n\n" + (lang == "en" ? "Subject: " : "Oggetto: ") + draft.Subject + "\n\n" + draft.Body.Trim();
            }

            return new CaseQuestionAnswer(body, FirstSource(reply.CitationsJson));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The agentic case answer failed for case {CaseId}; answering with the scripted path.", caseId);
            return await scripted.AnswerAsync(tenantId, contractId, text, lang, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string? FirstSource(string citationsJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(citationsJson) ? "[]" : citationsJson);
            foreach (var c in doc.RootElement.EnumerateArray())
            {
                if (c.ValueKind == System.Text.Json.JsonValueKind.Object
                    && c.TryGetProperty("title", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(t.GetString()))
                {
                    var page = c.TryGetProperty("page", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.Number ? " p." + p.GetInt32() : "";
                    return t.GetString() + page;
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
        }

        return null;
    }
}
