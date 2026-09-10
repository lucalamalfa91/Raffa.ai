using Raffa.Renewals.Domain;
using Raffa.Renewals.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Renewals.Application;

/// <summary>
/// Implements task E03/F02/US01/T02 (the wave-spec's <c>renewal-alerts</c> artifact; parent story
/// us-01-threshold-scheduler AC-2/AC-3): turns a raised <see cref="RenewalApproachingEvent"/> into a
/// durable, de-duplicated <see cref="RenewalAlert"/> row (AC-2's own "creating alerts" half, product
/// spec Appendix B), and recomputes a contract's alerts when its renewal terms are corrected (AC-3).
///
/// <para>
/// <b>Creation (<see cref="CreateFromEventsAsync"/>)</b>: <see cref="RenewalThresholdScheduler"/>'s
/// own doc comment draws the line this method stands on — the scheduler raises an event on every
/// *exact* threshold match, once per configured day-count, with "no persisted 'already alerted'
/// state" of its own; de-duplicating repeated matches into one durable alert per (tenant, contract,
/// milestone, thresholdDays) is this method's job, not the scheduler's. Called once per scheduler
/// tick (<c>Raffa.Worker.Scheduling.RenewalThresholdSchedulerHostedService</c>, immediately after
/// <see cref="RenewalThresholdScheduler.EvaluateThresholdsAsync"/> returns) and again, indirectly,
/// from <see cref="RecomputeForContractAsync"/> below.
/// </para>
///
/// <para>
/// <b>Recompute (<see cref="RecomputeForContractAsync"/>)</b>: AC-3, "Scheduler recomputes when a
/// contract/term is corrected". Re-derives the contract's current renewal date/cancellation deadline
/// via <see cref="RenewalEngine.Calculate"/> and resolves any <see cref="RenewalAlertStatus.Active"/>
/// alert whose own <see cref="RenewalAlert.MilestoneDate"/> no longer matches — a correction that
/// pushed the real date away makes the existing alert stale, and Appendix C rule 10 forbids leaving a
/// now-known-wrong signal live just because nothing told it otherwise. It then re-runs
/// <see cref="RenewalThresholdScheduler.EvaluateThresholdsAsync"/> for this one contract against the
/// corrected terms — the literal reading of "scheduler recomputes" — so a correction that lands a
/// contract exactly on a configured threshold *today* raises the same <c>renewal.approaching</c>
/// event (and durable audit entry) a scheduled tick would raise tomorrow, not a parallel or
/// differently-shaped signal. Both halves share this class's own <see cref="CreateFromEventsAsync"/>
/// de-dup path, so a correction can never create a second alert for a threshold the contract (still,
/// or again) matches.
/// </para>
///
/// <para>
/// Owns its own tenant scope on every public method (<see cref="ITenantContext.BeginScope"/>) —
/// same convention every other Application-layer service in this module follows
/// (<see cref="RenewalActionService"/>, <see cref="RenewalThresholdScheduler"/>); safe to call from
/// another caller that already holds a scope for the same tenant, since
/// <see cref="ITenantContext.BeginScope"/> nests and restores on dispose (that type's own doc
/// comment) — <c>Raffa.Api.RenewalAlertRecomputeService</c> relies on exactly that when it opens
/// its own scope to re-read the just-corrected <c>Contract</c> row before calling
/// <see cref="RecomputeForContractAsync"/>.
/// </para>
///
/// <para>
/// Depends only on this module's own types plus <see cref="Raffa.SharedKernel"/> — ADR-002 forbids
/// <c>Raffa.Renewals</c> from referencing <c>Raffa.Documents.Contracts</c> at all (the same rule
/// <see cref="ContractRenewalTerms"/>'s own doc comment cites), so this service takes already-mapped
/// <see cref="ContractRenewalTerms"/> the same way <see cref="RenewalEngine"/> itself does; mapping a
/// real, corrected <c>Contract</c> row onto that shape can only happen in <c>Raffa.Api</c>, "the one
/// project allowed to reference every module".
/// </para>
/// </summary>
public sealed class RenewalAlertService(
    RenewalsDbContext dbContext,
    ITenantContext tenantContext,
    IClock clock,
    IAuditWriter auditWriter,
    RenewalEngine renewalEngine,
    RenewalThresholdScheduler thresholdScheduler)
{
    /// <summary><see cref="AuditEntry.Action"/> for every newly created, non-duplicate alert —
    /// past-tense, matching this codebase's established convention
    /// (<c>ContractCorrectionService</c>'s <c>"contract.corrected"</c>,
    /// <c>RenewalThresholdScheduler</c>'s <c>"renewal.approaching"</c>).</summary>
    public const string AuditCreatedAction = "renewal.alert_created";

    /// <summary><see cref="AuditEntry.Action"/> for every alert a correction supersedes (see the
    /// type doc comment's "Recompute" section).</summary>
    public const string AuditResolvedAction = "renewal.alert_resolved";

    /// <summary><see cref="AuditEntry.ResourceType"/> — lowercase snake_case, matching this table's
    /// own name (<see cref="Infrastructure.Configurations.RenewalAlertConfiguration"/>) and this
    /// codebase's general "resource type mirrors the table/concept, not the C# type name"
    /// convention (<c>RenewalActionService</c>'s own <c>"renewal"</c>).</summary>
    private const string AuditResourceType = "renewal_alert";

    /// <summary>Actor recorded on every audit entry this service writes — there is no human
    /// operator behind either an automated scheduler tick or an automated correction-triggered
    /// recompute (same reasoning as <see cref="RenewalThresholdScheduler.SchedulerActor"/>, kept as
    /// its own distinct constant since this is a different component writing a different, though
    /// related, fact).</summary>
    public const string AlertServiceActor = "system:renewal-alert-service";

    /// <summary>
    /// De-duplicates <paramref name="events"/> against every currently-<see cref="RenewalAlertStatus.Active"/>
    /// alert for each event's (contract, milestone, thresholdDays) and persists exactly one
    /// <see cref="RenewalAlert"/> row per genuinely new match — see the type doc comment's
    /// "Creation" section. Writing zero, one, or many events is all valid (a scheduler tick's own
    /// per-tenant batch can raise any number, including zero); an empty <paramref name="events"/>
    /// list is a no-op, not an error.
    /// </summary>
    public async Task<IReadOnlyList<RenewalAlertResult>> CreateFromEventsAsync(
        TenantId tenantId,
        IReadOnlyList<RenewalApproachingEvent> events,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0)
        {
            return [];
        }

        using var _ = tenantContext.BeginScope(tenantId);

        var created = await CreateDedupedAsync(tenantId, events, cancellationToken).ConfigureAwait(false);
        return created.Select(ToResult).ToList();
    }

    /// <summary>
    /// AC-3: re-derives <paramref name="terms"/>'s current renewal date/cancellation deadline,
    /// resolves any <see cref="RenewalAlertStatus.Active"/> alert this contract holds that no longer
    /// matches, then re-runs the threshold check for "today" against the corrected terms and
    /// persists any newly-crossed match — see the type doc comment's "Recompute" section for why
    /// both halves exist and how they share <see cref="CreateFromEventsAsync"/>'s own de-dup path.
    /// </summary>
    public async Task<RenewalAlertRecomputeResult> RecomputeForContractAsync(
        TenantId tenantId,
        ContractRenewalTerms terms,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(terms);

        using var _ = tenantContext.BeginScope(tenantId);

        var calculation = renewalEngine.Calculate(terms);

        var activeAlerts = await dbContext.RenewalAlerts
            .Where(a => a.TenantId == tenantId && a.ContractId == terms.ContractId
                && a.Status == RenewalAlertStatus.Active)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var now = clock.UtcNow;
        var resolved = new List<RenewalAlert>();
        foreach (var alert in activeAlerts)
        {
            var currentMilestoneDate = alert.Milestone == RenewalMilestoneKind.RenewalDate
                ? calculation.RenewalDate
                : calculation.CancellationDeadline;

            // Still matches (including "still null" turning into "still a real date" never happens
            // here, since an Active alert always carries a real MilestoneDate) -> leave it alone.
            if (currentMilestoneDate == alert.MilestoneDate)
            {
                continue;
            }

            alert.Status = RenewalAlertStatus.Resolved;
            alert.UpdatedAt = now;
            resolved.Add(alert);
        }

        if (resolved.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            foreach (var alert in resolved)
            {
                await auditWriter.WriteAsync(ToResolvedAuditEntry(tenantId, alert, now), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        // Same threshold check a scheduled tick would run for this one contract, against the
        // corrected terms (type doc comment: "the literal reading of 'scheduler recomputes'").
        // EvaluateThresholdsAsync opens/closes its own nested tenant scope internally -- safe, see
        // the type doc comment.
        var freshEvents = await thresholdScheduler
            .EvaluateThresholdsAsync(tenantId, [terms], cancellationToken)
            .ConfigureAwait(false);

        var created = await CreateDedupedAsync(tenantId, freshEvents, cancellationToken).ConfigureAwait(false);

        return new RenewalAlertRecomputeResult(terms.ContractId, resolved.Count, created.Count);
    }

    /// <summary>
    /// Shared de-dup + persist step for both public methods above: for each raised event, skips it
    /// if an <see cref="RenewalAlertStatus.Active"/> alert already exists for the same (tenant,
    /// contract, milestone, thresholdDays); otherwise stages a new <see cref="RenewalAlertStatus.Active"/>
    /// row. Persists and audits everything staged in one batch (one <see cref="AuditCreatedAction"/>
    /// entry per created row) rather than one round trip per event — the same "stage in a loop, save
    /// once" shape <c>ContractCorrectionService.CorrectAsync</c> already uses for its own
    /// per-field <c>CorrectionHistory</c> rows.
    /// </summary>
    private async Task<List<RenewalAlert>> CreateDedupedAsync(
        TenantId tenantId, IReadOnlyList<RenewalApproachingEvent> events, CancellationToken cancellationToken)
    {
        var created = new List<RenewalAlert>();

        foreach (var raised in events)
        {
            var alreadyActive = await dbContext.RenewalAlerts.AnyAsync(
                a => a.TenantId == tenantId
                    && a.ContractId == raised.ContractId
                    && a.Milestone == raised.Milestone
                    && a.ThresholdDays == raised.ThresholdDays
                    && a.Status == RenewalAlertStatus.Active,
                cancellationToken).ConfigureAwait(false);

            if (alreadyActive)
            {
                continue;
            }

            created.Add(new RenewalAlert
            {
                TenantId = tenantId,
                ContractId = raised.ContractId,
                Milestone = raised.Milestone,
                ThresholdDays = raised.ThresholdDays,
                MilestoneDate = raised.MilestoneDate,
                Status = RenewalAlertStatus.Active,
                CreatedAt = raised.OccurredAt,
                UpdatedAt = raised.OccurredAt,
            });
        }

        if (created.Count == 0)
        {
            return created;
        }

        dbContext.RenewalAlerts.AddRange(created);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var alert in created)
        {
            await auditWriter.WriteAsync(ToCreatedAuditEntry(alert), cancellationToken).ConfigureAwait(false);
        }

        return created;
    }

    private static RenewalAlertResult ToResult(RenewalAlert alert) => new(
        alert.ContractId, alert.Milestone, alert.ThresholdDays, alert.MilestoneDate, alert.Status);

    private static AuditEntry ToCreatedAuditEntry(RenewalAlert alert) => new(
        alert.TenantId,
        Actor: AlertServiceActor,
        Action: AuditCreatedAction,
        ResourceType: AuditResourceType,
        ResourceId: alert.ContractId.ToString(),
        Timestamp: alert.CreatedAt,
        Detail: $"milestone={alert.Milestone}; thresholdDays={alert.ThresholdDays}; " +
                $"milestoneDate={alert.MilestoneDate:yyyy-MM-dd}");

    private static AuditEntry ToResolvedAuditEntry(TenantId tenantId, RenewalAlert alert, DateTimeOffset now) => new(
        tenantId,
        Actor: AlertServiceActor,
        Action: AuditResolvedAction,
        ResourceType: AuditResourceType,
        ResourceId: alert.ContractId.ToString(),
        Timestamp: now,
        Detail: $"milestone={alert.Milestone}; thresholdDays={alert.ThresholdDays}; " +
                $"staleMilestoneDate={alert.MilestoneDate:yyyy-MM-dd}");
}
