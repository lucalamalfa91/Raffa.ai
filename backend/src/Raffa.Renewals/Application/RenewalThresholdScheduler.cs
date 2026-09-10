using Raffa.Renewals.Configuration;
using Raffa.Renewals.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Renewals.Application;

/// <summary>
/// Task E03/F02/US01/T01's own artifact (wave-spec: <c>threshold-scheduler</c>; parent story
/// us-01-threshold-scheduler) — product spec §9.1's "Daily scheduler for each active contract:
/// calculate renewal date, calculate cancellation deadline, calculate days remaining, ... emit
/// threshold events if applicable" made concrete, minus "create/update renewal opportunity" (a
/// sibling artifact —
/// <see cref="Raffa.Renewals.Infrastructure.ServiceCollectionExtensions.AddRenewalsModule"/>'s
/// own doc comment names "renewal-opportunity generation" as a different task that also depends on
/// <c>renewal-engine</c>).
///
/// Composes <see cref="RenewalEngine"/> (this task's own <c>depends_on: [renewal-engine]</c>) with
/// <see cref="ThresholdWindowOptions"/> (AC-1: configurable windows, default
/// 365/270/180/120/90/60/30 days) to decide, once per contract per scheduler run, whether "today"
/// (<see cref="IClock"/>) is exactly N days before that contract's renewal date or cancellation
/// deadline, for one of the configured N's. Exact match, not "at or under" a threshold, so a daily
/// run fires each configured threshold exactly once per contract — no persisted "already alerted"
/// state is needed here; de-duplicating which alerts already exist for a threshold is parent story
/// task-02's job ("Alert creation + re-compute on correction"), not this one's.
///
/// Every raised <see cref="RenewalApproachingEvent"/> is written through <see cref="IAuditWriter"/>
/// as one <c>renewal.approaching</c> entry (see that type's own doc comment for why an audit entry,
/// not a mediator, is this codebase's actual "emit an event" mechanism today) before this method
/// returns, so the event is durable and queryable (<c>GET /api/audit</c>) even before a real
/// consumer exists — the same "the write happens, a caller arrives later" sequencing
/// <see cref="RenewalEngine"/>'s own doc comment already used for itself.
///
/// <para>
/// Opens its own <see cref="ITenantContext.BeginScope"/> for the run's <c>tenantId</c> before
/// writing anything (task E03/F04/US01/T01, r2-integration) — the same convention
/// <see cref="Raffa.Renewals.Application.RenewalActionService.SetActionAsync"/> already follows.
/// Without it, ADR-009's RLS backstop fails <em>closed</em>: <c>TenantRlsConnectionInterceptor</c>
/// leaves `app.tenant_id` unset on the connection, and the Audit module's own
/// `AddTenantRowLevelSecurity` `WITH CHECK` policy then rejects every `renewal.approaching` insert
/// this method makes — a real threshold crossing would throw instead of being recorded. Neither
/// <c>Raffa.Renewals.Tests.RenewalThresholdSchedulerTests</c> (a <c>RecordingAuditWriter</c>, no
/// database) nor <c>Raffa.Worker.Tests.RenewalThresholdSchedulerHostedServiceTests</c> (a
/// syntactically-valid-but-never-dialled connection string, by its own design) ever exercised a
/// real, RLS-enforced connection on this path, so this gap went undetected until r2-integration's
/// own real-Postgres proof (<c>Raffa.IntegrationTests.R2EndToEndTests</c>) surfaced it.
/// </para>
/// </summary>
public sealed class RenewalThresholdScheduler(
    IClock clock,
    RenewalEngine renewalEngine,
    IAuditWriter auditWriter,
    ThresholdWindowOptions options,
    ITenantContext tenantContext)
{
    /// <summary>Actor recorded on every <see cref="IAuditWriter"/> entry this scheduler writes —
    /// there is no human operator behind a scheduled run.</summary>
    public const string SchedulerActor = "system:renewal-threshold-scheduler";

    /// <summary>
    /// Runs <see cref="RenewalEngine.CalculateMany"/> over <paramref name="contracts"/>, then checks
    /// each result's <see cref="RenewalCalculationResult.DaysUntilRenewal"/> and
    /// <see cref="RenewalCalculationResult.DaysUntilCancellationDeadline"/> against
    /// <see cref="ThresholdWindowOptions.DaysBeforeDeadline"/>, writing and returning one
    /// <see cref="RenewalApproachingEvent"/> per match (zero, one, or two per contract — a contract
    /// can cross a renewal-date threshold, a cancellation-deadline threshold, both on the same run,
    /// or neither). A contract whose <see cref="RenewalEngine"/> result is <c>NoRenewal</c> or
    /// <c>CannotDetermine</c> naturally raises nothing: both leave the day-count fields
    /// <see langword="null"/>, and a null never matches a configured threshold (Appendix C rule 10
    /// — no threshold is ever fabricated against a date this engine could not determine).
    /// </summary>
    /// <param name="tenantId">The single tenant <paramref name="contracts"/> belongs to — one
    /// scheduler run operates within one tenant at a time (ADR-009's "one connection, one tenant
    /// context" convention); a caller iterating many tenants calls this once per tenant.</param>
    /// <param name="contracts">The tenant's active contracts' renewal terms. Which contracts count
    /// as "active" (in scope to call this with) is the caller's decision, the same division of
    /// responsibility <see cref="RenewalEngine.CalculateMany"/>'s own doc comment already
    /// draws.</param>
    /// <param name="cancellationToken">Propagated to every <see cref="IAuditWriter.WriteAsync"/>
    /// call.</param>
    public async Task<IReadOnlyList<RenewalApproachingEvent>> EvaluateThresholdsAsync(
        TenantId tenantId,
        IEnumerable<ContractRenewalTerms> contracts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        // Entry point: open this call's own tenant scope before the audit write below — see the
        // type doc comment for why (ADR-009's RLS backstop needs an active per-connection tenant
        // claim on every insert, including this method's own).
        using var _ = tenantContext.BeginScope(tenantId);

        var results = renewalEngine.CalculateMany(contracts);
        var events = results.SelectMany(result => EvaluateResult(tenantId, result)).ToList();

        foreach (var raised in events)
        {
            await auditWriter.WriteAsync(ToAuditEntry(raised), cancellationToken).ConfigureAwait(false);
        }

        return events;
    }

    private IEnumerable<RenewalApproachingEvent> EvaluateResult(TenantId tenantId, RenewalCalculationResult result)
    {
        var occurredAt = clock.UtcNow;

        if (result.RenewalDate is { } renewalDate
            && result.DaysUntilRenewal is { } daysUntilRenewal
            && options.DaysBeforeDeadline.Contains(daysUntilRenewal))
        {
            yield return new RenewalApproachingEvent
            {
                TenantId = tenantId,
                OccurredAt = occurredAt,
                ContractId = result.ContractId,
                Milestone = RenewalMilestoneKind.RenewalDate,
                MilestoneDate = renewalDate,
                ThresholdDays = daysUntilRenewal,
                DaysRemaining = daysUntilRenewal,
            };
        }

        if (result.CancellationDeadline is { } cancellationDeadline
            && result.DaysUntilCancellationDeadline is { } daysUntilCancellationDeadline
            && options.DaysBeforeDeadline.Contains(daysUntilCancellationDeadline))
        {
            yield return new RenewalApproachingEvent
            {
                TenantId = tenantId,
                OccurredAt = occurredAt,
                ContractId = result.ContractId,
                Milestone = RenewalMilestoneKind.CancellationDeadline,
                MilestoneDate = cancellationDeadline,
                ThresholdDays = daysUntilCancellationDeadline,
                DaysRemaining = daysUntilCancellationDeadline,
            };
        }
    }

    private static AuditEntry ToAuditEntry(RenewalApproachingEvent raised) => new(
        raised.TenantId,
        Actor: SchedulerActor,
        Action: RenewalApproachingEvent.EventName,
        ResourceType: "Contract",
        ResourceId: raised.ContractId.ToString(),
        Timestamp: raised.OccurredAt,
        Detail: $"milestone={raised.Milestone}; thresholdDays={raised.ThresholdDays}; " +
                $"milestoneDate={raised.MilestoneDate:yyyy-MM-dd}");
}
