using Raffa.Api.Infrastructure;
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
/// synchronously here rather than as a separate endpoint/queued job. Task E19/F04/US01/T01
/// (outcome-resolves-the-opportunity; ADR-028 §D5 clause 2) fills the gap that left this
/// unreachable from the product's own single caller: absent an explicit id,
/// <see cref="CaptureOutcomeAsync"/> asks
/// <see cref="NegotiationOutcomePropagationService.ResolveSavingsOpportunityIdAsync"/> to resolve
/// one from the quote's own supplier name before ever calling <c>PropagateAsync</c> — see
/// <see cref="CaptureOutcomeAsync"/>'s own doc comment for the corrected null/true/false rule.
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
    /// Task E05/F03/US02/T02 (outcome-propagation) plus task E19/F04/US01/T01
    /// (outcome-resolves-the-opportunity; ADR-028 §D5): after the capture itself is already durable,
    /// a second, best-effort step tries to link the outcome to a <c>Domain.SavingsOpportunity</c> —
    /// an explicit <c>savingsOpportunityId</c> on <paramref name="request"/> is used verbatim
    /// (clause 1, unchanged since task E05/F03/US02/T02); absent one,
    /// <see cref="NegotiationOutcomePropagationService.ResolveSavingsOpportunityIdAsync"/> resolves
    /// a link from the product's own supplier-identity rule instead, or declines (clause 2). Either
    /// way its own success/failure is reported as <c>savingsPropagated</c>/
    /// <c>savingsPropagationError</c> on the <em>same</em> 201 response, never as a distinct HTTP
    /// error: the outcome capture already succeeded and is already audit-tracked (AC-3), so a bad or
    /// unknown <c>savingsOpportunityId</c>, or an ambiguous/absent resolution, must not turn an
    /// already-durable write into a client-visible failure (see
    /// <see cref="NegotiationOutcomePropagationService"/>'s own "never fails an already-durable
    /// capture" doc comment). <b>Corrected rule (ADR-028 w16 round-2 footer)</b>:
    /// <c>savingsPropagated</c> is <see langword="null"/> exactly when no opportunity was linked —
    /// because none was named <em>and</em> none resolved unambiguously; it is
    /// <see langword="true"/>/<see langword="false"/> whenever a link was attempted, by either
    /// route.
    /// </para>
    /// </summary>
    private static async Task<IResult> CaptureOutcomeAsync(
        NegotiationOutcomeCaptureRequest request,
        HttpRequest httpRequest,
        NegotiationOutcomeService outcomeService,
        NegotiationOutcomePropagationService propagationService,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        // NW-05 (ADR-010 w15 footer; ADR-022 w15 footer clause 2): identity first, then the tenant
        // header as an authorized selector, then membership -- 401 / 400 / 404 in that order, all
        // owned by ICallerContext (acceptance A15-8). The scope it hands back is the tenant scope
        // this handler runs in; disposing it here is the same lifetime the old BeginScope had.
        var caller = await callerContext.ResolveTenantAsync(httpRequest, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantGuid = caller.TenantId.Value;

        var tenantId = new TenantId(tenantGuid);

        var result = await outcomeService.CaptureAsync(tenantId, request, caller.Identity!, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return string.Equals(result.Error, NegotiationOutcomeService.QuoteNotFoundError, StringComparison.Ordinal)
                ? Results.NotFound(result.Error)
                : Results.BadRequest(result.Error);
        }

        var outcome = result.Value;

        bool? savingsPropagated = null;
        string? savingsPropagationError = null;

        // ADR-028 §D5 precedence (task E19/F04/US01/T01): clause 1 (explicit id, verbatim) takes
        // priority, unchanged since task E05/F03/US02/T02. Absent one, clause 2 asks the resolver
        // for an unambiguous supplier-name match before ever calling PropagateAsync -- a decline
        // leaves this null, so PropagateAsync is never invoked and savingsPropagated stays honestly
        // null (w16 round-2 footer, Fence 2 -- see this method's own doc comment).
        //
        // Capture is already durable here. A throw from resolve/propagate (wrong connection
        // string, missing schema, a suppliers lookup that cannot run) must not turn that write
        // into a client-visible 500 -- the client would retry and mint a second outcome
        // (ADR-028: never fail an already-durable capture). Cancellation still flows.
        try
        {
            var savingsOpportunityIdToPropagate = outcome.SavingsOpportunityId
                ?? await propagationService.ResolveSavingsOpportunityIdAsync(
                    tenantId, outcome.QuoteId, cancellationToken).ConfigureAwait(false);

            if (savingsOpportunityIdToPropagate is { } savingsOpportunityId)
            {
                var propagationResult = await propagationService.PropagateAsync(
                    tenantId, outcome.Id, savingsOpportunityId, cancellationToken).ConfigureAwait(false);

                savingsPropagated = propagationResult.IsSuccess;
                savingsPropagationError = propagationResult.IsFailure ? propagationResult.Error : null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            savingsPropagated = false;
            savingsPropagationError = ex.Message;
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
