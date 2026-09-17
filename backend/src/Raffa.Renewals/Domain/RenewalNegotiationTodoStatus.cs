namespace Raffa.Renewals.Domain;

/// <summary>
/// Lifecycle of one <see cref="RenewalNegotiationTodo"/> row (task E29/F01/US01/T01,
/// todo-entity-api; parent story us-01-todo-entity-api; wave w19 NW-85; ADR-028 w19 —see that
/// entity's own doc comment for the citation). A closed, real enum (not a free-form string, unlike
/// e.g. <c>Raffa.Documents.Contracts.Domain.Contract.Status</c>, which is extraction-sourced and
/// therefore open-vocabulary): this value only ever moves through
/// <c>Raffa.Renewals.Application.RenewalNegotiationTodoService</c>'s own reconciliation/tick logic,
/// never sourced from a document, so a closed set is honest rather than restrictive (Appendix C
/// rule 10) — same reasoning <see cref="RenewalActionStatus"/>'s own doc comment already gives for
/// its sibling three-state lifecycle.
/// </summary>
public enum RenewalNegotiationTodoStatus
{
    /// <summary>The negotiation point is currently ranked/grounded and not yet marked done — the
    /// state a row is created in, and the state it returns to when the same
    /// <c>point_key</c> reappears in a later upsert after having been <see cref="Superseded"/>
    /// (<c>RenewalNegotiationTodoService.UpsertAsync</c>'s own doc comment).</summary>
    Open,

    /// <summary>Procurement ticked this point done via the tick PUT
    /// (<c>RenewalNegotiationTodoService.SetDoneAsync</c>). Terminal and frozen: once a row is
    /// <see cref="Done"/>, a later upsert of the same <c>point_key</c> never un-ticks it back to
    /// <see cref="Open"/> and never re-supersedes it — the task's own idempotent-upsert rule
    /// ("never un-ticks Done").</summary>
    Done,

    /// <summary>The point was tracked for this contract but the most recent ranked set no longer
    /// includes its <c>point_key</c> — the negotiation point stopped being grounded/relevant
    /// (<c>RenewalNegotiationTodoService.UpsertAsync</c>'s own "vanished → Superseded" rule). Never
    /// applied to a <see cref="Done"/> row (see that enum member's own doc comment): a point the
    /// user already closed out stays closed, it is not silently relabelled when the AI stops
    /// surfacing it.</summary>
    Superseded,
}
