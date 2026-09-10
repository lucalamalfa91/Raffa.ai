using Raffa.SharedKernel;

namespace Raffa.Renewals.Domain;

/// <summary>
/// The persisted row task E03/F02/US01/T02 ("Alert creation + re-compute on correction", the
/// wave-spec's <c>renewal-alerts</c> artifact) adds on top of task E03/F02/US01/T01's own
/// <c>renewal.approaching</c> <see cref="Raffa.Renewals.Application.RenewalApproachingEvent"/> —
/// product spec Appendix B's "typical consumer/action" for that event is literally "Create
/// alert/task", and this is that alert. <see cref="Raffa.Renewals.Application.RenewalThresholdScheduler"/>'s
/// own doc comment named exactly this gap: "no persisted 'already alerted' state is needed [there];
/// de-duplicating which alerts already exist for a threshold is parent story task-02's job" — this
/// type, plus <see cref="Raffa.Renewals.Application.RenewalAlertService"/>, is that job.
///
/// <para>
/// Keyed for de-duplication by (<see cref="TenantScopedEntity.TenantId"/>,
/// <see cref="ContractId"/>, <see cref="Milestone"/>, <see cref="ThresholdDays"/>) — see
/// <see cref="Raffa.Renewals.Infrastructure.Configurations.RenewalAlertConfiguration"/>'s own
/// partial unique index on that tuple (scoped to <see cref="RenewalAlertStatus.Active"/> rows only).
/// A contract can legitimately hold more than one *simultaneous* alert (a
/// <see cref="RenewalMilestoneKind.RenewalDate"/> threshold and a
/// <see cref="RenewalMilestoneKind.CancellationDeadline"/> threshold both firing the same run — see
/// <c>RenewalThresholdScheduler.EvaluateThresholdsAsync</c>'s own doc comment), so the key is the
/// full tuple, not <see cref="ContractId"/> alone (unlike <see cref="RenewalAction"/>, which is
/// deliberately a single mutable projection per contract).
/// </para>
///
/// <para>
/// Deliberately does not reference <c>Raffa.Documents.Contracts.Domain.Contract</c>: ADR-002's
/// dependency-direction rule only allows this module to reference <c>Raffa.SharedKernel</c> (plus
/// <c>Raffa.Benchmark</c>) — the same reason <see cref="Raffa.Renewals.Application.ContractRenewalTerms"/>
/// and <see cref="RenewalAction"/> are their own small shapes rather than the real entity.
/// </para>
/// </summary>
public sealed class RenewalAlert : TenantScopedEntity
{
    /// <summary>Correlates to the same id product spec App B's event/alert pair share —
    /// <see cref="Raffa.Renewals.Application.RenewalApproachingEvent.ContractId"/> when this row
    /// was created from a raised event.</summary>
    public required EntityId ContractId { get; set; }

    /// <summary>Which of the contract's two dates this alert is about — echoes
    /// <see cref="Raffa.Renewals.Application.RenewalApproachingEvent.Milestone"/>.</summary>
    public required RenewalMilestoneKind Milestone { get; set; }

    /// <summary>Which configured <see cref="Raffa.Renewals.Configuration.ThresholdWindowOptions.DaysBeforeDeadline"/>
    /// window matched — echoes <see cref="Raffa.Renewals.Application.RenewalApproachingEvent.ThresholdDays"/>;
    /// part of this row's own de-duplication key (see the type doc comment).</summary>
    public required int ThresholdDays { get; set; }

    /// <summary>
    /// The calendar date this alert was raised against — echoes
    /// <see cref="Raffa.Renewals.Application.RenewalApproachingEvent.MilestoneDate"/> at creation
    /// time. <see cref="Raffa.Renewals.Application.RenewalAlertService.RecomputeForContractAsync"/>
    /// compares this against a fresh <see cref="Raffa.Renewals.Application.RenewalEngine.Calculate"/>
    /// result on every contract correction (AC-3): a mismatch means the correction moved the
    /// contract's real renewal date/cancellation deadline away from what this row recorded, so the
    /// alert is superseded (<see cref="RenewalAlertStatus.Resolved"/>) rather than left silently
    /// wrong (Appendix C rule 10). This field itself is never rewritten in place — a resolved alert
    /// keeps the stale date it was originally raised against as its own honest historical record;
    /// a genuinely new match creates a new row instead.
    /// </summary>
    public required DateOnly MilestoneDate { get; set; }

    /// <summary>See <see cref="RenewalAlertStatus"/>'s own doc comment.</summary>
    public required RenewalAlertStatus Status { get; set; }

    /// <summary>When this row was first raised — <see cref="Raffa.Renewals.Application.RenewalApproachingEvent.OccurredAt"/>
    /// when created from a scheduler tick or a correction-triggered recompute (both go through the
    /// same <see cref="Raffa.Renewals.Application.RenewalThresholdScheduler.EvaluateThresholdsAsync"/>
    /// path); never rewritten after creation (append-only "when did this first fire", Appendix C
    /// rule 9).</summary>
    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>When this row last changed — equal to <see cref="CreatedAt"/> until a later
    /// correction resolves it (<see cref="Raffa.Renewals.Application.RenewalAlertService.RecomputeForContractAsync"/>
    /// sets this to <see cref="Raffa.SharedKernel.IClock.UtcNow"/> at the moment of resolution).
    /// No hidden clock — same convention every other timestamped write in this codebase
    /// follows.</summary>
    public required DateTimeOffset UpdatedAt { get; set; }
}
