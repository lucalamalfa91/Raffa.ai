using Raffa.Api.Infrastructure;
using Raffa.Audit.Infrastructure;

namespace Raffa.Api;

/// <summary>
/// Maps `GET /api/audit` (product spec API table row "Authorized audit query"; story
/// us-02-audit-baseline AC-2, task E01/F06/US02/T02; wave w16 NW-08, story
/// us-02-audit-read-for-a-real-admin, task E18/F02/US02/T01, ADR-025 §K). Thin composition per
/// ADR-002 — the actual decisions are made by <see cref="ICallerContext"/> (identity, the
/// <c>X-Tenant-Id</c> selector, membership) and <see cref="WorkspaceRoleResolver"/> (is this member
/// an Admin) plus <see cref="IAuditQueryService"/> (the tenant-scoped read); this file only maps
/// each outcome to an HTTP status code.
///
/// <para>
/// <b>The ladder is the one every other tenant-scoped route already uses</b> (verbatim,
/// <c>DocumentsEndpointExtensions</c>'s own reprocess handler; ADR-025 §K.1, security S16-4): no
/// validated token → 401; missing/non-GUID <c>X-Tenant-Id</c> → 400; a well-formed tenant with no
/// live membership → 404, never 403 (ADR-025 Rule B1 — a 403 there is a tenant-existence oracle); a
/// live member who is not Admin → 403; a live Admin membership → 200, that tenant's rows only.
/// <c>WorkspacePrincipalAuthorization</c> — the claims-based guard this route used before wave w16
/// (a <c>tenant_id</c> claim plus a <c>ClaimTypes.Role</c> claim, forbidden as an authorization
/// source by ADR-010's w14 footer, and unreachable by any real client since nothing in this host
/// ever minted either claim) — is deleted whole, not merely bypassed (ADR-025 §K.2/S16-5): keeping
/// the type around would leave a working, fail-closed, helpfully-named claims authorizer in this
/// assembly for the next endpoint to pick up, which is exactly how this defect arrived.
/// </para>
///
/// <para>
/// <c>X-Tenant-Id</c> survives as a header rather than a claim, but its role does not weaken: it is
/// a caller-supplied <b>selector</b>, verified against the caller's own membership before any row is
/// read — never an authorization input by itself (ADR-026 w16 clause 2). The route's old "no
/// <c>?tenantId=</c> query parameter" property survives in that stronger form.
/// </para>
/// </summary>
public static class AuditEndpointExtensions
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/audit", GetAuditEventsAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAuditEventsAsync(
        HttpContext httpContext,
        IAuditQueryService auditQueryService,
        WorkspaceRoleResolver roleResolver,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        // NW-08 (ADR-025 §K.1; ADR-010 w15 footer §3/§I): identity, then the tenant header as an
        // authorized selector, then membership -- 401 / 400 / 404 in that order, all owned by
        // ICallerContext, exactly like every other tenant-scoped route in this host. The scope it
        // hands back is the tenant scope this handler (and IAuditQueryService, which defensively
        // opens its own nested scope for the same tenant) run in; disposing it here is the same
        // lifetime every other handler already manages for its own tenant scope.
        var caller = await callerContext.ResolveTenantAsync(httpContext.Request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantId = caller.TenantId;

        // Only a Workspace Admin may read the audit trail (product spec §3.1 role table: "audit
        // logs" is listed under Workspace Admin only, none of the other four roles). A live member
        // who is not Admin is 403 -- 404 is reserved for a caller who is not a member at all
        // (already ruled out by ResolveTenantAsync above, ADR-025 Rule B1).
        if (!await roleResolver.IsAdminAsync(httpContext, tenantId, cancellationToken))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var events = await auditQueryService.GetEventsAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(events);
    }
}
