using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Contigo.Chat.Application.Conversations;
using Contigo.Chat.Domain.Conversations;
using Contigo.SharedKernel;

namespace Contigo.Api;

/// <summary>
/// Maps `GET /api/conversations`, `POST /api/conversations` and `GET /api/conversations/{id}`
/// (product spec §7; ADR-024 "Conversations (D5)"; story us-01-conversations AC-2/AC-3, task
/// E13/F05/US01/T02). Thin composition per ADR-002 — the actual decisions (tenant + user scoping,
/// title derivation, RLS-backstopped isolation) are made by
/// <see cref="ConversationService"/> (task E13/F05/US01/T01); this file only translates HTTP
/// &lt;-&gt; those calls and resolves the one thing <see cref="ConversationService"/> cannot:
/// which HTTP concept ("this request's tenant", "this request's caller") maps onto its own
/// <c>tenantId</c>/<c>userId</c> parameters.
///
/// <para>
/// <b>Caller identity (AC-3, ADR-022/ADR-010, OQ-askv2-005)</b>: <see cref="TryResolveUserId"/>
/// prefers the token subject of an already-authenticated <see cref="ClaimsPrincipal"/> — the
/// ADR-010 end state — and falls back to the required <c>X-User-Id</c> header (the MSAL account
/// username, per OQ-askv2-005's assumption in force) when no principal is authenticated. Nothing
/// in this host wires <c>AddAuthentication</c>/<c>AddJwtBearer</c> yet (same gap
/// <c>Contigo.Identity.Workspace.Domain.WorkspacePrincipalAuthorization</c>'s own doc comment
/// already documents for `GET /api/audit`), so today <c>HttpContext.User</c> is always the default
/// anonymous principal and every real caller takes the header branch — this dual-branch shape
/// exists so the ADR-010 host-auth task can start minting authenticated principals without this
/// file changing at all. <b>The header is never validated against a real identity provider</b> —
/// it scopes reads/writes and nothing else; do not trust it for authorization (see
/// `backend/README.md`'s "Interim auth" section). A missing header with no authenticated principal
/// is a 400, the same "reject, don't guess" shape as a missing/invalid `X-Tenant-Id`.
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
/// `POST /api/conversations/{id}/messages` is deliberately <b>not</b> mapped here — task F06/T01
/// (phase 3) adds it to this same file once the Ask engine can produce a turn to persist (see
/// <see cref="AppendConversationMessageRequest"/>'s own doc comment: this module only persists
/// what a caller already computed).
/// </para>
/// </summary>
public static class ConversationsEndpointExtensions
{
    private const string UserIdHeaderName = "X-User-Id";

    public static IEndpointRouteBuilder MapConversationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/conversations", GetConversationsAsync);
        endpoints.MapPost("/api/conversations", PostConversationAsync);
        endpoints.MapGet("/api/conversations/{id}", GetConversationAsync);
        return endpoints;
    }

    /// <summary>`GET /api/conversations` (R-CONV-02: "the rail shows the user's last 5
    /// conversations") — optional `?take=` overrides <see cref="ConversationService.DefaultRecentLimit"/>.
    /// Response is a bare array, not a `{ items, totalCount }` envelope — there is no paging
    /// concept for "my last N conversations".</summary>
    private static async Task<IResult> GetConversationsAsync(
        HttpRequest request, ConversationService conversationService, CancellationToken cancellationToken)
    {
        if (!TryResolveTenant(request, out var tenantId, out var tenantError))
        {
            return Results.BadRequest(tenantError);
        }

        if (!TryResolveUserId(request, out var userId, out var userError))
        {
            return Results.BadRequest(userError);
        }

        if (!TryParseTake(request.Query, out var take, out var takeError))
        {
            return Results.BadRequest(takeError);
        }

        var conversations = await conversationService
            .ListRecentAsync(tenantId, userId, take, cancellationToken)
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
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenant(httpRequest, out var tenantId, out var tenantError))
        {
            return Results.BadRequest(tenantError);
        }

        if (!TryResolveUserId(httpRequest, out var userId, out var userError))
        {
            return Results.BadRequest(userError);
        }

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
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenant(request, out var tenantId, out var tenantError))
        {
            return Results.BadRequest(tenantError);
        }

        if (!TryResolveUserId(request, out var userId, out var userError))
        {
            return Results.BadRequest(userError);
        }

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

    private static bool TryResolveTenant(HttpRequest request, out TenantId tenantId, out string error)
    {
        tenantId = default;
        error = string.Empty;

        if (!request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeaderValues)
            || !Guid.TryParse(tenantHeaderValues.ToString(), out var tenantGuid))
        {
            error = "A valid 'X-Tenant-Id' header (a GUID) is required.";
            return false;
        }

        tenantId = new TenantId(tenantGuid);
        return true;
    }

    /// <summary>See the type doc comment's "Caller identity" section.</summary>
    private static bool TryResolveUserId(HttpRequest request, out string userId, out string error)
    {
        userId = string.Empty;
        error = string.Empty;

        var principal = request.HttpContext.User;
        if (principal.Identity is { IsAuthenticated: true })
        {
            var subject = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? principal.FindFirst("sub")?.Value;
            if (!string.IsNullOrWhiteSpace(subject))
            {
                userId = subject;
                return true;
            }
        }

        if (request.Headers.TryGetValue(UserIdHeaderName, out var userIdValues)
            && !string.IsNullOrWhiteSpace(userIdValues.ToString()))
        {
            userId = userIdValues.ToString();
            return true;
        }

        error = $"A valid '{UserIdHeaderName}' header is required (non-authoritative until the " +
                "API JWT lands — ADR-022/OQ-askv2-005).";
        return false;
    }

    /// <summary>Same "reject, don't clamp" convention as
    /// <c>PortfolioEndpointExtensions.TryParsePage</c>.</summary>
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
        scopeContractId = summary.ScopeContractId?.Value,
        updatedAt = summary.UpdatedAt,
    };

    private static object ToDetailResponse(ConversationDetailResult detail) => new
    {
        id = detail.ConversationId.Value,
        title = detail.Title,
        scopeContractId = detail.ScopeContractId?.Value,
        createdAt = detail.CreatedAt,
        updatedAt = detail.UpdatedAt,
        messages = detail.Messages.Select(ToMessageResponse),
    };

    private static object ToMessageResponse(ConversationMessageResult message) => new
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
    };

    /// <summary>ADR-024 §6's wire literals — see <see cref="ConversationRole"/>'s own doc comment
    /// ("task E13/F05/US01/T02... owns mapping these PascalCase members onto the wire-format
    /// lowercase you/contigo literals").</summary>
    private static string ToWireRole(ConversationRole role) => role switch
    {
        ConversationRole.You => "you",
        ConversationRole.Contigo => "contigo",
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
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown ConversationMessageKind."),
    };

    /// <summary><see cref="ConversationMessage.CitationsJson"/>/<see cref="ConversationMessage.ActionsJson"/>
    /// are stored pre-serialized (<see cref="ConversationService"/> never touches their shape) —
    /// re-parsed here so the HTTP response carries real JSON arrays (AC-2: "citations, actions"),
    /// never a JSON string nested inside JSON. <see cref="JsonElement.Clone"/> detaches the result
    /// from the disposed <see cref="JsonDocument"/>, the same convention
    /// <c>Contigo.IntegrationTests.AskContigoRagCrossTenantIsolationTests.ParseAsync</c> already
    /// uses on the test side of this same JSON.</summary>
    private static JsonElement ParseJsonArray(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// `POST /api/conversations` request body (AC-2: `{ scopeContractId? }`). A nested type so
    /// `Contigo.ArchitectureTests.DependencyDirectionTests.Host_must_not_contain_domain_types`
    /// never sees it — same reasoning as `ChatEndpointExtensions.ChatQueryRequest`'s own doc
    /// comment. <see cref="ScopeContractId"/> is a string, not a <see cref="Guid"/>: a malformed
    /// value must produce this file's own 400 message, not ASP.NET Core's generic model-binding
    /// failure for an un-parseable route/body <see cref="Guid"/>.
    /// </summary>
    public sealed record CreateConversationRequest(string? ScopeContractId = null);
}
