using Contigo.Market;
using Contigo.Market.Contracts;
using Contigo.Market.Retrieval;

namespace Contigo.Api;

/// <summary>
/// Maps `GET /api/market/records/{id}` (`inputs/requirements.md` §6 API contract table: "one
/// market record (for the citation panel)"; R-EVD-02: "a market citation opens a side panel with
/// the record"). Task E13/F06/US01/T01 (ask-engine) is this endpoint's first writer — the market
/// -ingestion task that owns `Contigo.Market`'s own persisted `market_record` store
/// (R-MKT-03's own "T02" scope) has not landed in this wave; until it does, this handler reads the
/// same in-memory <see cref="IMarketIntelligenceProvider"/> feed
/// <see cref="Contigo.Market.Benchmark.MarketFeedBenchmarkAdapter"/>/
/// <see cref="InMemoryMarketKnowledgeRetrieval"/> already read directly (see either type's own doc
/// comment on why: "this task has no persisted store yet"), composed through
/// <see cref="MarketNoteComposer.Compose"/> the same way <c>InMemoryMarketKnowledgeRetrieval</c>
/// itself does — no new data path, just a by-id lookup over the same feed.
///
/// No tenant header: a market record is shared, tenant-agnostic data (ADR-024 "readable by every
/// tenant"), the same "no `X-Tenant-Id`" rule <see cref="CapabilitiesEndpointExtensions"/>'s own doc
/// comment already documents for the identical reason.
/// </summary>
public static class MarketEndpointExtensions
{
    public static IEndpointRouteBuilder MapMarketEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/market/records/{id}", GetMarketRecordAsync);
        return endpoints;
    }

    private static async Task<IResult> GetMarketRecordAsync(
        string id, IMarketIntelligenceProvider provider, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Results.BadRequest("A market record id is required.");
        }

        var feedResult = await provider.GetDealsAsync(feedVersion: null, cancellationToken).ConfigureAwait(false);
        if (feedResult.IsFailure)
        {
            return Results.BadRequest(feedResult.Error);
        }

        var deal = feedResult.Value.Deals.FirstOrDefault(d => string.Equals(d.RecordId, id, StringComparison.Ordinal));
        if (deal is null)
        {
            return Results.NotFound();
        }

        var note = MarketNoteComposer.Compose(deal);

        return Results.Ok(new
        {
            recordId = deal.RecordId,
            supplier = deal.Supplier,
            category = deal.Category,
            product = deal.Product,
            geography = deal.Geography,
            currency = deal.Currency,
            title = note.Title,
            snippet = note.Snippet,
            provenance = note.Provenance,
            updatedAt = deal.UpdatedAt,
            unitPriceP25 = deal.UnitPriceP25,
            unitPriceP50 = deal.UnitPriceP50,
            unitPriceP75 = deal.UnitPriceP75,
            sampleSize = deal.SampleSize,
            source = deal.Source,
            representative = deal.Representative,
        });
    }
}
