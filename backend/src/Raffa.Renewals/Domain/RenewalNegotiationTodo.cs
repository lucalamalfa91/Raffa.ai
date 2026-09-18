using Raffa.SharedKernel;

namespace Raffa.Renewals.Domain;

/// <summary>
/// The persisted negotiation TODO row task E29/F01/US01/T01 (todo-entity-api; parent story
/// us-01-todo-entity-api AC-1; wave w19 NW-85; ADR-028 w19 amendment, ADR-009 w19 amendment,
/// ADR-011 w19 amendment, ADR-003 w19 amendment — see <c>reports/architecture/waves/w19.md</c>'s
/// own NW-85 row: "new entity <c>renewal_negotiation_todo</c> keyed
/// <c>(tenant_id, contract_id, point_key)</c>... Do not reuse <c>RenewalAction</c> or
/// <c>ContractNegotiationStep</c>") adds. Ask ranks a contract's negotiation points
/// (<c>Raffa.Insights.Application.NegotiationPointRanker</c>, epic-31) and the host upserts the
/// whole ranked set here, in-process, after ranking and before the answer is returned
/// (<c>Raffa.Api.AskCopilotService</c>, epic-29/feature-02) — this table is what makes
/// <c>/renewals?select={contractId}</c> true a request later, from any browser.
///
/// <para>
/// <b>Deliberately not <see cref="RenewalAction"/> and not <c>ContractNegotiationStep</c></b> (the
/// task's own "Do not touch" list): <see cref="RenewalAction"/> is a single owner/status/action
/// projection per contract (at most one row); <c>ContractNegotiationStep</c> (owned by
/// <c>Raffa.Documents.Contracts</c>, ADR-028 §D3) is four fixed, product-named ticks. This entity is
/// neither — it is an open-ended, AI-ranked list of negotiation points per contract, each carrying
/// its own topic/target/rationale/evidence, keyed by a stable <see cref="PointKey"/> the ranker
/// assigns (not an array index, not a database identity column — the same "closed vocabulary key,
/// never an ordinal" convention ADR-028 §D3 already establishes for
/// <c>ContractNegotiationStep.Step</c>).
/// </para>
///
/// <para>
/// <b>Keyed by (<see cref="TenantScopedEntity.TenantId"/>, <see cref="ContractId"/>,
/// <see cref="PointKey"/>)</b> — see
/// <see cref="Raffa.Renewals.Infrastructure.Configurations.RenewalNegotiationTodoConfiguration"/>'s
/// own unique index on that triple. <see cref="Raffa.Renewals.Application.RenewalNegotiationTodoService.UpsertAsync"/>
/// is the only writer of that triple's content; <c>SetDoneAsync</c> only ever flips
/// <see cref="Status"/> on a row that already exists — it never creates one (client-architect's
/// "never invent a point").
/// </para>
///
/// <para>
/// Deliberately does not reference <c>Raffa.Documents.Contracts.Domain.Contract</c>: ADR-002's
/// dependency-direction rule only allows this module to reference <c>Raffa.SharedKernel</c> (plus
/// <c>Raffa.Benchmark</c>) — the same reason <see cref="RenewalAction"/>/<see cref="RenewalAlert"/>
/// are their own small shapes rather than the real entity, and the same reason the ranked-point
/// facts this row persists are computed upstream, in <c>Raffa.Insights</c>/<c>Raffa.Api</c>, and
/// hand to this module as plain values.
/// </para>
/// </summary>
public sealed class RenewalNegotiationTodo : TenantScopedEntity
{
    /// <summary>Correlates to the same id <c>GET /api/renewals</c> returns as
    /// <c>RenewalPipelineItem.ContractId</c> — the route's <c>{id}</c> segment, same meaning as
    /// <see cref="RenewalAction.ContractId"/>.</summary>
    public required EntityId ContractId { get; set; }

    /// <summary>Stable identity for one negotiation point on this contract — the ranker's own key
    /// (e.g. one of the six canonical categories NW-96 orders: above-band price, uncapped/high
    /// liability, auto-renew+short notice, SLA/credits, term/volume, payment terms), never a
    /// database identity column and never an array index. Stability across repeat asks is what
    /// makes <c>UpsertAsync</c>'s reconciliation idempotent (same key updates the existing row
    /// instead of creating a duplicate) — see
    /// <see cref="Raffa.Renewals.Infrastructure.Configurations.RenewalNegotiationTodoConfiguration"/>'s
    /// own unique index on (<see cref="TenantScopedEntity.TenantId"/>, <see cref="ContractId"/>,
    /// <see cref="PointKey"/>).</summary>
    public required string PointKey { get; set; }

    /// <summary>Human-readable label for the point (e.g. "Above-band unit price"). Set once, at
    /// creation, from the same closed vocabulary <see cref="PointKey"/> names — unlike
    /// <see cref="Current"/>/<see cref="Target"/>/<see cref="Rationale"/>/<see cref="Rank"/>, a
    /// repeat <c>UpsertAsync</c> call for the same <see cref="PointKey"/> does not rewrite this
    /// field (the task's own idempotent-upsert rule names only "current/target/rationale/rank" as
    /// what a repeat upsert updates); a point's category-derived label does not need to track a
    /// changing market fact the way its assessment values do.</summary>
    public required string Topic { get; set; }

    /// <summary>This point's priority order within the contract's ranked set (lower = higher
    /// priority — <c>NegotiationPointRanker</c>'s own ordering, NW-96: above-band price first,
    /// payment terms last). Refreshed on every <c>UpsertAsync</c> for an existing, non-<see
    /// cref="RenewalNegotiationTodoStatus.Done"/> row — a re-ask can legitimately re-order points as
    /// new facts/benchmarks change what is most urgent.</summary>
    public required int Rank { get; set; }

    /// <summary>What the contract has today for this point (e.g. "Auto-renews with 90-day notice,
    /// no cap on the increase") — free text, refreshed on every non-<see
    /// cref="RenewalNegotiationTodoStatus.Done"/> upsert.</summary>
    public required string Current { get; set; }

    /// <summary>What Procurement should ask for (e.g. "Cap the increase at 5%, 30-day notice") —
    /// free text, same refresh rule as <see cref="Current"/>.</summary>
    public required string Target { get; set; }

    /// <summary>Why this point matters / why the target is achievable — free text, grounded in the
    /// same evidence <see cref="CitationKeys"/> names. Same refresh rule as <see cref="Current"/>:
    /// refreshed together with the facts it explains, so a stored rationale never outlives the
    /// figures it justified.</summary>
    public required string Rationale { get; set; }

    /// <summary>
    /// The evidence this point is grounded in — <c>Raffa.Insights.Contracts.InsightsCitationKeys</c>'
    /// three shapes (<c>fact:...</c>/<c>market:...</c>/<c>calc:...</c>), copied verbatim from the
    /// ranker's own output. Refreshed alongside <see cref="Current"/>/<see cref="Target"/>/
    /// <see cref="Rationale"/> on every non-<see cref="RenewalNegotiationTodoStatus.Done"/> upsert —
    /// documented assumption (the task's own field list names only "current/target/rationale/rank"
    /// verbatim, not this field): the citations are the evidence *for* those three facts, so
    /// refreshing the facts while leaving stale citations behind would let a claim silently outlive
    /// the evidence that grounded it — the opposite of NW-96's "only grounded points" rule. See
    /// <see cref="Raffa.Renewals.Application.RenewalNegotiationTodoService.UpsertAsync"/>'s own doc
    /// comment.
    /// </summary>
    public required IReadOnlyList<string> CitationKeys { get; set; }

    /// <summary>Where this row came from. Always <c>"ask"</c> today
    /// (<see cref="Raffa.Renewals.Application.RenewalNegotiationTodoService.AskSource"/>) — the
    /// task's own field list names this a persisted column rather than an implicit constant,
    /// honestly leaving room for a future non-Ask writer (e.g. a manual add) without a schema
    /// change; no task builds one yet.</summary>
    public required string Source { get; set; }

    /// <summary>See <see cref="RenewalNegotiationTodoStatus"/>'s own doc comment.</summary>
    public required RenewalNegotiationTodoStatus Status { get; set; }

    /// <summary>When this row was first written (caller-request-time, via <c>IClock</c> — not a
    /// database default, same "no hidden clock" convention <see cref="RenewalAlert.CreatedAt"/>
    /// already follows). Never rewritten by a later upsert of the same <see cref="PointKey"/>.</summary>
    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>When this row last changed — set on every create, content refresh, supersede, and
    /// tick. Same "no hidden clock" convention as <see cref="CreatedAt"/>.</summary>
    public required DateTimeOffset UpdatedAt { get; set; }
}
