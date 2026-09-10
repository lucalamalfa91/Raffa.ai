using Raffa.Savings.Application;
using Raffa.SharedKernel;

namespace Raffa.Api;

/// <summary>
/// Maps `GET /api/savings` (list) and `PATCH /api/savings/{id}` (update status/owner/realized
/// value) — product spec §4.3 "Create a trackable SavingsOpportunity with status, owner and
/// realized outcome"; spec Appendix A "PATCH /api/savings/{id} | Status/owner/realized value";
/// module-map.md "Savings | SavingsOpportunity, RealizedSavings | /api/savings"; story
/// us-02-savings-opportunity AC-1/AC-3, task E04/F02/US02/T01 (savings-opportunity) and task
/// E04/F02/US02/T02 (realized-savings). Thin composition per ADR-002:
/// <see cref="SavingsOpportunityService"/> owns every persistence/validation/audit
/// decision; this file only translates HTTP &lt;-&gt; the service call, same shape as
/// <see cref="RenewalsEndpointExtensions"/>/<see cref="ContractsEndpointExtensions"/>.
///
/// Task E04/F03/US01/T02 (savings-list, the wave-spec's own artifact of that name) — parent story
/// us-01-savings-kpis AC-2 ("`GET /api/savings` returns the opportunity list with tenant scoping",
/// already true since task E04/F02/US02/T01, unchanged here) and AC-3 ("Returns provenance +
/// confidence, never fabricated precision") — adds <c>confidenceLevel</c> to
/// <see cref="ToResponse"/>'s wire shape; see <see cref="SavingsOpportunityResult.ConfidenceLevel"/>'s
/// own doc comment for why that tier, and not the fuller benchmark-shaped
/// <see cref="Raffa.Savings.Application.SavingsProvenance"/>, is the honest provenance signal to
/// add here.
///
/// Same interim `X-Tenant-Id` header placeholder as every other endpoint in this host (ADR-010 is
/// not in this task's "Architecture decisions in force" list, so there is no validated caller
/// principal yet) — see <c>Program.cs</c>'s own comment on why this interim gap is not promoted to
/// reports/open-questions.md by these tasks.
/// </summary>
public static class SavingsEndpointExtensions
{
    public static IEndpointRouteBuilder MapSavingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/savings", GetSavingsOpportunitiesAsync);
        endpoints.MapPatch("/api/savings/{id}", PatchSavingsOpportunityAsync);
        return endpoints;
    }

    /// <summary>
    /// `GET /api/savings` (AC-1 "lists opportunities"; parent story us-01-savings-kpis AC-2/AC-3) —
    /// every opportunity for the caller's tenant, newest identified first. Never 404s (an empty list
    /// is a valid, honest answer for a tenant with none yet) — same convention <c>GET /api/audit</c>'s
    /// own tenant-scoped read follows. Tenant scoping is <see cref="SavingsOpportunityService.ListAsync"/>'s
    /// own application-level `WHERE tenant_id` filter plus the non-bypassable RLS backstop (ADR-009;
    /// proven by <c>Raffa.Savings.Tests.SavingsOpportunityRlsCrossTenantIsolationTests</c>) — nothing
    /// new for task E04/F03/US01/T02 to add there. See <see cref="ToResponse"/> for this task's own
    /// addition (AC-3's confidence tier).
    /// </summary>
    private static async Task<IResult> GetSavingsOpportunitiesAsync(
        HttpRequest request,
        SavingsOpportunityService savingsOpportunityService,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeaderValues)
            || !Guid.TryParse(tenantHeaderValues.ToString(), out var tenantGuid))
        {
            return Results.BadRequest("A valid 'X-Tenant-Id' header (a GUID) is required.");
        }

        var tenantId = new TenantId(tenantGuid);

        var opportunities = await savingsOpportunityService
            .ListAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new
        {
            items = opportunities.Select(ToResponse),
            totalCount = opportunities.Count,
        });
    }

    /// <summary>
    /// `PATCH /api/savings/{id}` (AC-1 "updates status/owner..."; AC-3 "realized value is captured
    /// and audit-tracked", task E04/F02/US02/T02). 400 for a missing/invalid tenant header or route
    /// id (same guard shape as every other endpoint in this file/host), 404 when
    /// <see cref="SavingsOpportunityService.NotFoundError"/> comes back (no such opportunity for
    /// this tenant — same <c>Result&lt;T&gt;.Error</c>-sentinel-to-404 convention
    /// <see cref="ContractsEndpointExtensions"/>'s own `PATCH /api/contracts/{id}` handler already
    /// uses for <c>ContractCorrectionService.ContractNotFoundError</c>), 400 with
    /// <see cref="Result{T}.Error"/> for every other validation failure (empty owner, unrecognized
    /// status, a negative <c>realizedAmount</c>, <c>realizedAmount</c> combined with a
    /// contradictory explicit <c>status</c>, or none of the three fields supplied).
    /// </summary>
    private static async Task<IResult> PatchSavingsOpportunityAsync(
        string id,
        SavingsOpportunityPatchRequest request,
        HttpRequest httpRequest,
        SavingsOpportunityService savingsOpportunityService,
        CancellationToken cancellationToken)
    {
        if (!httpRequest.Headers.TryGetValue("X-Tenant-Id", out var tenantHeaderValues)
            || !Guid.TryParse(tenantHeaderValues.ToString(), out var tenantGuid))
        {
            return Results.BadRequest("A valid 'X-Tenant-Id' header (a GUID) is required.");
        }

        if (!Guid.TryParse(id, out var opportunityGuid))
        {
            return Results.BadRequest("The savings opportunity id in the route must be a GUID.");
        }

        var result = await savingsOpportunityService.UpdateAsync(
            new TenantId(tenantGuid),
            new EntityId(opportunityGuid),
            request.Owner,
            request.Status,
            request.RealizedAmount,
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return string.Equals(result.Error, SavingsOpportunityService.NotFoundError, StringComparison.Ordinal)
                ? Results.NotFound()
                : Results.BadRequest(result.Error);
        }

        return Results.Ok(ToResponse(result.Value));
    }

    /// <summary>
    /// Wire-shapes <see cref="SavingsOpportunityResult"/> (AC-2's own captured-field list). Enum
    /// members and <see cref="EntityId"/>? wrapper values are projected to plain strings/GUIDs —
    /// the same convention <see cref="RenewalsEndpointExtensions"/>/<see cref="PortfolioEndpointExtensions"/>
    /// already use. Shared by both `GET /api/savings` (list) and `PATCH /api/savings/{id}`, so task
    /// E04/F03/US01/T02's own <c>confidenceLevel</c> addition below covers both responses identically.
    /// </summary>
    private static object ToResponse(SavingsOpportunityResult result) => new
    {
        id = result.Id.Value,
        supplierId = result.SupplierId?.Value,
        contractId = result.ContractId?.Value,
        type = result.Type,
        currentSpend = result.CurrentSpend,
        currency = result.Currency,
        estimatedSavingsLow = result.EstimatedSavingsLow,
        estimatedSavingsHigh = result.EstimatedSavingsHigh,
        confidence = result.Confidence,
        // Task E04/F03/US01/T02 (savings-list): parent story AC-3 "Returns provenance + confidence,
        // never fabricated precision" -- the qualitative High/Medium/Low tier alongside the raw
        // score above, so a caller never has to interpret a bare decimal unaided. See
        // SavingsOpportunityResult.ConfidenceLevel's own doc comment for why this tier, and not a
        // fuller source/sample-size/updated-at provenance object this opportunity does not actually
        // have on file, is what is added here.
        confidenceLevel = result.ConfidenceLevel.ToString(),
        status = result.Status.ToString(),
        owner = result.Owner,
        createdAt = result.CreatedAt,
        updatedAt = result.UpdatedAt,
        // Task E04/F02/US02/T02 (realized-savings): non-null only on the PATCH response that just
        // recorded it -- see SavingsOpportunityResult.RealizedAmount's own doc comment.
        realizedAmount = result.RealizedAmount,
    };
}
