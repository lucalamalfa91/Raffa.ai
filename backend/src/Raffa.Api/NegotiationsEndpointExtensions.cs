using Raffa.Quotes.Application.Outcome;
using Raffa.SharedKernel;

namespace Raffa.Api;

/// <summary>
/// Maps `POST /api/negotiations/outcomes` (product spec Appendix A API table: "Record outcome";
/// spec §12.2 "Negotiation outcome capture"; module-map.md "Quotes | Quote, QuoteLine, Assessment,
/// NegotiationOutcome | /api/quotes, /api/negotiations/outcomes"; parent story
/// us-02-outcome-capture AC-1, task E05/F03/US02/T01, negotiation-outcome). Thin composition per
/// ADR-002 — <see cref="NegotiationOutcomeService"/> owns every validation/persistence/audit
/// decision; this file only translates HTTP &lt;-&gt; that call, the same shape
/// <see cref="QuotesEndpointExtensions"/>/<see cref="SavingsEndpointExtensions"/> already use.
///
/// Same interim `X-Tenant-Id` header placeholder as every other endpoint in this host (ADR-010 is
/// not in this task's "Architecture decisions in force" list, so there is no validated caller
/// principal yet) — see <c>Program.cs</c>'s own comment on why this interim gap is not promoted to
/// reports/open-questions.md by these tasks.
///
/// <para>
/// Task E05/F03/US02/T02 (outcome-propagation; parent story AC-2 "Realized savings surface on the
/// savings dashboard (cross-wave)"): when <see cref="NegotiationOutcomeCaptureRequest
/// .SavingsOpportunityId"/> is supplied, <see cref="CaptureOutcomeAsync"/> also calls
/// <see cref="NegotiationOutcomePropagationService.PropagateAsync"/> right after the capture itself
/// succeeds, still inside the same request — see that type's own doc comment for why this runs
/// synchronously here rather than as a separate endpoint/queued job.
/// </para>
/// </summary>
public static class NegotiationsEndpointExtensions
{
    public static IEndpointRouteBuilder MapNegotiationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/negotiations/outcomes", CaptureOutcomeAsync);
        return endpoints;
    }

    /// <summary>
    /// AC-1 ("`POST /api/negotiations/outcomes` records original/target/final/saving/discount/
    /// duration/levers"): a plain JSON body (unlike `POST /api/quotes`'s multipart upload) —
    /// <paramref name="request"/> binds directly from it (minimal API's default complex-type-as-body
    /// inference, the same mechanism <c>SavingsEndpointExtensions.PatchSavingsOpportunityAsync</c>'s
    /// own <c>SavingsOpportunityPatchRequest</c> parameter already relies on). 404 when
    /// <see cref="NegotiationOutcomeService.QuoteNotFoundError"/> comes back (no such quote for this
    /// tenant — same <c>Result&lt;T&gt;.Error</c>-sentinel-to-404 convention
    /// <see cref="SavingsEndpointExtensions"/>'s own `PATCH /api/savings/{id}` handler already uses
    /// for <c>SavingsOpportunityService.NotFoundError</c>), 400 with <see cref="Result{T}.Error"/>
    /// for every other validation failure.
    ///
    /// <para>
    /// Task E05/F03/US02/T02 (outcome-propagation): a <c>savingsOpportunityId</c> supplied on
    /// <paramref name="request"/> triggers a second, best-effort step after the capture itself is
    /// already durable — its own success/failure is reported as <c>savingsPropagated</c>/
    /// <c>savingsPropagationError</c> on the <em>same</em> 201 response, never as a distinct HTTP
    /// error: the outcome capture already succeeded and is already audit-tracked (AC-3), so a bad or
    /// unknown <c>savingsOpportunityId</c> must not turn an already-durable write into a client-visible
    /// failure (see <see cref="NegotiationOutcomePropagationService"/>'s own "never fails an
    /// already-durable capture" doc comment).
    /// </para>
    /// </summary>
    private static async Task<IResult> CaptureOutcomeAsync(
        NegotiationOutcomeCaptureRequest request,
        HttpRequest httpRequest,
        NegotiationOutcomeService outcomeService,
        NegotiationOutcomePropagationService propagationService,
        CancellationToken cancellationToken)
    {
        if (!httpRequest.Headers.TryGetValue("X-Tenant-Id", out var tenantHeaderValues)
            || !Guid.TryParse(tenantHeaderValues.ToString(), out var tenantGuid))
        {
            return Results.BadRequest("A valid 'X-Tenant-Id' header (a GUID) is required.");
        }

        var tenantId = new TenantId(tenantGuid);

        var result = await outcomeService.CaptureAsync(tenantId, request, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return string.Equals(result.Error, NegotiationOutcomeService.QuoteNotFoundError, StringComparison.Ordinal)
                ? Results.NotFound(result.Error)
                : Results.BadRequest(result.Error);
        }

        var outcome = result.Value;

        bool? savingsPropagated = null;
        string? savingsPropagationError = null;
        if (outcome.SavingsOpportunityId is { } savingsOpportunityId)
        {
            var propagationResult = await propagationService.PropagateAsync(
                tenantId, outcome.Id, savingsOpportunityId, cancellationToken).ConfigureAwait(false);

            savingsPropagated = propagationResult.IsSuccess;
            savingsPropagationError = propagationResult.IsFailure ? propagationResult.Error : null;
        }

        return Results.Created($"/api/negotiations/outcomes/{outcome.Id.Value}", new
        {
            id = outcome.Id.Value,
            quoteId = outcome.QuoteId.Value,
            originalQuoteTotal = outcome.OriginalQuoteTotal,
            targetPrice = outcome.TargetPrice,
            finalPrice = outcome.FinalPrice,
            realizedSaving = outcome.RealizedSaving,
            discountPercent = outcome.DiscountPercent,
            negotiationDurationDays = outcome.NegotiationDurationDays,
            leversUsed = outcome.LeversUsed.Select(l => l.ToString()),
            capturedAt = outcome.CapturedAt,
            savingsOpportunityId = outcome.SavingsOpportunityId?.Value,
            savingsPropagated,
            savingsPropagationError,
        });
    }
}
