using Raffa.Renewals.Domain;
using Raffa.SharedKernel;

namespace Raffa.Renewals.Application;

/// <summary>
/// The <c>renewal.approaching</c> event from product spec Appendix B ("Core Event Catalogue":
/// Producer "Renewal engine", typical consumer/action "Create alert/task") and §13.2
/// ("Event-ready architecture") — task E03/F02/US01/T01's own artifact (wave-spec:
/// <c>threshold-scheduler</c>), raised by <see cref="RenewalThresholdScheduler"/> whenever a
/// contract's renewal date or cancellation deadline crosses one of
/// <see cref="Raffa.Renewals.Configuration.ThresholdWindowOptions.DaysBeforeDeadline"/>'s
/// configured day-counts (parent story us-01-threshold-scheduler AC-1/AC-2).
///
/// Inherits <see cref="DomainEvent"/> — the pre-existing SharedKernel scaffold ("events are
/// dispatched in-process via the mediator; no durable outbox at R0") — for its
/// <see cref="DomainEvent.EventId"/>/<see cref="DomainEvent.TenantId"/>/<see cref="DomainEvent.OccurredAt"/>
/// envelope. No in-process mediator exists anywhere in this codebase yet (ADR-002 leaves the
/// mediator/DI pattern an open, council-owned choice — not this task's to make), so
/// <see cref="RenewalThresholdScheduler"/> does not attempt to invent one: today it makes this
/// event durable and queryable the same way every other named event in this codebase already is —
/// one <see cref="IAuditWriter"/> entry per event (<see cref="EventName"/> as
/// <c>AuditEntry.Action</c>, exactly like <c>DocumentUploadService</c>'s
/// <c>"document.uploaded"</c> and <c>ContractCorrectionService</c>'s <c>"contract.corrected"</c>)
/// — and also returns this strongly-typed record so a future consumer (parent story task-02,
/// "Alert creation") can act on the data directly instead of re-parsing an audit detail string.
/// </summary>
/// <param name="ContractId">The contract this milestone belongs to — echoes
/// <see cref="RenewalCalculationResult.ContractId"/> unchanged.</param>
/// <param name="Milestone">Which of the contract's two dates crossed a threshold.</param>
/// <param name="MilestoneDate">The actual calendar date reached —
/// <see cref="RenewalCalculationResult.RenewalDate"/> or
/// <see cref="RenewalCalculationResult.CancellationDeadline"/> depending on
/// <see cref="Milestone"/>.</param>
/// <param name="ThresholdDays">Which configured window (e.g. 90) matched.</param>
/// <param name="DaysRemaining">Signed day-count from "today" to <see cref="MilestoneDate"/> at the
/// moment this event was raised — equal to <see cref="ThresholdDays"/> by construction (the
/// scheduler only raises this event on an exact match), kept as its own field so a consumer never
/// has to assume that invariant holds forever.</param>
public sealed record RenewalApproachingEvent : DomainEvent
{
    /// <summary>Canonical event name — product spec Appendix B / §13.2's own literal string, and
    /// the <c>AuditEntry.Action</c> this event is durably recorded under.</summary>
    public const string EventName = "renewal.approaching";

    public required EntityId ContractId { get; init; }

    public required RenewalMilestoneKind Milestone { get; init; }

    public required DateOnly MilestoneDate { get; init; }

    public required int ThresholdDays { get; init; }

    public required int DaysRemaining { get; init; }
}
