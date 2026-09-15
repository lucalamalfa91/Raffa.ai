using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// Task E13/F04/US01/T02 (documents-v2-api), narrowed by task E14/F02/US02/T01 (wave w14 "workspace
/// is real"; ADR-022 w14 footer clause 1; ADR-025 §E "a client-declared role is never an
/// authorization source"), collapsed to membership-only by task E18/F01/US01/T01 (wave w15, NW-06;
/// ADR-010 w15 footer §3/S15-13; ADR-025 §I): resolves the caller's workspace role for a tenant, so
/// the Admin-only surfaces (<c>POST /api/documents/{id}/reprocess</c>, <c>DELETE
/// /api/documents/{id}</c> — R-DOC-07/R-DOC-10) can answer 403 for everyone else.
///
/// <para>
/// <b>One source, and one source only: the membership table.</b> The identity
/// <see cref="ICallerIdentity"/> resolves for the current request (the validated token's <c>oid</c>
/// — ADR-010 w15 footer §2.1/S15-6 — trusted for one thing only: which membership rows to look up,
/// no role, no tenant, no scope of its own) is matched against <c>workspace_membership</c> for this
/// tenant. A caller with no live membership row has no role, and every Admin-only endpoint answers
/// 403.
/// </para>
///
/// <para>
/// <b>NW-06 deleted the second source this method used to have, and it is a deletion, not a
/// tidy-up.</b> Before this task, an authenticated principal's role claims were tried first and
/// returned a role <em>without ever consulting which tenant was being asked about</em> — harmless
/// while this host authenticated nobody at all, but the moment this same task wires a real Entra
/// token (NW-05), one Entra app role assigned once in the directory would have resolved to that role
/// in <b>every</b> workspace the caller can name, bypassing <c>workspace_membership</c> entirely —
/// and this type's own <see cref="IsAdminAsync"/> gates <c>DELETE /api/documents/{id}</c> and
/// <c>POST …/reprocess</c>, so the failure would have been cross-tenant <b>destructive</b> access,
/// not merely visibility. A client-declared role header (the interim <c>X-Role</c>/
/// <c>X-Workspace-Role</c> signal) was already demoted out of this decision one wave earlier
/// (ADR-022 w14 footer clause 1; ADR-025 §E; Rule E2 "membership wins in both directions") and had
/// zero call sites left in this type even before this edit — this task removes the one authorization
/// source ADR-025 §I never sanctioned in the first place: "a <c>tenant_id</c> or <c>roles</c> claim
/// is never the authorization source." <c>GET /api/capabilities</c>'s own, separate, non-authoritative
/// use of the same two retired header names for UI affordance only is untouched by this task — that
/// endpoint's own doc comment already draws the line; <c>NW-31</c> (W16) is what eventually removes
/// it.
/// </para>
///
/// <para>
/// <b>Membership match, one key only (ADR-010 w16 footer S16-1)</b>: the role query below matches
/// <see cref="Raffa.Identity.Workspace.Domain.WorkspaceUser.ExternalSubjectId"/> only — it used to
/// also match <c>Email</c>, so a role could be granted by a match on a column an Admin writes at
/// invite time. Not exploitable while every identity is a GUID-shaped <c>oid</c>, but the same
/// latent gap <see cref="CallerContext"/>'s own membership check had; both are fixed together.
/// Email keeps only its two other jobs.
/// </para>
/// </summary>
internal sealed class WorkspaceRoleResolver(
    IdentityWorkspaceDbContext dbContext, ITenantContext tenantContext, ICallerIdentity callerIdentity)
{
    /// <summary>
    /// The caller's role for <paramref name="tenantId"/>, or <see langword="null"/> when none can
    /// be established — no validated identity at all, or no live <c>workspace_membership</c> row for
    /// it in this tenant (ADR-010 w15 footer §3: "deleting costs six lines").
    /// </summary>
    public async Task<WorkspaceRoleName?> ResolveAsync(
        HttpContext httpContext, TenantId tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        // ADR-025 §A2: the one identity seam, instead of reading a header off
        // HttpContext/HttpRequest directly. Trusted for one thing only -- which membership rows to
        // look up; it confers no role, no tenant and no scope of its own.
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
                && user.ExternalSubjectId == identity
            select role.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return WorkspaceRoleClaimResolver.TryResolve(roleNames.Select(name => name.ToString()), out var resolved)
            ? resolved
            : null;
    }

    /// <summary>Convenience for the endpoints: "is this caller a Workspace Admin of this tenant?"</summary>
    public async Task<bool> IsAdminAsync(
        HttpContext httpContext, TenantId tenantId, CancellationToken cancellationToken = default) =>
        await ResolveAsync(httpContext, tenantId, cancellationToken).ConfigureAwait(false) == WorkspaceRoleName.Admin;
}
