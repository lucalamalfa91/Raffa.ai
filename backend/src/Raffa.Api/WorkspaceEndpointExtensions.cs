using Raffa.Api.Infrastructure;
using Raffa.Documents.Contracts.Application;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Api;

/// <summary>
/// Maps `GET`/`POST /api/workspaces` and the two route-group mappers ADR-026 implication 6 splits
/// out of what used to be this whole file (task E14/F02/US01/T01, wave w14 "workspace is real"):
/// <see cref="WorkspaceMembersEndpointExtensions.MapWorkspaceMemberEndpoints"/> and
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
/// ADR-025 Rule D.2a changes the create endpoint's own posture: creating a workspace used to be the
/// deliberately anonymous pre-authentication signup step (task E01/F09/US01/T01: "nobody has a
/// tenant claim yet, by definition"); it now writes an identity-keyed grant — the creator's own
/// Admin membership, Rule D.2b — so a presented identity is required. Absent, the caller gets
/// <b>401</b> (<c>Results.Unauthorized()</c>), never 400 (ADR-025 §B: "absence of identity is an
/// authentication failure, not a body validation failure"). Every endpoint this wave adds or
/// changes consumes the one <see cref="ICallerIdentity"/> seam (ADR-025 §A1) instead of reading
/// <c>HttpRequest.Headers</c> directly.
///
/// <para>
/// Task E14/F03/US01/T01 (wave w14, NW-01/NW-09; ADR-026 §D1/§D2) adds `GET /api/workspaces` —
/// <see cref="ListWorkspacesAsync"/> — the wave's one identity-scope call site (ADR-025 §F.2): the
/// <b>only</b> handler in the product that opens an <see cref="ICallerIdentityContext"/> scope,
/// independent of (and orthogonal to) the per-candidate tenant scopes
/// <see cref="WorkspaceDirectoryService"/> opens and disposes inside it. <see cref="CreateWorkspaceAsync"/>
/// also gained `industry`/`country` on its request body this same task (NW-24, ADR-003 w14 footer).
/// </para>
/// </summary>
public static class WorkspaceEndpointExtensions
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/workspaces", ListWorkspacesAsync);
        endpoints.MapPost("/api/workspaces", CreateWorkspaceAsync);
        endpoints.MapWorkspaceMemberEndpoints();
        endpoints.MapWorkspaceInviteEndpoints();
        return endpoints;
    }

    /// <summary>
    /// `GET /api/workspaces` (ADR-026 §D1). Accepts no tenant header of any kind, ever — no route
    /// value, no header parameter, nothing read anywhere in this method — so acceptance N5 holds by
    /// construction rather than by a guard that could be forgotten. Empty result is 200 + `[]`,
    /// never 404 (AC-3): "you have none, create one" and "the request failed" are two different
    /// screens.
    /// </summary>
    private static async Task<IResult> ListWorkspacesAsync(
        ICallerIdentity callerIdentity,
        ICallerIdentityContext callerIdentityContext,
        WorkspaceDirectoryService directoryService,
        PortfolioQueryService portfolioQueryService,
        CancellationToken cancellationToken)
    {
        // ADR-025 §B: absence of identity is an authentication failure (401), the same posture
        // CreateWorkspaceAsync below already established for this file.
        var identity = callerIdentity.Resolve();
        if (identity is null)
        {
            return Results.Unauthorized();
        }

        // The wave's one identity-scope call site (see this type's own doc comment). Stays open
        // across the whole two-phase read WorkspaceDirectoryService performs *and* the
        // per-workspace count join below, and is disposed with this handler, never later — the
        // per-candidate tenant scopes WorkspaceDirectoryService opens internally are independent of
        // it (E14/F01/US01/T01 bullet 5) and need it open for none of their own reads.
        using var identityScope = callerIdentityContext.BeginIdentityScope(identity);

        var directoryResult = await directoryService
            .DiscoverForIdentityAsync(identity, cancellationToken)
            .ConfigureAwait(false);

        var rows = new List<object>(directoryResult.Workspaces.Count);
        foreach (var workspace in directoryResult.Workspaces)
        {
            // ADR-026 §D2: composition happens here, in the host — Raffa.Identity.Workspace may
            // reference only Raffa.SharedKernel (DependencyDirectionTests.cs:62, enforced), so it
            // cannot see Documents.Contracts. One scoped count query per workspace — N+1 by
            // construction, capped at MaxCandidates (ADR-026 Consequences: "known and accepted",
            // precedent SavingsKpiEndpointExtensions.cs:73-88).
            var contractCount = await portfolioQueryService
                .CountValidatedContractsAsync(workspace.TenantId, cancellationToken)
                .ConfigureAwait(false);

            rows.Add(new
            {
                id = workspace.TenantId.Value,
                name = workspace.Name,
                createdAt = workspace.CreatedAt,
                // No roleLabel on the row — the picker derives its tag from role (council decision
                // carried into the story). A plain string, never an enum (ADR-026 Amendment #3):
                // workspace_role.name is a per-tenant row, not a closed set the client can validate.
                role = workspace.Role.ToString(),
                contractCount,
                country = workspace.Country,
                currency = workspace.Currency,
            });
        }

        // Fix 2026-09-14: a tenant where this identity holds no membership yet but does hold a live
        // invitation (WorkspaceDirectoryService.DiscoverForIdentityAsync) -- normally empty, since
        // most callers either belong somewhere or nowhere. The client's own follow-up call is
        // `POST /api/workspaces/{tenantId}/invites/accept`, which re-verifies this from scratch
        // rather than trusting this row as a grant.
        var pendingInvitationRows = directoryResult.PendingInvitations
            .Select(pending => new
            {
                tenantId = pending.TenantId.Value,
                workspaceName = pending.WorkspaceName,
                role = pending.Role.ToString(),
            })
            .ToList();

        return Results.Ok(new { workspaces = rows, pendingInvitations = pendingInvitationRows });
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


        // Fix 2026-09-14 (NW-05, ADR-010 w15 footer §2.1): Resolve() returns the token's `oid`
        // since w15, but the creator row's Email column needs a real address -- passing the `oid`
        // made every real sign-in fail with "'<guid>' is not a valid email address to invite." and
        // left `dev` unable to create a workspace at all. The address comes from the token's own
        // `email` claim (requested as an optional claim on the access token by
        // infra/modules/identity), never from `preferred_username`/`upn`, which is the mangled
        // #EXT# value for a B2B guest. The `oid` still goes down as the creator's
        // ExternalSubjectId, which is what every later membership lookup matches on.
        var email = callerIdentity.ResolveEmail();
        if (string.IsNullOrWhiteSpace(email))
        {
            return Results.BadRequest(
                "Your sign-in token carries no email address, so a workspace cannot be created for it. " +
                "Sign out and sign in again; if that does not help, the API app registration is " +
                "missing its optional `email` claim.");
        }

        var result = await provisioningService
            .CreateWorkspaceAsync(request.Name, email, request.Industry, request.Country, callerObjectId: identity, cancellationToken: cancellationToken)
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
