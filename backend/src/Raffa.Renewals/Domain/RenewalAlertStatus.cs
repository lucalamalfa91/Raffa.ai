namespace Raffa.Renewals.Domain;

/// <summary>
/// Lifecycle of one <see cref="RenewalAlert"/> row (task E03/F02/US01/T02, the wave-spec's
/// <c>renewal-alerts</c> artifact; parent story us-01-threshold-scheduler AC-3: "Scheduler
/// recomputes when a contract/term is corrected"). Two states only, mirroring
/// <see cref="RenewalOpportunityStatus"/>'s own "three-way, never a fourth invented state" restraint
/// at the size this concept actually needs:
///
/// <list type="bullet">
/// <item><see cref="Active"/> — the threshold match this row records is still true against the
/// contract's current (possibly corrected) renewal terms.</item>
/// <item><see cref="Resolved"/> — a later correction moved the contract's computed
/// <see cref="Raffa.Renewals.Application.RenewalCalculationResult.RenewalDate"/> /
/// <see cref="Raffa.Renewals.Application.RenewalCalculationResult.CancellationDeadline"/> away
/// from this row's own <see cref="RenewalAlert.MilestoneDate"/>, so the alert is stale — superseded,
/// not deleted (Appendix C rule 5: never destructively overwrite history; the same append-only
/// instinct <see cref="Raffa.Documents.Contracts.Domain.CorrectionHistory"/> already applies one
/// module over, reproduced here as a status flip instead of a delete because a
/// <see cref="RenewalAlert"/> is a fact about "an alert was raised", not a mutable projection like
/// <see cref="Raffa.Renewals.Domain.RenewalAction"/>).</item>
/// </list>
/// </summary>
public enum RenewalAlertStatus
{
    Active,
    Resolved,
}
