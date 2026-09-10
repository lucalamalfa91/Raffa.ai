using Raffa.Chat.Application.Conversations;

namespace Raffa.Api;

/// <summary>
/// Maps `POST /api/chat/query` (task E13/F06/US01/T01, ask-engine; ADR-024 §6: "kept one release
/// as a thin alias that creates a conversation; then removed"). The real pack-composition root is
/// now <see cref="AskCopilotService"/> (this task's own new composition service — "ChatEndpointExtensions.cs
/// becomes the pack composition root" per the task's own coding objective, realized as a
/// dedicated type rather than inline in this file, so <c>Raffa.Api.Tests</c> and
/// <c>Raffa.IntegrationTests</c> can exercise the composition independently of the two thin HTTP
/// handlers that call it). This file's only remaining job is the alias: create a new conversation
/// (ADR-024 "The global Ask bar always opens a new chat" — the rule a caller of this legacy
/// endpoint gets by construction, since it never carries a conversation id of its own) and delegate
/// straight into <see cref="ConversationsEndpointExtensions.AskAndAppendAsync"/> — the exact same
/// "ask, then persist both turns, then wire-shape the reply" implementation
/// `POST /api/conversations/{id}/messages` uses, so the two endpoints can never drift apart.
///
/// <para>
/// <b>Superseded by this task</b>: the old `Structured`/`Semantic`
/// <c>Raffa.Chat.Application.AskRaffaQueryRouter</c> + <c>EmbeddingRetrievalService</c> +
/// <c>RagAnswerService</c> pipeline this file used to run directly (task E02/F04/US02/T01) is now
/// composed *inside* <see cref="AskCopilotService"/>'s own intent handling instead (the legacy
/// router/planner/handler trio is reused there — see that type's own doc comment).
/// <c>RagAnswerService</c> itself stays registered and independently tested
/// (<c>Raffa.Chat.Tests.RagAnswerServiceTests</c>) but is no longer this endpoint's own call path
/// — ADR-024 replaces the old `{ question, intent, canDetermine, answer, citations, message }`
/// response shape entirely with the §6 reply contract <see cref="AskCopilotService"/> now produces.
/// </para>
///
/// Same interim `X-Tenant-Id`/`X-User-Id` caller-identity resolution as
/// <see cref="ConversationsEndpointExtensions"/> (reused directly via its own internal helpers, not
/// re-implemented — see that type's own "Caller identity" doc comment section).
/// </summary>
public static class ChatEndpointExtensions
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/chat/query", PostChatQueryAsync);
        return endpoints;
    }

    private static async Task<IResult> PostChatQueryAsync(
        ChatQueryRequest? request,
        HttpRequest httpRequest,
        ConversationService conversationService,
        AskCopilotService askCopilotService,
        CancellationToken cancellationToken)
    {
        if (!ConversationsEndpointExtensions.TryResolveTenant(httpRequest, out var tenantId, out var tenantError))
        {
            return Results.BadRequest(tenantError);
        }

        if (!ConversationsEndpointExtensions.TryResolveUserId(httpRequest, out var userId, out var userError))
        {
            return Results.BadRequest(userError);
        }

        if (string.IsNullOrWhiteSpace(request?.Question))
        {
            return Results.BadRequest("A non-empty 'question' is required.");
        }

        var conversation = await conversationService
            .CreateAsync(tenantId, userId, scopeContractId: null, cancellationToken)
            .ConfigureAwait(false);

        // A freshly created conversation has no messages yet — ConversationDetailResult's own
        // Messages list is all AskAndAppendAsync actually reads from it, and a brand-new
        // conversation always has zero, so this is an honest empty history (not a second,
        // wasted GetAsync round trip just to re-confirm what CreateAsync already guarantees).
        // CreatedAt/UpdatedAt both echo the summary's own UpdatedAt — ConversationService
        // .CreateAsync sets both fields to the identical "now" for a brand-new row.
        var detail = new ConversationDetailResult(
            conversation.ConversationId,
            conversation.Title,
            conversation.ScopeContractId,
            conversation.UpdatedAt,
            conversation.UpdatedAt,
            []);

        var reply = await ConversationsEndpointExtensions.AskAndAppendAsync(
                askCopilotService, conversationService, tenantId, userId, conversation.ConversationId,
                detail, request.Question, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(reply);
    }

    /// <summary>
    /// `POST /api/chat/query` request body — `{ question }`, unchanged from before this task (only
    /// the response shape changes). A nested type so
    /// `Raffa.ArchitectureTests.DependencyDirectionTests.Host_must_not_contain_domain_types` never
    /// sees it — same reasoning this type's own original doc comment already gave (nested types
    /// report via <c>IsNestedPublic</c>, not <c>IsPublic</c>, so that test's "every top-level public
    /// type" scan skips it).
    /// </summary>
    public sealed record ChatQueryRequest(string? Question);
}
