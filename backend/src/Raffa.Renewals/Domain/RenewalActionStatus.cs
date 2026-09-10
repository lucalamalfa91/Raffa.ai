namespace Raffa.Renewals.Domain;

/// <summary>
/// The workflow status a Procurement user tracks against one renewal (task E03/F03/US01/T02,
/// renewal-action; parent story us-01-renewal-dashboard-api AC-3 "`POST /api/renewals/{id}/action`
/// updates owner/status/action"; product spec Appendix A row "Update owner/status/action").
///
/// <para>
/// Documented assumption (no ADR/spec vocabulary fixes this — the spec's own data-model table
/// names a bare <c>Renewal.owner</c> field and the Appendix A row names only the field triple
/// "owner/status/action", not an enum): a small, closed three-state lifecycle, deliberately
/// distinct from <see cref="RenewalCalculationStatus"/>/<see cref="RenewalOpportunityStatus"/> —
/// those describe whether the *engine* could determine a date; this describes whether *Procurement*
/// has started acting on an already-determined renewal. Modelled as a real enum (not a free-form
/// string, unlike e.g. <c>Raffa.Documents.Contracts.Domain.Contract.Status</c>, which is
/// extraction-sourced and therefore open-vocabulary) because this value is only ever set by a
/// human through this module's own API, never sourced from a document, so a closed set is honest
/// rather than restrictive (Appendix C rule 10) and lets a future dashboard filter/sort by status
/// without guessing at free-text values. If a later council decision fixes a different vocabulary,
/// this is a migration (rename/extend the enum + a data migration), not a redesign of the
/// persistence shape.
/// </para>
/// </summary>
public enum RenewalActionStatus
{
    /// <summary>No work has begun yet on this renewal's action — the default a Procurement user
    /// starts from once <see cref="Raffa.Renewals.Application.RenewalPipelineBuilder"/>'s
    /// computed recommendation (see <c>GET /api/renewals</c>) surfaces it.</summary>
    NotStarted,

    /// <summary>Procurement is actively working the renewal (negotiating, gathering approvals,
    /// etc.) — distinct from <see cref="NotStarted"/> so a dashboard can separate "needs
    /// attention" from "already being handled".</summary>
    InProgress,

    /// <summary>Procurement's action on this renewal is done (renewed, cancelled, escalated and
    /// closed, etc.) — <see cref="RenewalAction.Action"/>'s own free-text value on the same row
    /// records <em>what</em> was done; this only records that it is finished.</summary>
    Completed,
}
