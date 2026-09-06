using Contigo.Renewals.Domain;
using Contigo.SharedKernel;

namespace Contigo.Renewals.Application;

/// <summary>Small, immutable projection of one <see cref="RenewalAlert"/> row — what
/// <see cref="RenewalAlertService"/> returns to its callers instead of the mutable EF entity itself
/// (same "never leak the tracked entity across the service boundary" convention
/// <see cref="RenewalActionResult"/> already established for <see cref="RenewalAction"/>).</summary>
public sealed record RenewalAlertResult(
    EntityId ContractId,
    RenewalMilestoneKind Milestone,
    int ThresholdDays,
    DateOnly MilestoneDate,
    RenewalAlertStatus Status);

/// <summary>
/// Outcome of one <see cref="RenewalAlertService.RecomputeForContractAsync"/> call (parent story
/// us-01-threshold-scheduler AC-3) — counts only, not the full before/after row set: today's one
/// caller (<c>Contigo.Api.RenewalAlertRecomputeService</c>, itself called right after `PATCH
/// /api/contracts/{id}` durably commits a correction) has no HTTP contract yet that needs the
/// individual alerts back, and a count is enough to prove — and to log, the same way
/// <c>RenewalThresholdSchedulerHostedService</c> already logs an event count per tick — that a
/// correction actually changed something (or honestly changed nothing).
/// </summary>
public sealed record RenewalAlertRecomputeResult(EntityId ContractId, int ResolvedCount, int CreatedCount);
