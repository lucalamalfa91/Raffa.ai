using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Raffa.Api.Infrastructure;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Application.Feedback;
using Raffa.Chat.Application.Interview;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Domain.Conversations;
using Raffa.SharedKernel;

namespace Raffa.Api;

/// <summary>
/// Maps `GET /api/conversations`, `POST /api/conversations`, `GET/PATCH/DELETE /api/conversations/{id}`,
/// `POST /api/conversations/{id}/restore`
/// (product spec §7; ADR-024 "Conversations (D5)"; story us-01-conversations AC-2/AC-3, task
/// E13/F05/US01/T02). Thin composition per ADR-002 — the actual decisions (tenant + user scoping,
/// title derivation, RLS-backstopped isolation) are made by
/// <see cref="ConversationService"/> (task E13/F05/US01/T01); this file only translates HTTP
/// &lt;-&gt; those calls and resolves the one thing <see cref="ConversationService"/> cannot:
/// which HTTP concept ("this request's tenant", "this request's caller") maps onto its own
/// <c>tenantId</c>/<c>userId</c> parameters.
///
/// <para>
/// <b>Caller identity (AC-3, ADR-010)</b>: every handler below resolves tenant *and* caller in one
/// call to <see cref="ICallerContext.ResolveTenantAsync"/> (see each handler's own NW-05 comment
/// for the 401/400/404 order) — <see cref="CallerTenantResult.Identity"/> is the validated token's
/// <c>oid</c>, never a header. This paragraph used to describe an interim <c>X-User-Id</c> fallback
/// for a host that authenticated nobody; that gap closed in wave w15 (NW-05) and
/// <c>OQ-askv2-005</c> retired with task E18/F02/US01/T01 (ADR-024 w16 footer clause 1) — there is
/// no fallback branch left to document here.
/// </para>
///
/// <para>
/// <b>Tenant (ADR-009)</b>: same interim `X-Tenant-Id` header placeholder as every other endpoint
/// in this host (<see cref="ChatEndpointExtensions"/> et al.) — parsed exactly the same way as
/// <see cref="ChatEndpointExtensions.MapChatEndpoints"/>'s own `POST /api/chat/query` handler.
/// </para>
///
/// <para>
/// <b>404, not 403 (AC-1/AC-2)</b>: <see cref="GetConversationAsync"/> returns 404 whether
/// `{id}` does not exist, belongs to another tenant (RLS backstop, ADR-009), or belongs to another
/// user of the same tenant (an application-level filter — <see cref="ConversationService"/>'s own
/// doc comment: "RLS has no per-user predicate") — the same "one honest outcome, never a
/// distinguishing signal" rule <see cref="ConversationService.GetAsync"/> itself already
/// documents.
/// </para>
///
/// <para>
/// <b>`POST /api/conversations/{id}/messages` (task E13/F06/US01/T01, ask-engine; ADR-024 §6)</b>:
/// the one endpoint that actually runs the Ask engine. Resolves tenant/user exactly like every
/// other handler in this file, loads the conversation's own recent turns (R-ASK-05 "the pack + last
/// N turns"), calls <see cref="AskCopilotService.AskAsync"/> (the pack-composition root —
/// <c>Raffa.Api.AskCopilotService</c>'s own doc comment), then persists both the caller's
/// question and Raffa's reply through <see cref="ConversationService.AppendMessageAsync"/> — this
/// module still never computes a reply itself (<see cref="AppendConversationMessageRequest"/>'s own
/// doc comment: "this module only persists what a caller already computed" — <c>AskCopilotService</c>
/// is that caller now). <see cref="ChatEndpointExtensions"/>'s `POST /api/chat/query` alias creates a
/// conversation and calls straight into <see cref="AskAndAppendAsync"/> below, so the two endpoints
/// share one implementation of "ask, then persist both turns, then wire-shape the reply" rather than
/// two independent copies.
/// </para>
/// </summary>
public static class ConversationsEndpointExtensions
{

    /// <summary>R-ASK-05 "the pack + last N turns" — how many of the conversation's own prior
    /// messages <see cref="AskAndAppendAsync"/> feeds <c>AskCopilotService.AskAsync</c> as history.
    /// Not council-pinned to an exact number; three full exchanges (six turns) is generous enough
    /// for pronoun/follow-up continuity without growing the prompt unbounded as a conversation gets
    /// long.</summary>
    private const int RecentTurnsLimit = 6;

    public static IEndpointRouteBuilder MapConversationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/conversations", GetConversationsAsync);
        endpoints.MapPost("/api/conversations", PostConversationAsync);
        endpoints.MapGet("/api/conversations/{id}", GetConversationAsync);
        endpoints.MapPatch("/api/conversations/{id}", RenameConversationAsync);
        endpoints.MapDelete("/api/conversations/{id}", DeleteConversationAsync);
        endpoints.MapPost("/api/conversations/{id}/restore", RestoreConversationAsync);
        endpoints.MapPost("/api/conversations/{id}/messages", PostConversationMessageAsync);
        endpoints.MapPost("/api/conversations/{id}/feedback", PostConversationFeedbackAsync);
        return endpoints;
    }

    /// <summary>`GET /api/conversations` (R-CONV-02: "the rail shows the user's last 5
    /// conversations") — optional `?take=` overrides <see cref="ConversationService.DefaultRecentLimit"/>.
    /// Response is a bare array, not a `{ items, totalCount }` envelope — there is no paging
    /// concept for "my last N conversations".</summary>
    private static async Task<IResult> GetConversationsAsync(
        ICallerContext callerContext,
        HttpRequest request, ConversationService conversationService, CancellationToken cancellationToken)
    {
        // NW-05 (ADR-010 w15 footer; ADR-022 w15 footer clause 2): identity first, then the tenant
        // header as an authorized selector, then membership -- 401 / 400 / 404 in that order, all
        // owned by ICallerContext (acceptance A15-8). The scope it hands back is the tenant scope
        // this handler runs in; disposing it here is the same lifetime the old BeginScope had.
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantId = caller.TenantId;

        // NW-05: the per-user key is the validated identity ICallerContext just verified the membership for.

        var userId = caller.Identity!;

        if (!TryParseTake(request.Query, out var take, out var takeError))
        {
            return Results.BadRequest(takeError);
        }

        if (!TryParseArchived(request.Query, out var archive, out var archivedError))
        {
            return Results.BadRequest(archivedError);
        }

        var conversations = await conversationService
            .ListRecentAsync(tenantId, userId, take, archive, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(conversations.Select(ToSummaryResponse));
    }

    /// <summary>`POST /api/conversations` (AC-2: `{ scopeContractId? }` → 201). An absent/blank
    /// `scopeContractId` opens a conversation from the global Ask bar (ADR-024: "The global Ask
    /// bar always opens a new chat"); a well-formed one scopes it to that contract (Contract 360's
    /// "Ask about it"). <paramref name="request"/> is nullable so a caller posting no body at all
    /// (every field is optional) still binds, rather than a spurious 400 from empty-body model
    /// binding.</summary>
    private static async Task<IResult> PostConversationAsync(
        CreateConversationRequest? request,
        HttpRequest httpRequest,
        ConversationService conversationService,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        // NW-05 (ADR-010 w15 footer; ADR-022 w15 footer clause 2): identity first, then the tenant
        // header as an authorized selector, then membership -- 401 / 400 / 404 in that order, all
        // owned by ICallerContext (acceptance A15-8). The scope it hands back is the tenant scope
        // this handler runs in; disposing it here is the same lifetime the old BeginScope had.
        var caller = await callerContext.ResolveTenantAsync(httpRequest, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantId = caller.TenantId;

        // NW-05: the per-user key is the validated identity ICallerContext just verified the membership for.

        var userId = caller.Identity!;

        EntityId? scopeContractId = null;
        var scopeContractIdText = request?.ScopeContractId;
        if (!string.IsNullOrWhiteSpace(scopeContractIdText))
        {
            if (!Guid.TryParse(scopeContractIdText, out var scopeContractGuid))
            {
                return Results.BadRequest("'scopeContractId' must be a GUID when provided.");
            }

            scopeContractId = new EntityId(scopeContractGuid);
        }

        var created = await conversationService
            .CreateAsync(tenantId, userId, scopeContractId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"/api/conversations/{created.ConversationId}", ToSummaryResponse(created));
    }

    /// <summary>`GET /api/conversations/{id}` (AC-2: conversation + messages, oldest first). See
    /// the type doc comment's "404, not 403" note for why a wrong-tenant and a wrong-user id read
    /// back identically.</summary>
    private static async Task<IResult> GetConversationAsync(
        string id,
        HttpRequest request,
        ConversationService conversationService,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        // NW-05 (ADR-010 w15 footer; ADR-022 w15 footer clause 2): identity first, then the tenant
        // header as an authorized selector, then membership -- 401 / 400 / 404 in that order, all
        // owned by ICallerContext (acceptance A15-8). The scope it hands back is the tenant scope
        // this handler runs in; disposing it here is the same lifetime the old BeginScope had.
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantId = caller.TenantId;

        // NW-05: the per-user key is the validated identity ICallerContext just verified the membership for.

        var userId = caller.Identity!;

        if (!Guid.TryParse(id, out var conversationGuid))
        {
            return Results.BadRequest("The conversation id in the route must be a GUID.");
        }

        var conversation = await conversationService
            .GetAsync(tenantId, userId, new EntityId(conversationGuid), cancellationToken)
            .ConfigureAwait(false);

        if (conversation is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(ToDetailResponse(conversation));
    }

    /// <summary>`PATCH /api/conversations/{id}` — renames the caller's own conversation:
    /// `{ title }` sets its name (at most <see cref="ConversationService.TitleMaxLength"/>
    /// characters once whitespace is collapsed), a blank or null `title` clears it so the automatic
    /// title shows again. 200 with the summary (carrying `customTitle`). Same guard order and
    /// 404-not-403 rule as <see cref="DeleteConversationAsync"/>; not Admin-gated.</summary>
    private static async Task<IResult> RenameConversationAsync(
        string id,
        RenameConversationRequest? request,
        HttpRequest httpRequest,
        ConversationService conversationService,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(httpRequest, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantId = caller.TenantId;
        var userId = caller.Identity!;

        if (!Guid.TryParse(id, out var conversationGuid))
        {
            return Results.BadRequest("The conversation id in the route must be a GUID.");
        }

        var name = ConversationService.NormalizeCustomTitle(request?.Title);
        if (name is { Length: > ConversationService.TitleMaxLength })
        {
            return Results.BadRequest($"'title' must be at most {ConversationService.TitleMaxLength} characters.");
        }

        var renamed = await conversationService
            .RenameAsync(tenantId, userId, new EntityId(conversationGuid), name, cancellationToken)
            .ConfigureAwait(false);

        return renamed is null ? Results.NotFound() : Results.Ok(ToSummaryResponse(renamed));
    }

    /// <summary>`POST /api/conversations/{id}/restore` — takes the caller's own chat back out of the
    /// archive (<see cref="ConversationService.RestoreAsync"/>): 200 with the summary, now
    /// `archived: false`; a chat that was not archived comes back unchanged. No body. Same guard
    /// order and 404-not-403 rule as <see cref="DeleteConversationAsync"/>; not Admin-gated.</summary>
    private static async Task<IResult> RestoreConversationAsync(
        string id,
        HttpRequest request,
        ConversationService conversationService,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantId = caller.TenantId;
        var userId = caller.Identity!;

        if (!Guid.TryParse(id, out var conversationGuid))
        {
            return Results.BadRequest("The conversation id in the route must be a GUID.");
        }

        var restored = await conversationService
            .RestoreAsync(tenantId, userId, new EntityId(conversationGuid), cancellationToken)
            .ConfigureAwait(false);

        return restored is null ? Results.NotFound() : Results.Ok(ToSummaryResponse(restored));
    }

    /// <summary>`DELETE /api/conversations/{id}` — the caller's own conversation, 204 on success.
    /// Same 404-not-403 rule as <see cref="GetConversationAsync"/> (unknown / other tenant / other
    /// user). Any live member can delete their own chat; this is not Admin-gated.</summary>
    private static async Task<IResult> DeleteConversationAsync(
        string id,
        HttpRequest request,
        ConversationService conversationService,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantId = caller.TenantId;
        var userId = caller.Identity!;

        if (!Guid.TryParse(id, out var conversationGuid))
        {
            return Results.BadRequest("The conversation id in the route must be a GUID.");
        }

        var deleted = await conversationService
            .DeleteAsync(tenantId, userId, new EntityId(conversationGuid), cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }

    /// <summary>
    /// `POST /api/conversations/{id}/messages` (AC-8: returns the §6 reply contract). Same guard
    /// order as <see cref="GetConversationAsync"/> (tenant, then user, then route-id GUID) before
    /// any database call; 404 under the identical "wrong tenant / wrong user / unknown id, one
    /// honest outcome" rule.
    /// </summary>
    private static async Task<IResult> PostConversationMessageAsync(
        string id,
        PostConversationMessageRequest? request,
        HttpRequest httpRequest,
        ConversationService conversationService,
        Raffa.Api.AskCopilotService askCopilotService,
        CapabilityCheckDispatcher capabilityCheckDispatcher,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        // NW-05 (ADR-010 w15 footer; ADR-022 w15 footer clause 2): identity first, then the tenant
        // header as an authorized selector, then membership -- 401 / 400 / 404 in that order, all
        // owned by ICallerContext (acceptance A15-8). The scope it hands back is the tenant scope
        // this handler runs in; disposing it here is the same lifetime the old BeginScope had.
        var caller = await callerContext.ResolveTenantAsync(httpRequest, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantId = caller.TenantId;

        // NW-05: the per-user key is the validated identity ICallerContext just verified the membership for.

        var userId = caller.Identity!;

        if (!Guid.TryParse(id, out var conversationGuid))
        {
            return Results.BadRequest("The conversation id in the route must be a GUID.");
        }

        if (string.IsNullOrWhiteSpace(request?.Question))
        {
            return Results.BadRequest("A non-empty 'question' is required.");
        }

        var conversationId = new EntityId(conversationGuid);

        var conversation = await conversationService
            .GetAsync(tenantId, userId, conversationId, cancellationToken)
            .ConfigureAwait(false);

        if (conversation is null)
        {
            return Results.NotFound();
        }

        var hints = AskTurnHints.None;
        var effectiveQuestion = request.Question;
        string? youInterviewJson = null;

        if (request.InterviewAnswer is { } interviewAnswer)
        {
            var resolution = await ResolveInterviewAnswerAsync(
                    conversationService, tenantId, userId, conversationId, interviewAnswer, request.Question, cancellationToken)
                .ConfigureAwait(false);
            if (resolution.Failure is not null)
            {
                return resolution.Failure;
            }

            hints = resolution.Hints;
            effectiveQuestion = resolution.EffectiveQuestion;
            youInterviewJson = resolution.YouInterviewJson;
        }
        else if (request.WebResearch == true)
        {
            // ADR-032: the toggle is the consent for this question; the engine still checks the
            // kill switch, the workspace opt-in and the budget.
            hints = hints with { WebMode = true };
        }

        var reply = await AskAndAppendAsync(
                askCopilotService, conversationService, capabilityCheckDispatcher, tenantId, userId, conversationId, conversation,
                request.Question, effectiveQuestion, hints, youInterviewJson, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(reply);
    }

    /// <summary>
    /// Shared by <see cref="PostConversationMessageAsync"/> and
    /// <see cref="ChatEndpointExtensions"/>'s `POST /api/chat/query` alias: runs the Ask engine
    /// against <paramref name="conversation"/>'s own recent turns, then persists both the user's
    /// question and Raffa's reply as new <see cref="ConversationMessage"/> rows — one
    /// implementation of "ask, then persist both turns, then wire-shape the reply", not two.
    ///
    /// <para>
    /// <b>Scope (task E25/F03/US01/T01, NW-56; ADR-024)</b>: also threads
    /// <paramref name="conversation"/>'s own persisted <c>ScopeContractId</c> into
    /// <see cref="AskCopilotService.AskAsync"/>, so a conversation opened from Contract 360's "Ask
    /// about it" scopes every turn to that contract (see that method's own doc comment for the
    /// mechanism). The global Ask bar's alias always creates a conversation with
    /// <c>scopeContractId: null</c> (<see cref="ChatEndpointExtensions.MapChatEndpoints"/>'s own
    /// handler), so this is <see langword="null"/> there by construction, not by a second check
    /// here.
    /// </para>
    ///
    /// <para>
    /// <b>Deepened, not re-wired (task E27/F02/US01/T01, NW-76; ADR-024 w19 cl. 12)</b>: this call
    /// site already threaded <c>conversation.ScopeContractId</c> through unchanged — the fix that
    /// task adds (a scoped id winning over a same-name portfolio hit, an unseen id refusing, lock
    /// 4's <c>PortfolioMarketPosition</c> exception) all lives one level down, inside
    /// <c>AskCopilotService.AskAsync</c>/<c>BuildInDomainReplyAsync</c> — this file's own doc
    /// comment on that type is where the mechanism is documented.
    /// </para>
    ///
    /// <para>
    /// <b>The capability follow-up (ADR-031)</b>: a fresh in-domain turn starts the capability
    /// check beside the answer (<see cref="CapabilityCheckSlot"/>). Once the answer is persisted,
    /// a check that already found an operation Raffa cannot perform appends its proposal as a
    /// separate Raffa message, returned as <c>followUpMessage</c> (the stored-message shape of
    /// <c>GET /api/conversations/{id}</c>); a check still running is handed to
    /// <see cref="CapabilityCheckDispatcher.AppendWhenDone"/> and the reply says
    /// <c>capabilityCheck: "pending"</c>, so the client looks for the follow-up in the
    /// conversation. The answer never waits for the check.
    /// </para>
    /// </summary>
    internal static async Task<object> AskAndAppendAsync(
        Raffa.Api.AskCopilotService askCopilotService,
        ConversationService conversationService,
        CapabilityCheckDispatcher capabilityCheckDispatcher,
        TenantId tenantId,
        string userId,
        EntityId conversationId,
        ConversationDetailResult conversation,
        string question,
        string effectiveQuestion,
        AskTurnHints hints,
        string? youInterviewJson,
        CancellationToken cancellationToken)
    {
        var recentTurns = conversation.Messages
            .TakeLast(RecentTurnsLimit)
            .Select(m => (Role: ToWireRole(m.Role), Markdown: m.Markdown))
            .ToList();

        // A capability follow-up (ADR-031) sits after the turn it follows; it is never what a typed
        // message answers, so an interview right before it still counts as the last Raffa turn.
        var previousRaffaTurnWasInterview =
            conversation.Messages
                .LastOrDefault(m => m.Role == ConversationRole.Raffa && !IsCapabilityFollowUp(m))?.Kind == ConversationMessageKind.Interview;

        var capabilityCheck = new CapabilityCheckSlot();
        var reply = await askCopilotService
            .AskAsync(
                tenantId, effectiveQuestion, recentTurns, userId, conversation.ScopeContractId, hints,
                previousRaffaTurnWasInterview, capabilityCheck, cancellationToken)
            .ConfigureAwait(false);

        await conversationService.AppendMessageAsync(
                tenantId, userId, conversationId,
                new AppendConversationMessageRequest(
                    ConversationRole.You, ConversationMessageKind.Answer, question, "[]", "[]",
                    InterviewJson: youInterviewJson),
                cancellationToken)
            .ConfigureAwait(false);

        var raffaMessage = await conversationService.AppendMessageAsync(
                tenantId, userId, conversationId, ToAppendRequest(reply), cancellationToken)
            .ConfigureAwait(false);

        var messageId = raffaMessage?.MessageId ?? conversationId;

        object? followUpMessage = null;
        string? capabilityCheckState = null;
        if (capabilityCheck.FollowUp is { } check && raffaMessage is not null)
        {
            if (check.IsCompleted)
            {
                if (await check.ConfigureAwait(false) is { } followUp)
                {
                    var appended = await capabilityCheckDispatcher
                        .AppendAsync(tenantId, userId, conversationId, raffaMessage.MessageId, followUp, cancellationToken)
                        .ConfigureAwait(false);
                    followUpMessage = appended is null ? null : ToMessageResponse(appended, hasLaterTurn: false);
                }
            }
            else
            {
                capabilityCheckDispatcher.AppendWhenDone(check, tenantId, userId, conversationId, raffaMessage.MessageId);
                capabilityCheckState = "pending";
            }
        }

        return new
        {
            conversationId = conversationId.Value,
            messageId = messageId.Value,
            kind = reply.Kind.ToApiValue(),
            answerMarkdown = reply.AnswerMarkdown,
            citations = reply.Citations.Select(ToCitationJson),
            actions = reply.Actions.Select(ToActionJson),
            provenance = new
            {
                sources = reply.Provenance.Sources,
                modelId = reply.Provenance.ModelId,
                promptVersion = reply.Provenance.PromptVersion,
                inputHash = reply.Provenance.InputHash,
                unverified = reply.Provenance.Unverified,
            },
            followUps = reply.FollowUps,
            payload = reply.Payload is null ? (JsonElement?)null : ReplyPayloadJson.ToJsonElement(reply.Payload),
            interview = reply.Interview is null ? null : ToInterviewJson(reply.Interview, answered: false),
            capabilityCheck = capabilityCheckState,
            followUpMessage,
        };
    }

    /// <summary>ADR-031: a Raffa message appended after an answer by the capability check.</summary>
    private static bool IsCapabilityFollowUp(ConversationMessageResult message) =>
        message.Role == ConversationRole.Raffa &&
        ReplyPayloadJson.Deserialize(message.PayloadJson)?.CapabilityCheckFor is not null;

    /// <summary>The one mapping from a <see cref="CopilotReply"/> onto the persisted Raffa turn —
    /// shared by <see cref="AskAndAppendAsync"/> and the feedback confirmation
    /// (<see cref="PostConversationFeedbackAsync"/>), so the two never drift on how citations,
    /// actions and the ADR-030 payload are serialized.</summary>
    internal static AppendConversationMessageRequest ToAppendRequest(CopilotReply reply) =>
        new(
            ConversationRole.Raffa,
            ToMessageKind(reply.Kind),
            reply.AnswerMarkdown,
            JsonSerializer.Serialize(reply.Citations.Select(ToCitationJson)),
            JsonSerializer.Serialize(reply.Actions.Select(ToActionJson)),
            reply.Provenance.ModelId,
            reply.Provenance.PromptVersion,
            reply.Provenance.InputHash,
            reply.Payload is null ? null : ReplyPayloadJson.Serialize(reply.Payload),
            reply.Interview is null ? null : InterviewJsonCodec.SerializeTurn(reply.Interview));

    /// <summary>
    /// `POST /api/conversations/{id}/feedback` (ADR-030 D5): the in-chat feedback card's one call.
    /// Same 401/400/404 ladder as <see cref="PostConversationMessageAsync"/>; then the body is
    /// validated against the fixed interview vocabulary (<see cref="FeedbackAnswers.Validate"/>),
    /// <see cref="FeedbackService.SubmitAsync"/> stores the request, publishes it best-effort and
    /// builds the confirmation turn, which this handler persists exactly like a reply and returns
    /// as `message` — so the client appends it to the thread and a resumed conversation shows the
    /// same turn. 409 when the same offer was already answered (the existing row's facts, no new
    /// turn).
    /// </summary>
    private static async Task<IResult> PostConversationFeedbackAsync(
        string id,
        PostConversationFeedbackRequest? request,
        HttpRequest httpRequest,
        ConversationService conversationService,
        FeedbackService feedbackService,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(httpRequest, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantId = caller.TenantId;
        var userId = caller.Identity!;

        if (!Guid.TryParse(id, out var conversationGuid))
        {
            return Results.BadRequest("The conversation id in the route must be a GUID.");
        }

        if (request is null || !Guid.TryParse(request.MessageId, out var messageGuid))
        {
            return Results.BadRequest("A 'messageId' (GUID of the Raffa turn carrying the feedback offer) is required.");
        }

        var (answers, error) = FeedbackAnswers.Validate(request.Answers?.What, request.Answers?.Frequency, request.Answers?.Importance);
        if (answers is null)
        {
            return Results.BadRequest(error);
        }

        var conversationId = new EntityId(conversationGuid);
        var submitted = await feedbackService
            .SubmitAsync(tenantId, userId, conversationId, new EntityId(messageGuid), answers, cancellationToken)
            .ConfigureAwait(false);

        switch (submitted.Status)
        {
            case FeedbackSubmitStatus.NotFound:
                return Results.NotFound();
            case FeedbackSubmitStatus.InvalidMessage:
                return Results.BadRequest("'messageId' must name a Raffa turn of this conversation that carries a feedback offer.");
            case FeedbackSubmitStatus.AlreadySubmitted:
                return Results.Conflict(ToFeedbackResponse(submitted.Request!, message: null));
        }

        var confirmation = await conversationService.AppendMessageAsync(
                tenantId, userId, conversationId, ToAppendRequest(submitted.Confirmation!), cancellationToken)
            .ConfigureAwait(false);

        return Results.Created(
            $"/api/conversations/{conversationId.Value}/feedback",
            ToFeedbackResponse(submitted.Request!, confirmation is null ? null : ToMessageResponse(confirmation, hasLaterTurn: false)));
    }

    private static object ToFeedbackResponse(FeatureRequestResult request, object? message) => new
    {
        feedbackId = request.FeedbackId.Value,
        status = request.Status,
        issueNumber = request.IssueNumber,
        issueUrl = request.IssueUrl,
        message,
    };

    private static async Task<InterviewAnswerResolution> ResolveInterviewAnswerAsync(
        ConversationService conversationService,
        TenantId tenantId,
        string userId,
        EntityId conversationId,
        InterviewAnswerRequest answer,
        string typedQuestion,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(answer.MessageId, out var messageGuid))
        {
            return InterviewAnswerResolution.Fail(Results.BadRequest("'interviewAnswer.messageId' must be a GUID."));
        }

        if (string.IsNullOrWhiteSpace(answer.QuestionKey))
        {
            return InterviewAnswerResolution.Fail(Results.BadRequest("'interviewAnswer.questionKey' is required."));
        }

        var messageId = new EntityId(messageGuid);
        var message = await conversationService
            .GetMessageAsync(tenantId, userId, conversationId, messageId, cancellationToken)
            .ConfigureAwait(false);

        var record = message is { Role: ConversationRole.Raffa, Kind: ConversationMessageKind.Interview }
            ? InterviewJsonCodec.TryDecodeTurn(message.InterviewJson)
            : null;

        if (record is null)
        {
            return InterviewAnswerResolution.Fail(
                Results.BadRequest("'interviewAnswer.messageId' does not name an interview turn of this conversation."));
        }

        var question = record.FindQuestion(answer.QuestionKey);
        if (question is null)
        {
            return InterviewAnswerResolution.Fail(Results.BadRequest("'interviewAnswer.questionKey' is not a question of that interview."));
        }

        if (!string.IsNullOrWhiteSpace(answer.OptionKey))
        {
            var option = question.Options.FirstOrDefault(o => string.Equals(o.Key, answer.OptionKey, StringComparison.Ordinal));
            if (option is null)
            {
                return InterviewAnswerResolution.Fail(Results.BadRequest("'interviewAnswer.optionKey' is not an option of that question."));
            }

            if (question.Presentation == InterviewPresentation.Consent || option.ResolvesTo.WebResearch is not null)
            {
                var outcome = await conversationService
                    .MarkInterviewConsumedAsync(tenantId, userId, conversationId, messageId, option.Key, cancellationToken)
                    .ConfigureAwait(false);

                if (outcome == InterviewConsumeOutcome.AlreadyConsumed)
                {
                    return InterviewAnswerResolution.Fail(Results.Conflict("This authorization was already used — ask again."));
                }

                if (outcome == InterviewConsumeOutcome.NotFound)
                {
                    return InterviewAnswerResolution.Fail(Results.BadRequest("'interviewAnswer.messageId' does not name an interview turn of this conversation."));
                }
            }

            var hints = AskTurnHints.From(option.ResolvesTo);
            if (question.Presentation == InterviewPresentation.Consent && option.ResolvesTo.WebResearch is null)
            {
                hints = hints with { DeclinedWebResearch = true };
            }

            return new InterviewAnswerResolution(
                null,
                hints,
                option.ResolvesTo.RewrittenQuestion,
                InterviewJsonCodec.SerializeAnswer(messageId, question.Key, option.Key, freeText: false));
        }

        if (answer.FreeText is not true)
        {
            return InterviewAnswerResolution.Fail(
                Results.BadRequest("'interviewAnswer.optionKey' is required unless 'interviewAnswer.freeText' is true."));
        }

        if (!question.AllowFreeText)
        {
            return InterviewAnswerResolution.Fail(Results.BadRequest("That interview question takes one of its options, not free text."));
        }

        return new InterviewAnswerResolution(
            null,
            AskTurnHints.FreeText,
            typedQuestion,
            InterviewJsonCodec.SerializeAnswer(messageId, question.Key, null, freeText: true));
    }

    private sealed record InterviewAnswerResolution(
        IResult? Failure, AskTurnHints Hints, string EffectiveQuestion, string? YouInterviewJson)
    {
        public static InterviewAnswerResolution Fail(IResult failure) => new(failure, AskTurnHints.None, string.Empty, null);
    }

    private static object ToInterviewJson(InterviewTurn turn, bool answered) => new
    {
        prompt = turn.Prompt,
        questions = turn.Questions.Select(q => new
        {
            key = q.Key,
            prompt = q.Prompt,
            presentation = q.Presentation == InterviewPresentation.Consent ? "consent" : "choice",
            allowFreeText = q.AllowFreeText,
            options = q.Options.Select(o => new { key = o.Key, label = o.Label, hint = o.Hint }),
        }),
        answered,
    };

    private static object ToCitationJson(ReplyCitation citation) => new
    {
        n = citation.N,
        corpus = citation.Corpus,
        title = citation.Title,
        subtitle = citation.Subtitle,
        snippet = citation.Snippet,
        documentId = citation.DocumentId,
        contractId = citation.ContractId,
        page = citation.Page,
        section = citation.Section,
        previewUrl = citation.PreviewUrl,
        href = citation.Href,
        recordId = citation.RecordId,
    };

    private static object ToActionJson(CopilotAction action) => new
    {
        label = action.Label,
        href = action.Href,
        kind = action.Kind switch
        {
            CopilotActionKind.Navigate => "navigate",
            CopilotActionKind.Upload => "upload",
            CopilotActionKind.External => "external",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action.Kind, "Unknown CopilotActionKind."),
        },
    };

    /// <summary>Same PascalCase-member-to-lowercase-wire-literal mapping <see cref="ToWireRole"/>/
    /// <see cref="ToWireKind"/> already establish, extended to
    /// <c>Raffa.Chat.Application.Reply.ReplyKind</c> — its own
    /// <c>Raffa.Chat.Application.Reply.ReplyKindWireFormat.ToApiValue</c> already does exactly
    /// this; reused, not re-implemented.</summary>
    private static ConversationMessageKind ToMessageKind(ReplyKind kind) => kind switch
    {
        ReplyKind.Answer => ConversationMessageKind.Answer,
        ReplyKind.Abstain => ConversationMessageKind.Abstain,
        ReplyKind.Redirect => ConversationMessageKind.Redirect,
        ReplyKind.Refusal => ConversationMessageKind.Refusal,
        ReplyKind.Draft => ConversationMessageKind.Draft,
        ReplyKind.Interview => ConversationMessageKind.Interview,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown ReplyKind."),
    };
    /// <summary>Same "reject, don't clamp" convention as
    /// <c>PortfolioEndpointExtensions.TryParsePage</c>.</summary>
    /// <summary>`?archived=true` lists only archived chats, `?archived=false` only the ones in use
    /// (<see cref="ConversationService.ArchiveAfter"/>); absent lists both.</summary>
    private static bool TryParseArchived(IQueryCollection query, out ConversationArchiveFilter archive, out string error)
    {
        archive = ConversationArchiveFilter.All;
        error = string.Empty;

        if (!query.TryGetValue("archived", out var archivedValues))
        {
            return true;
        }

        if (!bool.TryParse(archivedValues.ToString(), out var archived))
        {
            error = "'archived' must be true or false.";
            return false;
        }

        archive = archived ? ConversationArchiveFilter.Archived : ConversationArchiveFilter.Active;
        return true;
    }

    private static bool TryParseTake(IQueryCollection query, out int take, out string error)
    {
        take = ConversationService.DefaultRecentLimit;
        error = string.Empty;

        if (!query.TryGetValue("take", out var takeValues))
        {
            return true;
        }

        if (!int.TryParse(takeValues.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out take)
            || take < 1)
        {
            error = "'take' must be a positive integer.";
            return false;
        }

        return true;
    }

    private static object ToSummaryResponse(ConversationSummaryResult summary) => new
    {
        id = summary.ConversationId.Value,
        title = summary.Title,
        customTitle = summary.CustomTitle,
        scopeContractId = summary.ScopeContractId?.Value,
        updatedAt = summary.UpdatedAt,
        archived = summary.Archived,
    };

    private static object ToDetailResponse(ConversationDetailResult detail) => new
    {
        id = detail.ConversationId.Value,
        title = detail.Title,
        customTitle = detail.CustomTitle,
        scopeContractId = detail.ScopeContractId?.Value,
        createdAt = detail.CreatedAt,
        updatedAt = detail.UpdatedAt,
        messages = detail.Messages.Select((message, index) => ToMessageResponse(message, index < detail.Messages.Count - 1)),
    };

    private static object ToMessageResponse(ConversationMessageResult message, bool hasLaterTurn) => new
    {
        id = message.MessageId.Value,
        role = ToWireRole(message.Role),
        kind = ToWireKind(message.Kind),
        markdown = message.Markdown,
        citations = ParseJsonArray(message.CitationsJson),
        actions = ParseJsonArray(message.ActionsJson),
        modelId = message.ModelId,
        promptVersion = message.PromptVersion,
        inputHash = message.InputHash,
        createdAt = message.CreatedAt,
        payload = string.IsNullOrWhiteSpace(message.PayloadJson) ? (JsonElement?)null : ParseJsonObject(message.PayloadJson),
        interview = ToPersistedInterviewJson(message, hasLaterTurn),
    };

    private static object? ToPersistedInterviewJson(ConversationMessageResult message, bool hasLaterTurn)
    {
        if (message.Role != ConversationRole.Raffa || message.Kind != ConversationMessageKind.Interview)
        {
            return null;
        }

        var record = InterviewJsonCodec.TryDecodeTurn(message.InterviewJson);
        return record is null ? null : ToInterviewJson(record.ToTurn(), hasLaterTurn || record.ConsumedAt is not null);
    }

    /// <summary>ADR-024 §6's wire literals — see <see cref="ConversationRole"/>'s own doc comment
    /// ("task E13/F05/US01/T02... owns mapping these PascalCase members onto the wire-format
    /// lowercase you/raffa literals").</summary>
    private static string ToWireRole(ConversationRole role) => role switch
    {
        ConversationRole.You => "you",
        ConversationRole.Raffa => "raffa",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown ConversationRole."),
    };

    /// <summary>Same wire-mapping rule as <see cref="ToWireRole"/>, for
    /// <see cref="ConversationMessageKind"/>'s own doc comment.</summary>
    private static string ToWireKind(ConversationMessageKind kind) => kind switch
    {
        ConversationMessageKind.Answer => "answer",
        ConversationMessageKind.Abstain => "abstain",
        ConversationMessageKind.Redirect => "redirect",
        ConversationMessageKind.Refusal => "refusal",
        ConversationMessageKind.Draft => "draft",
        ConversationMessageKind.Interview => "interview",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown ConversationMessageKind."),
    };

    /// <summary><see cref="ConversationMessage.CitationsJson"/>/<see cref="ConversationMessage.ActionsJson"/>
    /// are stored pre-serialized (<see cref="ConversationService"/> never touches their shape) —
    /// re-parsed here so the HTTP response carries real JSON arrays (AC-2: "citations, actions"),
    /// never a JSON string nested inside JSON. <see cref="JsonElement.Clone"/> detaches the result
    /// from the disposed <see cref="JsonDocument"/>, the same convention
    /// <c>Raffa.IntegrationTests.AskRaffaRagCrossTenantIsolationTests.ParseAsync</c> already
    /// uses on the test side of this same JSON.</summary>
    private static JsonElement ParseJsonArray(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>Same detach-from-the-document convention as <see cref="ParseJsonArray"/>, for the
    /// ADR-030 payload object.</summary>
    private static JsonElement ParseJsonObject(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// `POST /api/conversations` request body (AC-2: `{ scopeContractId? }`). A nested type so
    /// `Raffa.ArchitectureTests.DependencyDirectionTests.Host_must_not_contain_domain_types`
    /// never sees it — same reasoning as `ChatEndpointExtensions.ChatQueryRequest`'s own doc
    /// comment. <see cref="ScopeContractId"/> is a string, not a <see cref="Guid"/>: a malformed
    /// value must produce this file's own 400 message, not ASP.NET Core's generic model-binding
    /// failure for an un-parseable route/body <see cref="Guid"/>.
    /// </summary>
    public sealed record CreateConversationRequest(string? ScopeContractId = null);

    /// <summary>`PATCH /api/conversations/{id}` request body: `{ title }`, the chat's new name;
    /// blank or null clears it. Nested for the same reason as <see cref="CreateConversationRequest"/>.</summary>
    public sealed record RenameConversationRequest(string? Title = null);

    /// <summary>`POST /api/conversations/{id}/feedback` request body (ADR-030 D5):
    /// `{ messageId, answers: { what, frequency, importance } }`. Strings, not typed keys, for the
    /// same "this file's own 400 message" reason as <see cref="CreateConversationRequest"/>.</summary>
    public sealed record PostConversationFeedbackRequest(string? MessageId = null, FeedbackAnswersRequest? Answers = null);

    public sealed record FeedbackAnswersRequest(string? What = null, string? Frequency = null, string? Importance = null);

    /// <summary>
    /// `POST /api/conversations/{id}/messages` request body (ADR-024 §6: <c>{ question }</c>) — a
    /// nested type for the identical reason <see cref="CreateConversationRequest"/>'s own doc
    /// comment gives.
    /// </summary>
    /// <param name="WebResearch">ADR-032: the composer's web-search toggle was on when this
    /// question was sent. Ignored on an interview answer, which always runs its own resolution.</param>
    public sealed record PostConversationMessageRequest(
        string? Question, InterviewAnswerRequest? InterviewAnswer = null, bool? WebResearch = null);

    public sealed record InterviewAnswerRequest(string? MessageId, string? QuestionKey, string? OptionKey = null, bool? FreeText = null);
}
