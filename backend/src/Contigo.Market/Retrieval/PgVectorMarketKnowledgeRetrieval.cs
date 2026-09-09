using System.Text.Json;
using Contigo.AiGateway;
using Contigo.AiGateway.Contracts;
using Contigo.Market.Contracts;
using Contigo.Market.Infrastructure;
using Contigo.Market.Ingestion;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Contigo.Market.Retrieval;

/// <summary>
/// T02's replacement for <see cref="InMemoryMarketKnowledgeRetrieval"/> (task objective:
/// "PgVectorMarketKnowledgeRetrieval : IMarketKnowledgeRetrieval (cosine distance, top-k, optional
/// category / geography filters) replacing the in-memory implementation when
/// ConnectionStrings:Market is present") — reads the shared, read-only `market_embedding` index
/// instead of calling <see cref="IMarketIntelligenceProvider"/> at question time (ADR-024: "the
/// provider is called only by the ingestion job"). Callers depend on
/// <see cref="IMarketKnowledgeRetrieval"/> only; the swap is a DI registration change
/// (<c>ServiceCollectionExtensions.AddMarketModule</c>), never a call-site change.
///
/// Takes <see cref="MarketDbContext"/> directly and is registered <b>Scoped</b> (unlike
/// <see cref="InMemoryMarketKnowledgeRetrieval"/>'s own Singleton registration): this type also
/// depends on <see cref="IAiGateway"/> to embed the search query, and
/// <c>Contigo.AiGateway.ServiceCollectionExtensions.AddAiGatewayModule</c> registers
/// <see cref="IAiGateway"/> Scoped (it wraps the Scoped <c>IAuditWriter</c> — see that method's own
/// doc comment). A Singleton capturing either the Scoped <see cref="MarketDbContext"/> or the
/// Scoped <see cref="IAiGateway"/> would be exactly the captive-dependency bug that doc comment
/// describes — unlike <c>Benchmark.MarketFeedBenchmarkAdapter</c>'s own DB-backed constructor
/// (forced Singleton by <c>Contigo.Benchmark.BenchmarkAdapterRegistry</c>'s own eager
/// <c>IEnumerable&lt;IBenchmarkProviderAdapter&gt;</c> constructor injection, and never needing
/// <see cref="IAiGateway"/> at all), nothing forces this interface's own registration to stay
/// Singleton once a real dependency requires otherwise.
/// </summary>
public sealed class PgVectorMarketKnowledgeRetrieval(
    MarketDbContext dbContext,
    IAiGateway aiGateway,
    ITenantContext tenantContext) : IMarketKnowledgeRetrieval
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<MarketNote>>> SearchAsync(
        string query,
        int topK,
        MarketKnowledgeSearchFilters? filters = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Result<IReadOnlyList<MarketNote>>.Failure("Query text is required.");
        }

        if (topK <= 0)
        {
            return Result<IReadOnlyList<MarketNote>>.Failure("topK must be a positive number.");
        }

        // See MarketIngestionService.SystemTenantId's own doc comment: this call's own embed of
        // the search query is not attributable to any real tenant (a market search reads the
        // shared index, never a tenant's own data), but LoggingAiGateway still requires an active
        // scope to log it. Scoped to only this method's own embed call -- nested/restored on
        // dispose, so an outer, real tenant scope the caller already opened (for a simultaneous
        // tenant-RAG lookup in the same Ask turn) is left exactly as it was once this returns.
        using var tenantScope = tenantContext.BeginScope(MarketIngestionService.SystemTenantId);

        var embedResult = await aiGateway
            .EmbedAsync(new AiEmbeddingRequest(query), cancellationToken)
            .ConfigureAwait(false);

        if (embedResult.IsFailure)
        {
            return Result<IReadOnlyList<MarketNote>>.Failure(embedResult.Error);
        }

        var queryVector = new Vector(embedResult.Value.Vector.ToArray());

        // Distance is projected once here and reused for the score below, rather than calling
        // CosineDistance twice, so Postgres computes it once per row (same reasoning
        // Contigo.Documents.Contracts.Application.EmbeddingRetrievalService.SearchAsync's own
        // identical comment gives). No SQL-side Take(topK): category/geography filtering happens
        // in-memory below (MarketDeal.Category/.Geography live inside MarketRecordEntity's own
        // jsonb payload, not a queryable column -- see MarketRecordEntity's own doc comment for
        // why only Provider is denormalized), so every candidate, nearest-first, must be
        // considered before this method knows which topK actually pass the filter. The mock
        // feed's own ~65-row scale (R-MKT-02) makes materializing every candidate cheap; this is
        // not expected to scale to a materially larger shared index unchanged.
        var candidates = await dbContext.MarketEmbeddings
            .AsNoTracking()
            .Join(
                dbContext.MarketRecords.AsNoTracking(),
                embedding => embedding.RecordId,
                record => record.RecordId,
                (embedding, record) => new { record.PayloadJson, Distance = embedding.Vector.CosineDistance(queryVector) })
            .OrderBy(x => x.Distance)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hits = new List<MarketNote>(Math.Min(topK, candidates.Count));
        var seenRecordIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            var deal = JsonSerializer.Deserialize<MarketDeal>(candidate.PayloadJson, JsonOptions);
            if (deal is null || !seenRecordIds.Add(deal.RecordId))
            {
                // A null deserialization would mean a corrupt payload -- never written by
                // MarketIngestionService -- so this is defensive, not an expected path. The
                // seenRecordIds guard is forward-looking: today MarketIngestionService writes
                // exactly one chunk (ChunkIndex 0) per record, so no record can appear twice in
                // candidates, but a future multi-chunk record must still surface as one MarketNote
                // per record, not one per chunk.
                continue;
            }

            if (filters?.Category is { } category
                && !string.Equals(deal.Category, category, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (filters?.Geography is { } geography
                && !string.Equals(deal.Geography, geography, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var note = MarketNoteComposer.Compose(deal);

            // pgvector cosine distance ranges [0, 2] (0 = identical direction, 1 = orthogonal,
            // 2 = opposite -- Pgvector.EntityFrameworkCore's own CosineDistance, the `<=>`
            // operator). MarketNote.Score's own doc comment promises "a vector-similarity score"
            // (higher = more relevant, matching InMemoryMarketKnowledgeRetrieval's own [0, 1]
            // overlap-ratio convention) rather than raw distance (lower = more relevant) --
            // converted here so a caller never has to know which retrieval implementation
            // produced a given hit to interpret its Score.
            var similarity = 1.0 - (candidate.Distance / 2.0);
            hits.Add(note with { Score = similarity });

            if (hits.Count == topK)
            {
                break;
            }
        }

        return Result<IReadOnlyList<MarketNote>>.Success(hits);
    }
}
