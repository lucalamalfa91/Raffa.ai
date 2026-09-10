using System.Security.Claims;
using Raffa.Audit.Infrastructure;
using Raffa.Identity.Workspace.Domain;

namespace Raffa.Api;

/// <summary>
/// Maps `GET /api/audit` (product spec API table row "Authorized audit query"; story
/// us-02-audit-baseline AC-2, task E01/F06/US02/T02). Thin composition per ADR-002 — the actual
/// decisions are made by <see cref="WorkspacePrincipalAuthorization"/> (who is this caller, which
/// tenant/role) and <see cref="IAuditQueryService"/> (the tenant-scoped read); this file only maps
/// each outcome to an HTTP status code.
/// </summary>
public static class AuditEndpointExtensions
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/audit", GetAuditEventsAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAuditEventsAsync(
        ClaimsPrincipal user, IAuditQueryService auditQueryService, CancellationToken cancellationToken)
    {
        // Only a Workspace Admin may read the audit trail (product spec §3.1 role table: "audit
        // logs" is listed under Workspace Admin only, none of the other four roles). The tenant
        // scope itself always comes from the caller's own authorized identity, never from a
        // client-supplied query parameter — a `?tenantId=` here would be exactly the cross-tenant
        // query path ADR-009 forbids.
        if (!WorkspacePrincipalAuthorization.TryAuthorize(
                user, WorkspaceRoleName.Admin, out var tenantId, out var failure))
        {
            return failure == WorkspaceAuthorizationFailure.Unauthenticated
                ? Results.Unauthorized()
                : Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var events = await auditQueryService.GetEventsAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(events);
    }
}
