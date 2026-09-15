using Raffa.SharedKernel;

namespace Raffa.Chat.Domain.Conversations;

/// <summary>
/// One Ask Raffa conversation thread (task E13/F05/US01/T01, story us-01-conversations;
/// `inputs/requirements.md` R-CONV-01, §7; ADR-024). Server-side, per user and per workspace
/// (HITL D5) — resumable from any device, private to the user who started it. Owns zero or more
/// <see cref="ConversationMessage"/> rows.
/// </summary>
public sealed class Conversation : TenantScopedEntity
{
    /// <summary>
    /// Scopes this conversation to the caller who started it (R-CONV-01 "keyed by tenant +
    /// user"). A plain string, not an FK into <c>Raffa.Identity.Workspace</c> — the validated
    /// token subject (the <c>oid</c> claim, ADR-010; task E18/F02/US01/T01, ADR-010 w16 footer),
    /// resolved once per request by <c>Raffa.Api.Infrastructure.ICallerContext</c> and passed in
    /// unchanged. Opaque and case-sensitive, matched ordinally — never lower-cased
    /// (<c>CallerIdentity.cs:60-68</c> records that as deliberate): two rows differing only in
    /// case are two different callers, by design. A conversation created under the pre-w15
    /// <c>X-User-Id</c>/email posture is keyed by that email and is not reachable by any
    /// <c>oid</c> — left in place, never remapped, never deleted (ADR-001 w16 footer clause 1).
    /// Postgres Row-Level Security (this task's own migration)
    /// scopes by <see cref="Raffa.SharedKernel.TenantId"/> only — RLS has no notion of a
    /// second, per-user predicate — so "another user of the same workspace cannot list or read
    /// the conversation" (R-CONV-01 AC-1) is enforced in application code
    /// (<c>Application.Conversations.ConversationService</c> filters by this field on every
    /// read/write), the same belt-and-suspenders relationship ADR-009 already describes between
    /// RLS and the app-level <see cref="Raffa.SharedKernel.TenantId"/> filter.
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
    /// <c>Raffa.Documents.Contracts.Domain.Contract.SupplierId</c>'s own doc comment already
    /// documents, here doubly true because <c>Raffa.Chat</c> may not reference
    /// <c>Raffa.Documents.Contracts</c> at all.
    /// </summary>
    public EntityId? ScopeContractId { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Bumped every time a message is appended (never on read) — the recency ordering
    /// `GET /api/conversations` (a later task) sorts by, per R-CONV-02's "last N conversations".
    /// </summary>
    public required DateTimeOffset UpdatedAt { get; set; }
}
