using Contigo.Market;
using Contigo.Market.Contracts;
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

        var deal = detail.Deal;
        var note = MarketNoteComposer.Compose(deal);

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
