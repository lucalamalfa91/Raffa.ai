using Raffa.Api.Infrastructure;
using Raffa.Identity.Workspace.Infrastructure;

namespace Raffa.Api;

/// <summary>
/// Maps `POST /api/workspaces` (create) and the two route-group mappers ADR-026 implication 6
/// splits out of what used to be this whole file (task E14/F02/US01/T01, wave w14 "workspace is
/// real"): <see cref="WorkspaceMembersEndpointExtensions.MapWorkspaceMemberEndpoints"/> and
/// <see cref="WorkspaceInvitesEndpointExtensions.MapWorkspaceInviteEndpoints"/>.
/// <see cref="MapWorkspaceEndpoints"/> stays the single entry point `Program.cs:225` calls — that
/// line is untouched by this task — so the three later w14/w15 tasks that add workspace routes
/// (`E14/F02/US02/T01`, `E14/F03/US01/T01`, `E15/F01/US01/T01`) only ever touch their own group
/// file, never `Program.cs` or a file a sibling phase-1 task also writes.
///
/// The invite handler that used to live here moved to <see cref="WorkspaceInvitesEndpointExtensions"/>
/// together with the ADR-025 §D.1a authorization guard this task adds in front of it — on the
/// wave's base checkout `POST /api/workspaces/{tenantId}/invites` had no authorization at all
/// (ADR-025 Context fact 1).
///
/// ADR-025 Rule D.2a changes this endpoint's own posture: creating a workspace used to be the
/// deliberately anonymous pre-authentication signup step (task E01/F09/US01/T01: "nobody has a
/// tenant claim yet, by definition"); it now writes an identity-keyed grant — the creator's own
/// Admin membership, Rule D.2b — so a presented identity is required. Absent, the caller gets
/// <b>401</b> (<c>Results.Unauthorized()</c>), never 400 (ADR-025 §B: "absence of identity is an
/// authentication failure, not a body validation failure"). Every endpoint this wave adds or
/// changes consumes the one <see cref="ICallerIdentity"/> seam (ADR-025 §A1) instead of reading
/// <c>HttpRequest.Headers</c> directly.
/// </summary>
public static class WorkspaceEndpointExtensions
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/workspaces", CreateWorkspaceAsync);
        endpoints.MapWorkspaceMemberEndpoints();
        endpoints.MapWorkspaceInviteEndpoints();
        return endpoints;
    }

    private static async Task<IResult> CreateWorkspaceAsync(
        CreateWorkspaceRequest request,
        ICallerIdentity callerIdentity,
        WorkspaceProvisioningService provisioningService,
        CancellationToken cancellationToken)
    {
        // ADR-025 Rule D.2a: absent identity is an authentication failure (401), not a body
        // validation failure (400) — the endpoint now writes an identity-keyed grant.
        var identity = callerIdentity.Resolve();
        if (identity is null)
        {
            return Results.Unauthorized();
        }

        var result = await provisioningService
            .CreateWorkspaceAsync(request.Name, identity, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Results.BadRequest(result.Error);
        }

        var workspace = result.Value;
        // ADR-025 Rule D.2b: the creator is Admin of the tenant they just created, unconditionally
        // — CreateWorkspaceRequest carries no `role` property at all, so nothing in the body could
        // ever be honoured as a self-assigned role (AC-3). ADR-026 §D5: the 201 carries the role so
        // the SPA enters the new workspace without a second call.
        return Results.Created($"/api/workspaces/{workspace.TenantId.Value}", new
        {
            id = workspace.TenantId.Value,
            name = workspace.Name,
            createdAt = workspace.CreatedAt,
            role = "Admin",
        });
    }
}
