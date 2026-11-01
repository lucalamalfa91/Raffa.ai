using Raffa.Api.Infrastructure;
using Raffa.Chat.Application.Ask4;
using Raffa.Chat.Application.Conversations;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;

namespace Raffa.Api;

/// <summary>
/// AskRaffa 4.0 Chiedi (README §7; BACKEND-SPEC §2.8, §4 Chat): one question → a sourced answer
/// (or the parameter picker when the contract is missing), explicit archive / unarchive, and the
/// validated contracts the pickers offer. The list filter <c>?state=recent|archived|case</c> lives
/// on <c>GET /api/conversations</c> (<see cref="ConversationsEndpointExtensions"/>).
/// </summary>
public static class AskConversationsEndpointExtensions
{
    public static IEndpointRouteBuilder MapAskConversationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/conversations/ask", AskAsync);
        endpoints.MapPost("/api/conversations/{id}/archive", (string id, HttpRequest r, ConversationService s, ICallerContext c, CancellationToken ct) => SetArchivedAsync(id, true, r, s, c, ct));
        endpoints.MapPost("/api/conversations/{id}/unarchive", (string id, HttpRequest r, ConversationService s, ICallerContext c, CancellationToken ct) => SetArchivedAsync(id, false, r, s, c, ct));
        endpoints.MapGet("/api/ask/contracts", ListContractsAsync);
        return endpoints;
    }

    public sealed record AskRequestBody(string? ConversationId, string? Question, string? ContractId, bool? AnswerOnly, string? Lang);

    private static async Task<IResult> AskAsync(
        AskRequestBody? body,
        HttpRequest request,
        AskConversationService askService,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        if (string.IsNullOrWhiteSpace(body?.Question))
        {
            return Results.BadRequest("A non-empty 'question' is required.");
        }

        if (!TryParseOptionalId(body.ConversationId, out var conversationId) || !TryParseOptionalId(body.ContractId, out var contractId))
        {
            return Results.BadRequest("'conversationId' and 'contractId' must be GUIDs when provided.");
        }

        var answerOnly = body.AnswerOnly == true;
        if (answerOnly && (conversationId is null || contractId is null))
        {
            return Results.BadRequest("'answerOnly' needs both 'conversationId' and 'contractId'.");
        }

        var result = await askService.AskAsync(
                caller.TenantId,
                caller.Identity!,
                new AskConversationService.AskRequest(conversationId, body.Question, contractId, answerOnly, body.Lang == "en" ? "en" : "it"),
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Found || result.Conversation is null)
        {
            return Results.NotFound();
        }

        var detail = result.Conversation;
        return Results.Ok(new
        {
            conversationId = detail.ConversationId.Value,
            needsContract = result.NeedsContract,
            conversation = ToDetail(detail),
        });
    }

    private static async Task<IResult> SetArchivedAsync(
        string id, bool archived, HttpRequest request, ConversationService conversations, ICallerContext callerContext, CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        if (!Guid.TryParse(id, out var guid))
        {
            return Results.BadRequest("The conversation id in the route must be a GUID.");
        }

        var summary = await conversations
            .SetArchivedAsync(caller.TenantId, caller.Identity!, new EntityId(guid), archived, cancellationToken)
            .ConfigureAwait(false);
        return summary is null ? Results.NotFound() : Results.Ok(ToSummary(summary));
    }

    private static async Task<IResult> ListContractsAsync(
        HttpRequest request, IContractFactsReader facts, ICallerContext callerContext, CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var validated = await facts.ListValidatedAsync(caller.TenantId, cancellationToken).ConfigureAwait(false);

        // The order the user validated them in (the prototype's `validated` list).
        var ordered = validated
            .OrderBy(c => c.ValidatedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(c => c.FixtureId ?? int.MaxValue)
            .ThenBy(c => c.Supplier, StringComparer.Ordinal);
        return Results.Ok(ordered.Select(c => new
        {
            contractId = c.ContractId,
            supplier = c.Supplier,
            name = c.Name,
            noticeDeadline = c.NoticeDeadline,
        }));
    }

    internal static object ToSummary(ConversationSummaryResult s) => new
    {
        id = s.ConversationId.Value,
        title = s.Title,
        customTitle = s.CustomTitle,
        scopeContractId = s.ScopeContractId?.Value,
        updatedAt = s.UpdatedAt,
        archived = s.Archived,
        archivedAt = s.ArchivedAt,
        caseId = s.CaseId?.Value,
        questionCount = s.QuestionCount,
    };

    private static object ToDetail(ConversationDetailResult d) => new
    {
        id = d.ConversationId.Value,
        title = d.Title,
        customTitle = d.CustomTitle,
        scopeContractId = d.ScopeContractId?.Value,
        createdAt = d.CreatedAt,
        updatedAt = d.UpdatedAt,
        archivedAt = d.ArchivedAt,
        caseId = d.CaseId?.Value,
        messages = d.Messages.Select(m => new
        {
            id = m.MessageId.Value,
            role = m.Role == Chat.Domain.Conversations.ConversationRole.You ? "you" : "raffa",
            kind = m.Kind.ToString().ToLowerInvariant(),
            markdown = m.Markdown,
            citations = ConversationsEndpointExtensions.ParseJsonArrayPublic(m.CitationsJson),
            actions = ConversationsEndpointExtensions.ParseJsonArrayPublic(m.ActionsJson),
            createdAt = m.CreatedAt,
        }),
    };

    private static bool TryParseOptionalId(string? text, out EntityId? id)
    {
        id = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!Guid.TryParse(text, out var guid))
        {
            return false;
        }

        id = new EntityId(guid);
        return true;
    }
}
