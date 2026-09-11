using System.Security.Claims;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// Task E13/F04/US01/T02 (documents-v2-api), narrowed by task E14/F02/US02/T01 (wave w14 "workspace
/// is real"; ADR-022 w14 footer clause 1; ADR-025 §E "a client-declared role is never an
/// authorization source"): resolves the caller's workspace role for a tenant, so the Admin-only
/// surfaces (<c>POST /api/documents/{id}/reprocess</c>, <c>DELETE /api/documents/{id}</c> —
/// R-DOC-07/R-DOC-10) can answer 403 for everyone else.
///
/// <para>
/// Two sources, in order of how much they can be trusted, and <b>nothing else</b>:
/// <list type="number">
/// <item><b>Claims.</b> An authenticated principal's role claims, through the same
/// <see cref="WorkspaceRoleClaimResolver"/> <c>GET /api/audit</c> already uses. This is the ADR-010
/// end state, and the only source that survives past this wave.</item>
/// <item><b>The membership table.</b> The identity <see cref="ICallerIdentity"/> resolves for the
/// current request (ADR-025 §A2: trusted for one thing only — which membership rows to look up, no
/// role, no tenant, no scope of its own) is matched against <c>workspace_membership</c> for this
/// tenant. This is the branch that makes the web's Admin-only buttons work today, and it is the
/// least spoofable of the two: it is a real row a workspace admin created, not a self-declared
/// string.</item>
/// </list>
/// A caller matching neither source has no role, and every Admin-only endpoint answers 403.
/// </para>
///
/// <para>
/// <b>The header is demoted, not removed (ADR-022 w14 footer clause 1; ADR-025 §E).</b> Before this
/// wave, a third branch sat between the two above and read the two interim role-header names
/// declared just below — a client-declared role, trusted exactly like <c>X-Tenant-Id</c>. That
/// branch is deleted:
/// where a header and a real membership row disagree, membership now wins in <b>both</b> directions
/// — a header claiming <c>Admin</c> never grants, and one claiming <c>Procurement</c> never revokes
/// a real Admin's rights (ADR-025 Rule E2). The two header-name constants below remain only as the
/// historical shape of that interim signal; see the doc comment on the method that still parses
/// them for why it is never invoked from here again.
/// </para>
/// </summary>
internal sealed class WorkspaceRoleResolver(
    IdentityWorkspaceDbContext dbContext, ITenantContext tenantContext, ICallerIdentity callerIdentity)
{
    public const string RoleHeaderName = "X-Role";
    public const string WorkspaceRoleHeaderName = "X-Workspace-Role";

    /// <summary>
    /// The caller's role for <paramref name="tenantId"/>, or <see langword="null"/> when none can
    /// be established.
    /// </summary>
    public async Task<WorkspaceRoleName?> ResolveAsync(
        HttpContext httpContext, TenantId tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (httpContext.User.Identity is { IsAuthenticated: true }
            && WorkspaceRoleClaimResolver.TryResolve(
                httpContext.User.FindAll(ClaimTypes.Role).Select(c => c.Value), out var claimRole))
        {
            return claimRole;
        }

        // ADR-022 w14 footer clause 1 / ADR-025 §E: the header branch that used to sit here between
        // claims and membership is deleted, not narrowed -- a client-declared role is never an
        // authorization source. Membership is the only remaining fallback.
        return await ResolveMembershipRoleAsync(tenantId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Convenience for the endpoints: "is this caller a Workspace Admin of this tenant?"</summary>
    public async Task<bool> IsAdminAsync(
        HttpContext httpContext, TenantId tenantId, CancellationToken cancellationToken = default) =>
        await ResolveAsync(httpContext, tenantId, cancellationToken).ConfigureAwait(false) == WorkspaceRoleName.Admin;

    /// <summary>
    /// The interim role-header parse (<see cref="RoleHeaderName"/> / <see cref="WorkspaceRoleHeaderName"/>;
    /// ADR-022 w14 footer clause 1; ADR-025 §E). <b>Demoted, not removed.</b>
    /// <see cref="ResolveAsync"/> no longer calls this —
    /// the only thing a header can still shape anywhere in this host is non-authoritative UI
    /// affordance on <c>GET /api/capabilities</c>, which parses the same two header names
    /// independently through <see cref="WorkspaceRoleClaimResolver"/> directly rather than through
    /// this method (<c>CapabilitiesEndpointExtensions.cs</c> is untouched by this task — its own doc
    /// comment already draws this exact line). This method must never again be reached from an
    /// authorization decision.
    /// </summary>
    private static bool TryResolveHeaderRole(HttpRequest request, out WorkspaceRoleName role)
    {
        foreach (var headerName in new[] { RoleHeaderName, WorkspaceRoleHeaderName })
        {
            if (request.Headers.TryGetValue(headerName, out var values)
                && WorkspaceRoleClaimResolver.TryResolve(values.ToString(), out role))
            {
                return true;
            }
        }

        role = default;
        return false;
    }

    private async Task<WorkspaceRoleName?> ResolveMembershipRoleAsync(
        TenantId tenantId, CancellationToken cancellationToken)
    {
        // ADR-025 §A2 / Rule A1: the one identity seam, instead of reading X-User-Id off
        // HttpRequest.Headers directly. Trusted for one thing only -- which membership rows to look
        // up; it confers no role, no tenant and no scope of its own. W15 (NW-05) retires the header
        // by rewriting ICallerIdentity's own implementation, so this call site is unaffected.
        var identity = callerIdentity.Resolve();
        if (identity is null)
        {
            return null;
        }

        using var tenantScope = tenantContext.BeginScope(tenantId);

        // One join, tenant-scoped on every leg (RLS backstops it — ADR-009). Highest role wins for
        // a user who somehow holds several memberships, using WorkspaceRoleClaimResolver's own
        // precedence rather than a second, divergent ordering here.
        var roleNames = await (
            from user in dbContext.WorkspaceUsers
            join membership in dbContext.WorkspaceMemberships on user.Id equals membership.WorkspaceUserId
            join role in dbContext.WorkspaceRoles on membership.WorkspaceRoleId equals role.Id
            where user.TenantId == tenantId
                && membership.TenantId == tenantId
                && role.TenantId == tenantId
                && (user.Email == identity || user.ExternalSubjectId == identity)
            select role.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return WorkspaceRoleClaimResolver.TryResolve(roleNames.Select(name => name.ToString()), out var resolved)
            ? resolved
            : null;
    }
}
