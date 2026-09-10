namespace Raffa.Identity.Workspace.Domain;

/// <summary>
/// Why <see cref="WorkspacePrincipalAuthorization.TryAuthorize"/> refused a caller (task
/// E01/F06/US02/T02; story us-02-audit-baseline AC-2: "GET /api/audit returns authorized,
/// tenant-scoped events"). A distinct reason (rather than a bare <see langword="bool"/>) so the
/// HTTP layer can map each cause to the right status code — 401 for "no identity at all" vs 403 for
/// "an identity, but not entitled" — without re-deriving the reason itself.
/// </summary>
public enum WorkspaceAuthorizationFailure
{
    /// <summary>Authorization succeeded. Only meaningful as the paired out-value when
    /// <see cref="WorkspacePrincipalAuthorization.TryAuthorize"/> returns <see langword="true"/>.</summary>
    None,

    /// <summary>The request carries no authenticated identity at all.</summary>
    Unauthenticated,

    /// <summary>
    /// Authenticated, but the principal carries no (or an unparsable)
    /// <see cref="WorkspacePrincipalAuthorization.TenantIdClaimType"/> claim. This codebase has no
    /// host authentication wired yet that mints one — ADR-010 (Entra ID JWT bearer) is a distinct,
    /// not-yet-scheduled task, and this story only carries ADR-009/ADR-003 — so this is the
    /// expected outcome for any caller until that task lands. Treated as a plain refusal rather
    /// than a distinguishable status code, so a malformed or foreign token cannot be used to probe
    /// which half of the check it failed.
    /// </summary>
    MissingOrInvalidTenantClaim,

    /// <summary>
    /// Authenticated with a resolvable tenant, but the caller's resolved
    /// <see cref="WorkspaceRoleName"/> does not meet the endpoint's required role (product spec
    /// §3.1: "audit logs" is a Workspace Admin permission, not shared by the other four roles).
    /// </summary>
    InsufficientRole,
}
