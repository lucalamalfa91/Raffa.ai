using Raffa.Renewals.Domain;
using Raffa.SharedKernel;

namespace Raffa.Renewals.Application;

/// <summary>
/// The outcome of <see cref="RenewalOpportunityGenerator.Generate"/> — product spec §9.1's daily
/// scheduler step "create/update renewal opportunity", task E03/F01/US01/T02, parent story
/// us-01-deterministic-dates AC-3. Deliberately shaped like
/// <see cref="RenewalCalculationResult"/> (same <see cref="ContractId"/> correlation key, same
/// dates/day-counts, an <see cref="Explanation"/> trace) because an opportunity is never more
/// certain than the calculation it is built from — see
/// <see cref="RenewalOpportunityStatus"/>'s own doc comment for why the status enum mirrors
/// <see cref="RenewalCalculationStatus"/> case-for-case instead of inventing new vocabulary.
///
/// <para>
/// What this type deliberately does <em>not</em> carry: a priority score/component breakdown
/// (us-02-priority-score, a sibling task under this same feature that depends on
/// <c>renewal-engine</c> directly, not on this type), a threshold-alert flag
/// (feature-02-cancellation-alerts' threshold-scheduler, same story), or an owner/status/action
/// (feature-03-renewal-dashboard's renewal-action task, spec Appendix A
/// <c>POST /api/renewals/{id}/action</c>). Those are later, independent enrichments over the same
/// <see cref="ContractId"/> — bolting them on here would smuggle another task's file scope into
/// this one. Also absent: a persisted identity of its own. Spec §9.1 says "create/update renewal
/// opportunity" (upsert semantics), which implies a stored row keyed by <see cref="ContractId"/>
/// (at most one open opportunity per contract) — but no task in this wave gives
/// <c>Raffa.Renewals</c> a <c>DbContext</c> yet (the same "no host endpoint calls this yet"
/// gap <see cref="RenewalOpportunityGenerator"/>'s own doc comment and <c>backend/README.md</c>'s
/// "Renewal Intelligence" section describe), so persistence/upsert is a follow-up composition task,
/// not this one.
/// </para>
/// </summary>
/// <param name="ContractId">Echoes <see cref="RenewalCalculationResult.ContractId"/> unchanged —
/// the correlation key a caller processing many contracts (spec §9.1's "daily scheduler for each
/// active contract") uses to line an opportunity back up with its source contract.</param>
/// <param name="Status">Which of the three outcomes this is — see
/// <see cref="RenewalOpportunityStatus"/>'s own doc comments.</param>
/// <param name="RenewalDate">Equal to <see cref="RenewalCalculationResult.RenewalDate"/> when
/// <see cref="Status"/> is <see cref="RenewalOpportunityStatus.Open"/>; null otherwise — an
/// opportunity this generator could not (or need not) determine never carries a date, fabricated
/// or otherwise.</param>
/// <param name="CancellationDeadline">Equal to
/// <see cref="RenewalCalculationResult.CancellationDeadline"/> when <see cref="Status"/> is
/// <see cref="RenewalOpportunityStatus.Open"/>. Can still be null inside an
/// <see cref="RenewalOpportunityStatus.Open"/> opportunity, the same partial-determination case
/// <see cref="RenewalCalculationResult"/>'s own
/// doc comment describes: a renewal date only needs <c>EndDate</c>/<c>AutoRenewal</c>, but the
/// deadline additionally needs <c>CancellationNoticeDays</c>.</param>
/// <param name="DaysUntilRenewal">Equal to <see cref="RenewalCalculationResult.DaysUntilRenewal"/>
/// — signed and unclamped; a negative value honestly means the renewal date already passed rather
/// than being hidden behind a fabricated floor (Appendix C rule 10).</param>
/// <param name="DaysUntilCancellationDeadline">Equal to
/// <see cref="RenewalCalculationResult.DaysUntilCancellationDeadline"/>; same signed/unclamped
/// rule as <see cref="DaysUntilRenewal"/>.</param>
/// <param name="Explanation">Human-readable trace of why this opportunity has the shape it does —
/// wraps <see cref="RenewalCalculationResult.Explanation"/> with the opportunity-level outcome, the
/// same "not for end users as-is, but enough for a test or developer to see why" role that source
/// field plays.</param>
public sealed record RenewalOpportunity(
    EntityId ContractId,
    RenewalOpportunityStatus Status,
    DateOnly? RenewalDate,
    DateOnly? CancellationDeadline,
    int? DaysUntilRenewal,
    int? DaysUntilCancellationDeadline,
    string Explanation);
