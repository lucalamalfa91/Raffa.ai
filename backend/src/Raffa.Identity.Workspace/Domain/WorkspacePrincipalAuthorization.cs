using System.Security.Claims;
using Raffa.SharedKernel;

namespace Raffa.Identity.Workspace.Domain;

/// <summary>
/// Resolves "which tenant, with which role" an already-authenticated
/// <see cref="ClaimsPrincipal"/> is acting as (task E01/F06/US02/T02, the `audit-query` artifact;
/// story us-02-audit-baseline AC-2: "GET /api/audit returns authorized, tenant-scoped events"; the
/// story's own dependency row: "feature-05 (roles) | audit reads identity").
///
/// Deliberately independent of *how* the principal was authenticated. ADR-010 (Entra ID app
/// registrations / JWT bearer validation per environment) is not cited by this story — only
/// ADR-009 (RLS) and ADR-003 (PostgreSQL) are — and nothing in this codebase wires
/// <c>AddAuthentication</c>/<c>AddJwtBearer</c> yet (<c>Raffa.Api</c>'s host has no auth
/// middleware; <c>WorkspaceMembershipService</c>'s own doc comment notes it is "not yet called by a
/// host" for the same reason). Rather than block this task on a not-yet-scheduled ADR-010 wiring
/// task, this type only fixes the claim *contract* a future host-auth task must satisfy — one
/// <see cref="TenantIdClaimType"/> claim carrying the caller's workspace/tenant id, and one or more
/// <see cref="ClaimTypes.Role"/> claims in any shape <see cref="WorkspaceRoleClaimResolver"/>
/// already accepts — and makes the authorization decision from whatever principal it is handed. A
/// real JWT bearer handler later (or a test today) both work the same way. This is this task's own
/// scope decision, the same way <see cref="WorkspaceRoleClaimResolver"/>'s own doc comment already
/// left the exact OIDC claim shape to be settled by whichever task wires the host — not a
/// re-litigation of ADR-010.
/// </summary>
public static class WorkspacePrincipalAuthorization
{
    /// <summary>
    /// Claim type carrying the caller's workspace/tenant id (a <see cref="Guid"/>, any format
    /// <see cref="Guid.TryParse(string?, out Guid)"/> accepts). Not an Entra-standard claim name —
    /// this codebase has no host authentication wired yet to mint one; whichever task adds it
    /// (ADR-010) is free to change this constant, since every caller goes through this one place.
    /// </summary>
    public const string TenantIdClaimType = "tenant_id";

    /// <summary>
    /// <see langword="true"/> and populates <paramref name="tenantId"/> only when
    /// <paramref name="principal"/> is authenticated, carries a valid
    /// <see cref="TenantIdClaimType"/> claim, and resolves (via
    /// <see cref="WorkspaceRoleClaimResolver.TryResolve(IEnumerable{string}, out WorkspaceRoleName)"/>)
    /// to exactly <paramref name="requiredRole"/>. Fails closed — any missing or malformed piece is
    /// a full refusal, never a partial grant, mirroring ADR-009's own "fail closed, never fail
    /// open" tenancy rule.
    /// </summary>
    public static bool TryAuthorize(
        ClaimsPrincipal principal,
        WorkspaceRoleName requiredRole,
        out TenantId tenantId,
        out WorkspaceAuthorizationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(principal);

        tenantId = default;

        if (principal.Identity is not { IsAuthenticated: true })
        {
            failure = WorkspaceAuthorizationFailure.Unauthenticated;
            return false;
        }

        var tenantClaimValue = principal.FindFirst(TenantIdClaimType)?.Value;
        if (!Guid.TryParse(tenantClaimValue, out var tenantGuid))
        {
            failure = WorkspaceAuthorizationFailure.MissingOrInvalidTenantClaim;
            return false;
        }

        var roleClaimValues = principal.FindAll(ClaimTypes.Role).Select(c => c.Value);
        if (!WorkspaceRoleClaimResolver.TryResolve(roleClaimValues, out var role) || role != requiredRole)
        {
            failure = WorkspaceAuthorizationFailure.InsufficientRole;
            return false;
        }

        tenantId = new TenantId(tenantGuid);
        failure = WorkspaceAuthorizationFailure.None;
        return true;
    }
}
