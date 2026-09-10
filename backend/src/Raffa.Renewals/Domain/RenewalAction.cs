using Raffa.SharedKernel;

namespace Raffa.Renewals.Domain;

/// <summary>
/// The persisted row <c>POST /api/renewals/{id}/action</c> upserts (task E03/F03/US01/T02,
/// renewal-action; parent story us-01-renewal-dashboard-api AC-3). Keyed by
/// (<see cref="TenantScopedEntity.TenantId"/>, <see cref="ContractId"/>) — at most one row per
/// contract per tenant, matching product spec §9.1's "create/update renewal opportunity" upsert
/// semantics (<see cref="Raffa.Renewals.Application.RenewalOpportunity"/>'s own doc comment
/// named this exact table as the follow-up persistence this task delivers) and
/// <see cref="Raffa.Renewals.Infrastructure.Configurations.RenewalActionConfiguration"/>'s unique
/// index on that pair.
///
/// Deliberately keyed by <see cref="ContractId"/> alone, not a foreign key to a stored
/// <c>Renewal</c> row: no task has given this module a persisted "renewal" entity — `GET
/// /api/renewals` computes the pipeline on the fly from <c>Raffa.Documents.Contracts</c> plus
/// <c>RenewalEngine</c> every call (see <c>Raffa.Api.RenewalsEndpointExtensions</c>) — and
/// ADR-002 forbids this module from referencing <c>Raffa.Documents.Contracts.Domain.Contract</c>
/// at all, so this type cannot validate that <see cref="ContractId"/> actually names an existing,
/// tenant-owned contract; that cross-module existence check (if ever added) belongs in
/// <c>Raffa.Api</c>, "the one project allowed to reference every module" — deliberately not
/// attempted by this task (see <c>Raffa.Api.RenewalsEndpointExtensions</c>'s own doc comment on
/// the POST handler for the honest gap this leaves). Tenant scoping itself does not depend on that
/// check: RLS plus the explicit <see cref="TenantScopedEntity.TenantId"/> filter already make it
/// impossible for one tenant's request to read or write another tenant's row, regardless of
/// whether <see cref="ContractId"/> resolves to anything.
/// </summary>
public sealed class RenewalAction : TenantScopedEntity
{
    /// <summary>Correlates to the same id <c>GET /api/renewals</c> returns as
    /// <c>RenewalPipelineItem.ContractId</c> — the route's <c>{id}</c> segment.</summary>
    public required EntityId ContractId { get; set; }

    /// <summary>Free-text — who is tracking/responsible for this renewal. Not a foreign key to a
    /// workspace member: ADR-010 (Entra ID/OIDC) is not wired into this host yet (same interim gap
    /// every other tenant-scoped write in this codebase documents — see
    /// <c>Raffa.Documents.Contracts.Application.ContractCorrectionService</c>'s own doc comment),
    /// so there is no validated, queryable identity to reference. A caller-supplied string is
    /// honest about that; nothing here claims it was verified.</summary>
    public required string Owner { get; set; }

    /// <summary>The Procurement workflow lifecycle — see <see cref="RenewalActionStatus"/>'s own
    /// doc comment for the documented assumption behind this exact three-state shape.</summary>
    public required RenewalActionStatus Status { get; set; }

    /// <summary>Free-text — what Procurement is doing/did about this renewal (e.g. "Started
    /// negotiation", "Renewed at same terms", "Escalated to Legal"). Independent of
    /// <c>RenewalInsightRecommendations.RecommendedAction</c> (the system's own computed
    /// suggestion, e.g. "Start negotiation now") — this is the human's own record of what they
    /// actually chose, which may or may not match the recommendation.</summary>
    public required string Action { get; set; }

    /// <summary>When this row was last written (caller-supplied via <c>IClock</c>, not a database
    /// default) — the same "no hidden clock" convention every other timestamped write in this
    /// codebase follows.</summary>
    public required DateTimeOffset UpdatedAt { get; set; }
}
