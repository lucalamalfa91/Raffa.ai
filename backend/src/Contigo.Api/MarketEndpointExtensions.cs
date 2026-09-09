using Contigo.Market.Retrieval;

namespace Contigo.Api;

/// <summary>
/// Maps `GET /api/market/records/{id}` (task E13/F02/US01/T02, parent story
/// us-01-market-intelligence AC-5: "returns one record with its provenance label and updatedAt for
/// the citation panel"). Deliberately **not** called from `Program.cs` by this task — the task's
/// own coding objective names this file "mapped by F06/T01 in this same phase", the identical
/// "endpoint exists, host wiring is a later task's job" shape
/// <c>Contigo.Api.CapabilitiesEndpointExtensions</c>'s own doc comment already documents for `GET
/// /api/capabilities`. If `Program.cs` already calls <c>MapMarketEndpoints()</c> by the end of
/// this phase (F06/T01 landing after this task), this file is what makes that call resolve; if
/// this task's own worktree merges after F06/T01's, the method already exists for that merge to
/// find.
///
/// <b>No tenant header</b>: unlike every tenant-scoped endpoint in this host
/// (`ContractsEndpointExtensions`, `RenewalsEndpointExtensions`, ...), the market index is shared
/// and read-only for every tenant (ADR-024 "three sources, one rule"; parent story AC-3) — there is
/// no authorization boundary to enforce here, so this endpoint needs no `X-Tenant-Id` the same way
/// `CapabilitiesEndpointExtensions`' own static, tenant-agnostic catalog does not.
///
/// Business logic (the lookup, the 404 rule, deserializing the stored payload) lives in
/// <see cref="MarketRecordQueryService"/>, not here — ADR-002's "host is a thin composition root",
/// applied identically to every other endpoint file in this project.
/// </summary>
public static class MarketEndpointExtensions
{
    public static IEndpointRouteBuilder MapMarketEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/market/records/{id}", GetRecordAsync);
        return endpoints;
    }

    private static async Task<IResult> GetRecordAsync(
        string id,
        MarketRecordQueryService queryService,
        CancellationToken cancellationToken)
    {
        var detail = await queryService.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return Results.NotFound();
        }

        var deal = detail.Deal;

        return Results.Ok(new
        {
            recordId = deal.RecordId,
            provider = deal.Provider,
            supplier = deal.Supplier,
            category = deal.Category,
            product = deal.Product,
            sku = deal.Sku,
            geography = deal.Geography,
            currency = deal.Currency,
            companySizeBand = deal.CompanySizeBand,
            termMonths = deal.TermMonths,
            annualValueBand = deal.AnnualValueBand,
            unitPriceP25 = deal.UnitPriceP25,
            unitPriceP50 = deal.UnitPriceP50,
            unitPriceP75 = deal.UnitPriceP75,
            discountAchievedPct = deal.DiscountAchievedPct,
            upliftCapPct = deal.UpliftCapPct,
            noticeDays = deal.NoticeDays,
            paymentTerms = deal.PaymentTerms,
            negotiatedClauses = deal.NegotiatedClauses,
            closingPeriod = deal.ClosingPeriod,
            sampleSize = deal.SampleSize,
            source = deal.Source,
            representative = deal.Representative,
            // AC-5, verbatim: "its provenance label and updatedAt for the citation panel".
            updatedAt = deal.UpdatedAt,
            provenance = detail.ProvenanceLabel,
        });
    }
}
