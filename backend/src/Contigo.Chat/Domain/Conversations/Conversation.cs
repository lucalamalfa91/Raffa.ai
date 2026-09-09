using Contigo.SharedKernel;

namespace Contigo.Chat.Domain.Conversations;

/// <summary>
/// One Ask Contigo conversation thread (task E13/F05/US01/T01, story us-01-conversations;
/// `inputs/requirements.md` R-CONV-01, §7; ADR-024). Server-side, per user and per workspace
/// (HITL D5) — resumable from any device, private to the user who started it. Owns zero or more
/// <see cref="ConversationMessage"/> rows.
/// </summary>
public sealed class Conversation : TenantScopedEntity
{
    /// <summary>
    /// Scopes this conversation to the caller who started it (R-CONV-01 "keyed by tenant +
    /// user"). A plain string, not an FK into <c>Contigo.Identity.Workspace</c> — under the
    /// ADR-022 posture this is the <c>X-User-Id</c> header (MSAL account username),
    /// non-authoritative until the task that lands the API JWT (ADR-010) replaces it with the
    /// token subject (OQ-askv2-005). Postgres Row-Level Security (this task's own migration)
    /// scopes by <see cref="Contigo.SharedKernel.TenantId"/> only — RLS has no notion of a
    /// second, per-user predicate — so "another user of the same workspace cannot list or read
    /// the conversation" (R-CONV-01 AC-1) is enforced in application code
    /// (<c>Application.Conversations.ConversationService</c> filters by this field on every
    /// read/write), the same belt-and-suspenders relationship ADR-009 already describes between
    /// RLS and the app-level <see cref="Contigo.SharedKernel.TenantId"/> filter.
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// The conversation's display title — derived from the first question (R-CONV-01: "A
    /// conversation has a title (first question, ≤ 48 chars)"), truncated by
    /// <c>ConversationService</c>. Starts as <c>ConversationService.DefaultTitle</c> ("New chat")
    /// until the first <see cref="ConversationRole.You"/> message lands.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// The contract this conversation was opened from (Contract 360 "Ask about it" → new chat
    /// scoped to that contract), or <see langword="null"/> for a conversation started from the
    /// global Ask bar. Cross-module reference by id only — deliberately no FK, the same "a
    /// physical constraint would cross a bounded-context boundary (ADR-002)" rule
    /// <c>Contigo.Documents.Contracts.Domain.Contract.SupplierId</c>'s own doc comment already
    /// documents, here doubly true because <c>Contigo.Chat</c> may not reference
    /// <c>Contigo.Documents.Contracts</c> at all.
    /// </summary>
    public EntityId? ScopeContractId { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Bumped every time a message is appended (never on read) — the recency ordering
    /// `GET /api/conversations` (a later task) sorts by, per R-CONV-02's "last N conversations".
    /// </summary>
    public required DateTimeOffset UpdatedAt { get; set; }
}
