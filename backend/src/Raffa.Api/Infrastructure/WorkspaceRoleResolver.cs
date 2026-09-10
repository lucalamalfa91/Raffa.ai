using System.Security.Claims;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// Task E13/F04/US01/T02 (documents-v2-api): resolves the caller's workspace role for a tenant, so
/// the Admin-only surfaces (<c>POST /api/documents/{id}/reprocess</c>,
/// <c>DELETE /api/documents/{id}</c> — R-DOC-07/R-DOC-10) can answer 403 for everyone else.
///
/// <para>
/// Three sources, in order of how much they can be trusted:
/// <list type="number">
/// <item><b>Claims.</b> An authenticated principal's role claims, through the same
/// <see cref="WorkspaceRoleClaimResolver"/> <c>GET /api/audit</c> already uses. This is the ADR-010
/// end state, and the only source that will survive the interim posture below.</item>
/// <item><b>A role header</b> — <c>X-Role</c> (what <c>GET /api/capabilities</c> already reads) or
/// its <c>X-Workspace-Role</c> alias. Non-authoritative, exactly like <c>X-Tenant-Id</c>: it says
/// which role the caller claims, and is only as trustworthy as the network in front of this API.
/// </item>
/// <item><b>The membership table.</b> When no claim and no header is present but the request does
/// carry <c>X-User-Id</c> (ADR-022: the MSAL account username; the web sends it on every call), the
/// caller's role is read from <c>workspace_membership</c> for this tenant. This is the branch that
/// makes the web's Admin-only buttons work today without inventing a new client header, and it is
/// the least spoofable of the two interim ones: it is a real row a workspace admin created, not a
/// self-declared string.</item>
/// </list>
/// A caller matching none of the three has no role, and every Admin-only endpoint answers 403.
/// </para>
/// </summary>
internal sealed class WorkspaceRoleResolver(IdentityWorkspaceDbContext dbContext, ITenantContext tenantContext)
{
    public const string RoleHeaderName = "X-Role";
    public const string WorkspaceRoleHeaderName = "X-Workspace-Role";
    public const string UserIdHeaderName = "X-User-Id";

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

        if (TryResolveHeaderRole(httpContext.Request, out var headerRole))
        {
            return headerRole;
        }

        return await ResolveMembershipRoleAsync(httpContext.Request, tenantId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Convenience for the endpoints: "is this caller a Workspace Admin of this tenant?"</summary>
    public async Task<bool> IsAdminAsync(
        HttpContext httpContext, TenantId tenantId, CancellationToken cancellationToken = default) =>
        await ResolveAsync(httpContext, tenantId, cancellationToken).ConfigureAwait(false) == WorkspaceRoleName.Admin;

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
        HttpRequest request, TenantId tenantId, CancellationToken cancellationToken)
    {
        if (!request.Headers.TryGetValue(UserIdHeaderName, out var values))
        {
            return null;
        }

        var userId = values.ToString().Trim();
        if (string.IsNullOrEmpty(userId))
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
                && (user.Email == userId || user.ExternalSubjectId == userId)
            select role.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return WorkspaceRoleClaimResolver.TryResolve(roleNames.Select(name => name.ToString()), out var resolved)
            ? resolved
            : null;
    }
}
