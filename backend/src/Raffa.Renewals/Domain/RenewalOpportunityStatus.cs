namespace Raffa.Renewals.Domain;

/// <summary>
/// Outcome discriminator for
/// <see cref="Raffa.Renewals.Application.RenewalOpportunityGenerator"/> (task E03/F01/US01/T02,
/// parent story us-01-deterministic-dates AC-3; product spec §9.1's daily-scheduler step
/// "create/update renewal opportunity"). Mirrors
/// <see cref="RenewalCalculationStatus"/>'s own three-way shape on purpose — an opportunity can
/// only ever be as certain as the calculation it is built from — renaming only the case where the
/// two concepts genuinely diverge: a <see cref="RenewalCalculationStatus.Determined"/> calculation
/// means "this engine could do the arithmetic"; an <see cref="Open"/> opportunity means "Procurement
/// has something to track/act on". <see cref="NoRenewal"/> and <see cref="CannotDetermine"/> are the
/// same facts at both layers, so they keep the same names rather than invent new vocabulary for an
/// identical distinction (Appendix C rule 10: a determined "nothing to do" and an honest "cannot
/// tell" must stay distinguishable, never collapsed into one bit).
/// </summary>
public enum RenewalOpportunityStatus
{
    /// <summary>A renewal date was determined — there is a real opportunity for Procurement to
    /// track (spec §9.1's "create/update renewal opportunity"). Priority scoring
    /// (us-02-priority-score), threshold alerts (feature-02-cancellation-alerts) and owner/status
    /// actions (feature-03-renewal-dashboard's renewal-action task) all build on an
    /// <see cref="Open"/> opportunity; none of that enrichment is computed here — see
    /// <see cref="Raffa.Renewals.Application.RenewalOpportunity"/>'s own doc comment for the exact
    /// field list this status carries.</summary>
    Open,

    /// <summary>Same fact as <see cref="RenewalCalculationStatus.NoRenewal"/>: the contract does not
    /// auto-renew, so there is nothing for Procurement to track — a determined non-event, not a
    /// data gap.</summary>
    NoRenewal,

    /// <summary>Same fact as <see cref="RenewalCalculationStatus.CannotDetermine"/>: required
    /// structured data is missing, so generation abstains rather than fabricate an opportunity
    /// (Appendix C rule 10; parent story AC-3 — "Missing dates return 'cannot determine' rather
    /// than a fabricated value", which applies just as much to the opportunity built from those
    /// dates as to the dates themselves).</summary>
    CannotDetermine,
}
