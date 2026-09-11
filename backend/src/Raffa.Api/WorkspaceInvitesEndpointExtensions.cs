using Raffa.Api.Infrastructure;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;

namespace Raffa.Api;

/// <summary>
/// Maps `POST /api/workspaces/{tenantId}/invites` (task E14/F02/US01/T01, wave w14; ADR-025 §D.1a —
/// "the highest-priority security task of the wave"). Moved out of
/// <see cref="WorkspaceEndpointExtensions"/> together with the guard this task adds in front of it:
/// on the wave's base checkout the route took a body, the service and a `CancellationToken` only —
/// no `HttpContext`, no claim, no membership check — so anyone who could reach the API and guess a
/// tenant GUID could grant themselves Admin over another customer's contracts
/// (`WorkspaceEndpointExtensions.cs:59-97` pre-wave; ADR-025 Context fact 1). It has been
/// survivable only because discovery was a `localStorage` array in one browser — NW-01/NW-02 are
/// exactly what make it live, which is why the guard ships one phase earlier than either.
///
/// <para>
/// The guard (ADR-025 Rule D.1a), in order: a presented identity (<see cref="ICallerIdentity"/>) —
/// else <b>401</b>; a live <c>workspace_membership</c> for that identity in the <b>route</b> tenant,
/// resolved through the existing membership branch of <see cref="WorkspaceRoleResolver"/> — else
/// <b>404</b>, never 403 (a 403 on a tenant the caller does not belong to is a tenant-existence
/// oracle, ADR-025 §B Rule B1); that membership's role is <see cref="WorkspaceRoleName.Admin"/> —
/// else <b>403</b>. The tenant is always the <b>route</b> value, never `X-Tenant-Id` (ADR-022 w14
/// footer clause 3: "not an input to a membership route"). ADR-025 Rule D.1b: an Admin may invite
/// another Admin — what is refused is a non-Admin reaching the endpoint at all, and any
/// self-assignment (the caller's own role is never read from a body, anywhere in this wave).
/// </para>
///
/// <para>
/// <see cref="WorkspaceRoleResolver"/> itself is unedited by this task — its header branch
/// (`X-Role`/`X-Workspace-Role`) is demoted by ADR-022/ADR-025 but the code deletion is phase 2's
/// `E14/F02/US02/T01`, not here (single-writer per phase; see this task's own "do not touch" list).
/// <see cref="WorkspaceMembershipService.InviteAsync"/> is also unedited: it still writes a
/// <b>live</b> membership at invite time in phase 1 — ADR-025's "an invitation is an offer, not a
/// grant" redesign (the pending `workspace_invitation` table) is phase 3's `E15/F01/US01/T01`. This
/// task's whole job is the guard in front of the existing write, not a new invite lifecycle.
/// </para>
/// </summary>
public static class WorkspaceInvitesEndpointExtensions
{
    public static IEndpointRouteBuilder MapWorkspaceInviteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/workspaces/{tenantId}/invites", InviteAsync);
        return endpoints;
    }

    private static async Task<IResult> InviteAsync(
        string tenantId,
        InviteRequest request,
        HttpContext httpContext,
        ICallerIdentity callerIdentity,
        WorkspaceRoleResolver roleResolver,
        WorkspaceMembershipService membershipService,
        CancellationToken cancellationToken)
    {
        // ADR-025 Rule D.1a, step 1: no identity presented -> 401.
        if (callerIdentity.Resolve() is null)
        {
            return Results.Unauthorized();
        }

        if (!Guid.TryParse(tenantId, out var tenantGuid))
        {
            return Results.BadRequest("The tenant id in the route must be a GUID.");
        }

        // ADR-025 Rule D.1c / ADR-022 w14 footer clause 3: the tenant is the route value, verified
        // against membership — X-Tenant-Id is not an input to this endpoint.
        var routeTenantId = new TenantId(tenantGuid);

        // ADR-025 Rule D.1a, steps 2-3, resolved through WorkspaceRoleResolver's existing
        // membership branch rather than a second, divergent lookup: null -> no live membership in
        // this tenant for this identity -> 404, never 403 (§B Rule B1: a 403 on a tenant the
        // caller does not belong to is a tenant-existence oracle); any non-Admin role -> 403.
        var callerRole = await roleResolver
            .ResolveAsync(httpContext, routeTenantId, cancellationToken)
            .ConfigureAwait(false);

        if (callerRole is null)
        {
            return Results.NotFound();
        }

        if (callerRole != WorkspaceRoleName.Admin)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Results.BadRequest("An 'email' is required.");
        }

        if (!WorkspaceRoleClaimResolver.TryResolve(request.Role, out var role))
        {
            return Results.BadRequest(
                $"'{request.Role}' is not a recognized role (Admin/Procurement/Legal/Finance/ReadOnly).");
        }

        // ADR-025 Rule D.1b: role comes from the body unconstrained beyond the catalog check above
        // — an Admin may invite another Admin. Only reaching this line was gated.
        var result = await membershipService
            .InviteAsync(routeTenantId, request.Email, role, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Results.BadRequest(result.Error);
        }

        var membership = result.Value;
        return Results.Created($"/api/workspaces/{tenantId}/invites/{membership.Id.Value}", new
        {
            id = membership.Id.Value,
            email = request.Email,
            role = role.ToString(),
        });
    }
}
