using Raffa.Market;
using Raffa.Market.Contracts;
using Raffa.Market.Retrieval;

namespace Raffa.Api;

/// <summary>
/// Maps <c>GET /api/market/records/{id}</c> — one market-intelligence record for the Ask citation
/// panel (<c>inputs/requirements.md</c> §6 API contract table; R-EVD-02 "a market citation opens a
/// side panel with the record"; story us-01-market-intelligence AC-5 "returns one record with its
/// provenance label and updatedAt").
///
/// <para>
/// <b>Single writer, reconciled.</b> Tasks E13/F02/US01/T02 (market-index) and E13/F06/US01/T01
/// (ask-engine) both created this file in the same wave phase; the phase-barrier union merge
/// concatenated the two handlers and broke the build. This is the reconciled version: the
/// persisted <c>market_record</c> store is the source of truth when the Market module is wired with
/// a connection string (<see cref="MarketRecordQueryService"/>, ADR-024 "the provider is called
/// only by the ingestion job"); when the host runs without the Market database (local, CI on the
/// fixture) the handler falls back to the same in-memory feed
/// <see cref="Raffa.Market.Benchmark.MarketFeedBenchmarkAdapter"/> and
/// <see cref="InMemoryMarketKnowledgeRetrieval"/> already read, so the endpoint behaves the same in
/// both hosts. The response is the OpenAPI shape the web client expects
/// (<c>web/openapi/raffa-api.v1.json</c>, operation <c>getMarketRecord</c>: <c>recordId</c>,
/// <c>title</c>, <c>category</c>, <c>geography</c>, <c>band</c>, <c>provenance</c>,
/// <c>updatedAt</c>) plus the raw deal fields both original handlers exposed.
/// </para>
///
/// <para>
/// <b>No tenant header</b>: unlike every tenant-scoped endpoint in this host, the market index is
/// shared and read-only for every tenant (ADR-024 "three sources, one rule"), so there is no
/// authorization boundary to enforce here — the same rule
/// <see cref="CapabilitiesEndpointExtensions"/> documents for its static catalog.
/// </para>
/// </summary>
public static class MarketEndpointExtensions
{
    public static IEndpointRouteBuilder MapMarketEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/market/records/{id}", GetMarketRecordAsync);
        return endpoints;
    }

    private static async Task<IResult> GetMarketRecordAsync(
        string id,
        HttpContext httpContext,
        IMarketIntelligenceProvider provider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Results.BadRequest("A market record id is required.");
        }

        MarketDeal? deal;
        string? provenance = null;

        // Persisted store first (registered only when AddMarketModule received a connection
        // string — see Raffa.Market.ServiceCollectionExtensions); otherwise the in-memory feed.
        var queryService = httpContext.RequestServices.GetService<MarketRecordQueryService>();
        if (queryService is not null)
        {
            var detail = await queryService.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
            deal = detail?.Deal;
            provenance = detail?.ProvenanceLabel;
        }
        else
        {
            var feedResult = await provider.GetDealsAsync(feedVersion: null, cancellationToken).ConfigureAwait(false);
            if (feedResult.IsFailure)
            {
                return Results.BadRequest(feedResult.Error);
            }

            deal = feedResult.Value.Deals.FirstOrDefault(d => string.Equals(d.RecordId, id, StringComparison.Ordinal));
        }

        if (deal is null)
        {
            return Results.NotFound();
        }

        var note = MarketNoteComposer.Compose(deal);

        return Results.Ok(new
        {
            // OpenAPI `getMarketRecord` contract (web MarketRecordPanel).
            recordId = deal.RecordId,
            title = note.Title,
            snippet = note.Snippet,
            category = deal.Category,
            geography = deal.Geography,
            band = new
            {
                p25 = deal.UnitPriceP25,
                p50 = deal.UnitPriceP50,
                p75 = deal.UnitPriceP75,
                currency = deal.Currency,
            },
            provenance = provenance ?? note.Provenance,
            updatedAt = deal.UpdatedAt,

            // Raw deal fields (superset kept from both original handlers).
            provider = deal.Provider,
            supplier = deal.Supplier,
            product = deal.Product,
            sku = deal.Sku,
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
        });
    }
}
