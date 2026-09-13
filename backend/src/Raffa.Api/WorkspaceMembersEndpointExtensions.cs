using Raffa.Api.Infrastructure;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Api;

/// <summary>
/// The workspace members route group (ADR-026 implication 6's file split; task E14/F02/US01/T01
/// created this file empty of routes, wired into <see cref="WorkspaceEndpointExtensions.MapWorkspaceEndpoints"/>
/// already). Task E14/F04/US01/T01 (wave w14, story us-01-members-api; ADR-026 §D3, ADR-025
/// §B/§D.4) lands `GET /api/workspaces/{tenantId}/members` — the roster.
/// `DELETE /api/workspaces/{tenantId}/members/{membershipId}` (remove, ADR-025 §D.5) is a later
/// task on this same file (phase 3, `E15/F01/US01/T01`).
///
/// <para>
/// Verify, then scope, then read (ADR-009 w14 footer clause 7): <see cref="IsLiveMemberAsync"/>
/// opens its own narrow tenant scope to confirm the caller holds a live <c>workspace_membership</c>
/// in the <b>route</b> tenant — never the client-declared tenant header ADR-022's w14 footer
/// demoted (clause 3; the ADR-026 §D3 amendment corrects that ADR's own header line to match) —
/// and that scope closes before
/// <see cref="WorkspaceMembershipService.ListMembersAsync"/> opens a second one to build the
/// roster. This deliberately duplicates the shape of
/// <see cref="WorkspaceInvitesEndpointExtensions.ResolveMembershipRoleAsync"/> rather than calling
/// <c>WorkspaceRoleResolver.ResolveAsync</c>: that method tries an authenticated principal's
/// claims, then the interim <c>X-Role</c>/<c>X-Workspace-Role</c> header, before it ever reaches
/// its own membership branch — a caller with no relationship to the route tenant could set a
/// static <c>X-Role: Admin</c> header and be treated as a member, reopening the class of hole
/// <c>WorkspaceInvitesEndpointExtensions</c> already exists to close (ADR-025 §H's non-negotiable
/// spoofed-header test, T2). Unlike that guard, this check never resolves *which* role the caller
/// holds — ADR-025 Rule D.4a grants read to <b>any</b> live member, so existence is the whole
/// question — so it is simpler: one <c>AnyAsync</c>, not a role-precedence join.
/// </para>
///
/// <para>
/// <b>Task E15/F01/US01/T01 (phase 3, wave w14; ADR-025 Rule D.5a-c, §H T7/T9)</b> adds
/// `DELETE /api/workspaces/{tenantId}/members/{membershipId}` — <see cref="RemoveMemberAsync"/> —
/// Admin only, with the last-Admin guard (<see cref="Domain.WorkspaceMembershipRemoval.CanRemove"/>).
/// Its own guard duplicates <see cref="WorkspaceInvitesEndpointExtensions.ResolveMembershipRoleAsync"/>'s
/// shape locally (<see cref="ResolveMembershipRoleAsync"/> below) rather than sharing it across
/// files, the same "one named method per file" convention <see cref="IsLiveMemberAsync"/> already
/// established for the read guard above.
/// </para>
/// </summary>
public static class WorkspaceMembersEndpointExtensions
{
    public static IEndpointRouteBuilder MapWorkspaceMemberEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/workspaces/{tenantId}/members", ListMembersAsync);
        endpoints.MapDelete("/api/workspaces/{tenantId}/members/{membershipId}", RemoveMemberAsync);
        return endpoints;
    }

    private static async Task<IResult> ListMembersAsync(
        string tenantId,
        ICallerIdentity callerIdentity,
        IdentityWorkspaceDbContext dbContext,
        ITenantContext tenantContext,
        WorkspaceMembershipService membershipService,
        CancellationToken cancellationToken)
    {
        // ADR-025 §B / ADR-009 w14 footer clause 7: no identity presented -> 401, never 400 --
        // absence of identity is an authentication failure, not a body/route validation failure.
        var identity = callerIdentity.Resolve();
        if (identity is null)
        {
            return Results.Unauthorized();
        }

        if (!Guid.TryParse(tenantId, out var tenantGuid))
        {
            return Results.BadRequest("The tenant id in the route must be a GUID.");
        }

        // ADR-022 w14 footer clause 3 / the ADR-026 §D3 amendment: the tenant is always the route
        // value -- the client-declared tenant header is never read on this endpoint, let alone
        // consulted (rg for it over this file returns nothing, by design).
        var routeTenantId = new TenantId(tenantGuid);

        // Verify, then scope, then read (ADR-009 w14 footer clause 7): confirm a live membership
        // for this identity in the route tenant with its own narrow scope, closed before the
        // roster read opens a second one. ADR-025 Rule D.4b: a non-member gets 404 -- never 403
        // (a tenant-existence oracle), never an empty 200 (also an oracle: "that tenant exists and
        // is empty") -- and the 404 below carries no body, so it names no tenant.
        var isMember = await IsLiveMemberAsync(
                dbContext, tenantContext, routeTenantId, identity, cancellationToken)
            .ConfigureAwait(false);
        if (!isMember)
        {
            return Results.NotFound();
        }

        // ADR-025 Rule D.4a: any live member reads, regardless of role -- the guard above already
        // proved membership, so no further role check gates this read.
        var members = await membershipService
            .ListMembersAsync(routeTenantId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new
        {
            members = members.Select(member => new
            {
                id = member.Id.Value,
                email = member.Email,
                name = member.Name,
                // ADR-026 §D3 amendment clause 3 / implication 7: role and status are non-nullable
                // strings on the wire, never an OpenAPI enum -- role names are per-tenant rows and
                // the generator loses `null` on a nullable enum.
                role = member.Role.ToString(),
                status = member.Status.ToString(),
                // 2026-09-11 fix (E15/F02/US01/T01's halt): the action ids -- exactly one is set,
                // matching `status` (Active -> membershipId, Invited -> invitationId). DELETE
                // .../members/{membershipId} and the invitation revoke each need their own id,
                // never `id` (WorkspaceUser's, stable across the transition, not an action target).
                membershipId = member.MembershipId?.Value,
                invitationId = member.InvitationId?.Value,
            }),
        });
    }

    /// <summary>
    /// ADR-025 Rule D.4a: any live membership qualifies, regardless of role — unlike
    /// <see cref="WorkspaceInvitesEndpointExtensions.ResolveMembershipRoleAsync"/> this never
    /// resolves *which* role the caller holds, only whether one exists, and never falls back to a
    /// claim or a role header. Mirrors that method's own tenant-scoped
    /// <c>workspace_user ⋈ workspace_membership</c> join (identical filter shape to
    /// <c>WorkspaceRoleResolver.cs:104-114</c>), keyed on <paramref name="callerIdentity"/>
    /// (already resolved by <see cref="ICallerIdentity"/>).
    /// </summary>
    private static async Task<bool> IsLiveMemberAsync(
        IdentityWorkspaceDbContext dbContext,
        ITenantContext tenantContext,
        TenantId tenantId,
        string callerIdentity,
        CancellationToken cancellationToken)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        return await (
            from user in dbContext.WorkspaceUsers
            join membership in dbContext.WorkspaceMemberships on user.Id equals membership.WorkspaceUserId
            where user.TenantId == tenantId
                && membership.TenantId == tenantId
                && (user.Email == callerIdentity || user.ExternalSubjectId == callerIdentity)
            select membership.Id)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// `DELETE /api/workspaces/{tenantId}/members/{membershipId}` (ADR-025 Rule D.5a-c): identity
    /// (401) → membership (404, never 403 — a tenant-existence oracle) → Admin (403) → the
    /// last-Admin guard (409) → 204. The removed identity's next request in this tenant sees the
    /// effect immediately (Rule D.5b: "nothing caches authorization") — this handler itself does
    /// nothing to make that true, it is simply what every other tenant-scoped read in this host
    /// already does (read role/membership from the database on every request).
    /// </summary>
    private static async Task<IResult> RemoveMemberAsync(
        string tenantId,
        string membershipId,
        ICallerIdentity callerIdentity,
        IdentityWorkspaceDbContext dbContext,
        ITenantContext tenantContext,
        WorkspaceMembershipService membershipService,
        CancellationToken cancellationToken)
    {
        var identity = callerIdentity.Resolve();
        if (identity is null)
        {
            return Results.Unauthorized();
        }

        if (!Guid.TryParse(tenantId, out var tenantGuid))
        {
            return Results.BadRequest("The tenant id in the route must be a GUID.");
        }

        if (!Guid.TryParse(membershipId, out var membershipGuid))
        {
            return Results.BadRequest("The membership id in the route must be a GUID.");
        }

        var routeTenantId = new TenantId(tenantGuid);

        var callerRole = await ResolveMembershipRoleAsync(
                dbContext, tenantContext, routeTenantId, identity, cancellationToken)
            .ConfigureAwait(false);

        if (callerRole is null)
        {
            return Results.NotFound();
        }

        if (callerRole != WorkspaceRoleName.Admin)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var result = await membershipService
            .RemoveMemberAsync(routeTenantId, new EntityId(membershipGuid), identity, cancellationToken)
            .ConfigureAwait(false);

        return result.Status switch
        {
            MembershipOperationStatus.Success => Results.NoContent(),
            MembershipOperationStatus.Conflict => Results.Conflict(result.Error),
            _ => Results.NotFound(),
        };
    }

    /// <summary>
    /// The removal guard's membership-only role lookup — identical shape to
    /// <see cref="WorkspaceInvitesEndpointExtensions.ResolveMembershipRoleAsync"/> (see that
    /// method's own doc comment for why this does not call <c>WorkspaceRoleResolver.ResolveAsync</c>),
    /// duplicated locally rather than shared across files, the same convention
    /// <see cref="IsLiveMemberAsync"/> above already established for this file's own read guard.
    /// </summary>
    private static async Task<WorkspaceRoleName?> ResolveMembershipRoleAsync(
        IdentityWorkspaceDbContext dbContext,
        ITenantContext tenantContext,
        TenantId tenantId,
        string callerIdentity,
        CancellationToken cancellationToken)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var roleNames = await (
            from user in dbContext.WorkspaceUsers
            join membership in dbContext.WorkspaceMemberships on user.Id equals membership.WorkspaceUserId
            join role in dbContext.WorkspaceRoles on membership.WorkspaceRoleId equals role.Id
            where user.TenantId == tenantId
                && membership.TenantId == tenantId
                && role.TenantId == tenantId
                && (user.Email == callerIdentity || user.ExternalSubjectId == callerIdentity)
            select role.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return WorkspaceRoleClaimResolver.TryResolve(roleNames.Select(name => name.ToString()), out var resolved)
            ? resolved
            : null;
    }
}
