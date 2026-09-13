using Raffa.Api.Infrastructure;
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
/// </summary>
public static class WorkspaceMembersEndpointExtensions
{
    public static IEndpointRouteBuilder MapWorkspaceMemberEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/workspaces/{tenantId}/members", ListMembersAsync);
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
}
