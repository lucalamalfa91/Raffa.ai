namespace Raffa.Renewals.Domain;

/// <summary>
/// Which of <see cref="Raffa.Renewals.Application.RenewalCalculationResult"/>'s two
/// independently-computed dates a <see cref="Raffa.Renewals.Application.RenewalApproachingEvent"/>
/// is about. feature-02-cancellation-alerts is titled "Threshold alerts for cancellation
/// deadlines" and its own slice names both "renewal/cancellation deadline events" as distinct
/// milestones an owner must act on — a contract can cross a threshold for one, the other, or both
/// on the same scheduler run (task E03/F02/US01/T01), and a consumer needs to tell them apart to
/// route/word the resulting alert correctly (parent story task-02, "Alert creation").
/// </summary>
public enum RenewalMilestoneKind
{
    /// <summary>
    /// The threshold matched <see cref="Raffa.Renewals.Application.RenewalCalculationResult.RenewalDate"/> /
    /// <see cref="Raffa.Renewals.Application.RenewalCalculationResult.DaysUntilRenewal"/>.
    /// </summary>
    RenewalDate,

    /// <summary>
    /// The threshold matched <see cref="Raffa.Renewals.Application.RenewalCalculationResult.CancellationDeadline"/> /
    /// <see cref="Raffa.Renewals.Application.RenewalCalculationResult.DaysUntilCancellationDeadline"/> —
    /// the "notice must be given by" date (product spec §9.1).
    /// </summary>
    CancellationDeadline,
}
