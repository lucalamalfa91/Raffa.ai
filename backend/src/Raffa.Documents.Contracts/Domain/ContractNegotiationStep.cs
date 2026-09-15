using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Domain;

/// <summary>
/// One ticked step of Contract 360's negotiation tracker "4-step checklist" (task
/// E19/F03/US01/T01, NW-13; ADR-028 §D3; design oracle
/// <c>inputs/design/prototypes/raffa-v2/screens-v2.md:106-108</c>). <b>The row's presence is the
/// tick</b> — there is no <c>ticked</c> boolean column here. Unticking a step deletes its row (see
/// <see cref="Application.NegotiationStepService"/>'s own doc comment for why that is exactly what
/// makes the whole-set <c>PUT</c> idempotent, with no third state to reconcile).
///
/// <para>
/// <see cref="Step"/> stores the step's **name** as a closed enum (see
/// <see cref="NegotiationStep"/>'s own doc comment) — never an array index and never the
/// rendered label — the same "enum as string" convention
/// <c>Raffa.Quotes.Domain.NegotiationOutcome.LeversUsed</c> already establishes for a closed
/// vocabulary (<c>:112-116</c>), simplified here to a single value per row rather than a list. The
/// client's own store is a positional, unnamed <c>boolean[4]</c>
/// (<c>web/src/routes/contracts/contract360/negotiationStepsStore.ts:11,18,24,31</c>), and the
/// rendered labels live entirely client-side
/// (<c>web/src/routes/contracts/contract360/contract360ViewModel.ts:211-218</c>, two of the four
/// parameterized with the supplier name / cancellation deadline) — an index key would silently
/// re-point every tick the day a step is inserted, and a stored label would freeze a fact that
/// later changes (ADR-001 w16 clause 3; w14 clause 3).
/// </para>
///
/// <para>
/// Unique on (<see cref="TenantScopedEntity.TenantId"/>, <see cref="ContractId"/>,
/// <see cref="Step"/>) — see
/// <see cref="Infrastructure.Configurations.ContractNegotiationStepConfiguration"/>'s own index —
/// so at most one row per step per contract per tenant. Ticks are **per contract**, not per
/// renewal cycle (ADR-001 w16 clause 3; ADR-028 §D3 assumption 3): a per-cycle key would add one
/// column to this unique constraint later, if that capability is ever built — it is out of scope
/// for V1.
/// </para>
///
/// <para>
/// Deliberately no foreign key to <see cref="Contract"/>, even though both types live in this same
/// module — the same "no FK across aggregates" treatment
/// <c>Raffa.Renewals.Infrastructure.Configurations.RenewalActionConfiguration</c> already gives its
/// own <c>ContractId</c> column (there, cross-module; here, cross-aggregate within one module, same
/// reasoning either way: this table's own existence and tenant isolation never depend on the
/// referenced contract row). <see cref="Application.NegotiationStepService"/> checks
/// <see cref="Contract"/> existence explicitly instead, the same "contract exists but has nothing
/// recorded yet" vs. "no such contract" distinction
/// <see cref="Application.ContractCorrectionHistoryQueryService"/> already draws for its own sibling
/// read.
/// </para>
/// </summary>
public sealed class ContractNegotiationStep : TenantScopedEntity
{
    /// <summary>The contract this tick belongs to — the route's <c>{id}</c> segment on both
    /// <c>GET</c>/<c>PUT /api/contracts/{id}/negotiation-steps</c>.</summary>
    public required EntityId ContractId { get; set; }

    /// <summary>Which of the four canonical checklist steps this row ticks — see
    /// <see cref="NegotiationStep"/>'s own doc comment for the closed vocabulary and its
    /// source. Stored by name (see the type doc comment above); never an ordinal, never the
    /// rendered label.</summary>
    public required NegotiationStep Step { get; set; }

    /// <summary>When this step was ticked (caller-request-time, via <c>IClock</c> — the same "no
    /// hidden clock" convention every other timestamped write in this codebase follows, e.g.
    /// <c>Raffa.Renewals.Domain.RenewalAction.UpdatedAt</c>). A step that stays ticked across a
    /// repeated whole-set <c>PUT</c> keeps its original instant — <see cref="Application
    /// .NegotiationStepService.SetAsync"/> never touches a row already present in the requested
    /// set.</summary>
    public required DateTimeOffset TickedAt { get; set; }
}

/// <summary>
/// The four canonical steps of Contract 360's negotiation tracker checklist — the design oracle's
/// own order and wording (<c>inputs/design/prototypes/raffa-v2/screens-v2.md:106-108</c>: "4-step
/// checklist (Notify · Request revised pricing · Counter with the market benchmark · Sign or send
/// non-renewal notice)"), ratified as the closed, exhaustive vocabulary by ADR-001 w16 clause 3 and
/// OQ-w16-006's product-half ruling: <b>four, no more — a fifth is a product change, not an
/// implementation detail.</b>
///
/// <para>
/// These member names are both the C# identifiers and — via
/// <see cref="Infrastructure.Configurations.ContractNegotiationStepConfiguration"/>'s
/// enum-as-string column mapping — the exact wire keys
/// <c>GET</c>/<c>PUT /api/contracts/{id}/negotiation-steps</c> return and accept. The rendered
/// label (two of the four parameterized with the supplier name / the cancellation deadline,
/// <c>web/src/routes/contracts/contract360/contract360ViewModel.ts:211-218</c>) stays entirely
/// client-side and is never persisted here (ADR-001 w16 clause 3).
/// </para>
/// </summary>
public enum NegotiationStep
{
    Notify,
    RequestRevisedPricing,
    CounterWithMarketBenchmark,
    SignOrSendNonRenewalNotice,
}
