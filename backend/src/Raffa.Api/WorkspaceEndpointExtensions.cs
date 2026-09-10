using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;

namespace Raffa.Api;

/// <summary>
/// Maps `POST /api/workspaces` (create) and `POST /api/workspaces/{tenantId}/invites` (invite) —
/// task E01/F09/US01/T01 (r0-integration): the R0 path's own "create workspace -&gt; invite" steps
/// already had working domain/infrastructure logic (<see cref="WorkspaceProvisioningService"/>,
/// <see cref="WorkspaceMembershipService"/>, tasks E01/F05/US01/T01 and T02), but no host endpoint
/// had called either one yet (see those types' own "not yet called by a host" doc comments).
/// Thin composition per ADR-002 — same shape as <see cref="AuditEndpointExtensions"/>.
///
/// Same interim-authentication placeholder as <c>Program</c>'s document endpoints: ADR-010 is not
/// in this task's "architecture decisions in force" list, so there is still no validated caller
/// principal to require here. Workspace creation is the pre-authentication signup step (nobody
/// has a tenant claim yet, by definition); the invite endpoint takes its target tenant from the
/// route rather than from a caller claim for the same reason document upload/read take theirs
/// from an explicit header today — see <c>Program.cs</c>'s own comment on why this gap is not
/// promoted to reports/open-questions.md by this task (a mid-wave append there has previously
/// broken a phase-barrier merge). <see cref="AuditEndpointExtensions"/>'s `GET /api/audit` is the
/// one endpoint that already assumes a validated <see cref="System.Security.Claims.ClaimsPrincipal"/>
/// — proving that path end-to-end is this task's integration test's job, via a test-only
/// principal, not a production auth handler this task is not scoped to add.
/// </summary>
public static class WorkspaceEndpointExtensions
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/workspaces", CreateWorkspaceAsync);
        endpoints.MapPost("/api/workspaces/{tenantId}/invites", InviteAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateWorkspaceAsync(
        CreateWorkspaceRequest request,
        WorkspaceProvisioningService provisioningService,
        CancellationToken cancellationToken)
    {
        var result = await provisioningService
            .CreateWorkspaceAsync(request.Name, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Results.BadRequest(result.Error);
        }

        var workspace = result.Value;
        return Results.Created($"/api/workspaces/{workspace.TenantId.Value}", new
        {
            id = workspace.TenantId.Value,
            name = workspace.Name,
            createdAt = workspace.CreatedAt,
        });
    }

    private static async Task<IResult> InviteAsync(
        string tenantId,
        InviteRequest request,
        WorkspaceMembershipService membershipService,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(tenantId, out var tenantGuid))
        {
            return Results.BadRequest("The tenant id in the route must be a GUID.");
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

        var result = await membershipService
            .InviteAsync(new TenantId(tenantGuid), request.Email, role, cancellationToken)
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
