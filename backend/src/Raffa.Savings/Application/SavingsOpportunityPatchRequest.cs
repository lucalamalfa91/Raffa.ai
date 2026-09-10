namespace Raffa.Savings.Application;

/// <summary>
/// Request body for `PATCH /api/savings/{id}` (task E04/F02/US02/T01, savings-opportunity, and task
/// E04/F02/US02/T02, realized-savings; parent story us-02-savings-opportunity AC-1 "updates
/// status/owner/realized..."; spec Appendix A "PATCH /api/savings/{id} | Status/owner/realized
/// value"). Deliberately living here rather than in `Raffa.Api` — same
/// `Raffa.ArchitectureTests.DependencyDirectionTests.Host_must_not_contain_domain_types` reason
/// <c>Raffa.Renewals.Application.RenewalActionRequest</c>'s own doc comment already gives.
///
/// All three fields are optional (unlike <c>RenewalActionRequest</c>, which requires all three of
/// its own fields together for its upsert): this is a genuine partial update of an already-identified,
/// already-persisted row keyed by the route's own <c>{id}</c>, not an upsert keyed by a natural key,
/// so a caller supplies only whichever of owner/status/realizedAmount it wants to change.
/// <see cref="SavingsOpportunityService.UpdateAsync"/> is the one place that validates/parses any
/// value — <see cref="Status"/> is a plain string, not
/// <see cref="Domain.SavingsOpportunityStatus"/>, for the same reason
/// <c>RenewalActionRequest.Status</c> is.
/// </summary>
/// <param name="RealizedAmount">Task E04/F02/US02/T02 (realized-savings, parent story AC-3):
/// when supplied, records a new, append-only <see cref="Domain.RealizedSavings"/> row for this
/// opportunity, in the opportunity's own <see cref="Domain.SavingsOpportunity.Currency"/> — see
/// <see cref="SavingsOpportunityService.UpdateAsync"/>'s own doc comment for the validation this
/// triggers (non-negative; only compatible with a <see cref="Status"/> of <c>"Realized"</c>, or no
/// <see cref="Status"/> at all, which then defaults to <c>Realized</c> automatically).</param>
public sealed record SavingsOpportunityPatchRequest(string? Owner, string? Status, decimal? RealizedAmount);
