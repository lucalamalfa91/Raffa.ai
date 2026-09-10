namespace Raffa.Identity.Workspace.Domain;

/// <summary>
/// The fixed V1 role catalog (product spec §3.1; story us-01-workspace-roles AC-3: "Role
/// assignment resolves from OIDC claims (Admin/Procurement/Legal/Finance/Read-only)"). Council-
/// owned and not extensible per tenant in V1 — every workspace is seeded with exactly one
/// <see cref="WorkspaceRole"/> row per member of this enum
/// (<see cref="WorkspaceFactory.CreateWorkspaceWithDefaultRoles"/>). Resolving an OIDC claim to
/// one of these values and recording the resulting <see cref="WorkspaceMembership"/> is task
/// E01/F05/US01/T02's job (ADR-010); this enum only names the fixed set both tasks share.
/// </summary>
public enum WorkspaceRoleName
{
    /// <summary>Workspace config, users, roles, uploads/deletion, integrations, all contracts, audit logs.</summary>
    Admin,

    /// <summary>Contracts, spend, renewals, benchmarks, savings, quote checks, negotiation recommendations.</summary>
    Procurement,

    /// <summary>Clauses, risks, liability, obligations, termination, evidence.</summary>
    Legal,

    /// <summary>Spend, financial obligations, payment terms, savings.</summary>
    Finance,

    /// <summary>"Read-only / Business" in the spec: authorized search and Q&amp;A without editing.</summary>
    ReadOnly,
}
