using System.Globalization;
using System.Text.Json;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;
using Raffa.Market.Infrastructure.Entities;
using Raffa.Market.Ingestion;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Raffa.Market.Retrieval;

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
/// <c>Raffa.AiGateway.ServiceCollectionExtensions.AddAiGatewayModule</c> registers
/// <see cref="IAiGateway"/> Scoped (it wraps the Scoped <c>IAuditWriter</c> — see that method's own
/// doc comment). A Singleton capturing either the Scoped <see cref="MarketDbContext"/> or the
/// Scoped <see cref="IAiGateway"/> would be exactly the captive-dependency bug that doc comment
/// describes — unlike <c>Benchmark.MarketFeedBenchmarkAdapter</c>'s own DB-backed constructor
/// (forced Singleton by <c>Raffa.Benchmark.BenchmarkAdapterRegistry</c>'s own eager
/// <c>IEnumerable&lt;IBenchmarkProviderAdapter&gt;</c> constructor injection, and never needing
/// <see cref="IAiGateway"/> at all), nothing forces this interface's own registration to stay
/// Singleton once a real dependency requires otherwise.
///
/// <para>
/// F7-T01: the vector query is <c>ORDER BY distance LIMIT window</c> answered from the HNSW index
/// (<see cref="NearestNeighbours"/>), not a materialization of the whole table;
/// <see cref="MarketRetrievalWindow"/> sizes the window and the search widens it (exactly) only when
/// the post-filters leave too few hits. F7-T06: before ranking, the query's embedding identity is
/// checked against what the index holds (<see cref="MarketEmbeddingCompatibility"/>); an index built
/// with another model, or with fixture vectors, is refused with an explicit error instead of
/// returning noise.
/// </para>
/// </summary>
public sealed class PgVectorMarketKnowledgeRetrieval(
    MarketDbContext dbContext,
    IAiGateway aiGateway,
    ITenantContext tenantContext,
    ILogger<PgVectorMarketKnowledgeRetrieval>? logger = null) : IMarketKnowledgeRetrieval
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    // One search at a time per instance: a MarketDbContext is not thread-safe, and a search is now
    // several statements inside one transaction. The instance is Scoped, so this only serializes
    // callers that share a request/scope -- never searches across requests.
    private readonly SemaphoreSlim _gate = new(1, 1);

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
        var queryIdentity = MarketEmbeddingIdentity.From(embedResult.Value.Metadata);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SearchIndexAsync(queryVector, queryIdentity, topK, filters, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Result<IReadOnlyList<MarketNote>>> SearchIndexAsync(
        Vector queryVector,
        MarketEmbeddingIdentity queryIdentity,
        int topK,
        MarketKnowledgeSearchFilters? filters,
        CancellationToken cancellationToken)
    {
        // F7-T06 guard: what the index holds, by (model, fixture-or-not). A GROUP BY over two narrow
        // columns -- cheap next to the vector query it protects. A query embedded with another model
        // (or a fixture query against a real index, and the reverse) has no meaningful distance to
        // these rows; ranking it anyway would return noise that looks like an answer, so refuse.
        var populations = await dbContext.MarketEmbeddings
            .AsNoTracking()
            .GroupBy(e => new { e.Model, e.IsFixture })
            .Select(g => new MarketEmbeddingPopulation(g.Key.Model, g.Key.IsFixture, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var state = MarketEmbeddingCompatibility.Evaluate(populations, queryIdentity);
        if (state == MarketEmbeddingIndexState.Empty)
        {
            return Result<IReadOnlyList<MarketNote>>.Success([]);
        }

        if (state == MarketEmbeddingIndexState.Incompatible)
        {
            var error = MarketEmbeddingCompatibility.DescribeIncompatibility(populations, queryIdentity);
            _logger.LogWarning("{Error}", error);
            return Result<IReadOnlyList<MarketNote>>.Failure(error);
        }

        var mixed = state == MarketEmbeddingIndexState.Mixed;
        if (mixed)
        {
            _logger.LogWarning(
                "The market index mixes embeddings of different models or kinds; ranking only the rows " +
                "comparable with the query ({Model}, fixture={IsFixture}). Re-run `ingest-market`.",
                queryIdentity.Model,
                queryIdentity.IsFixture);
        }

        var comparable = MarketEmbeddingCompatibility.ComparableCount(populations, queryIdentity);
        var filtered = filters?.Category is not null || filters?.Geography is not null;

        // F7-T01: `ORDER BY distance LIMIT window`, not "materialize every candidate". The category /
        // geography filters live inside the jsonb payload (no queryable column yet), so they are
        // applied after the vector query: over-fetch a window, filter, and widen only when too few
        // rows survived. The widening is exact (no ANN index) and ends at the whole comparable
        // index, so a filtered search still finds every match the unbounded version did.
        var window = MarketRetrievalWindow.Initial(topK, filtered, comparable);
        var useIndex = !mixed;

        while (true)
        {
            useIndex &= window <= MarketRetrievalWindow.MaxIndexWindow;

            var neighbours = await FetchNeighboursAsync(
                    queryVector, queryIdentity, window, useIndex, restrictToComparable: mixed, cancellationToken)
                .ConfigureAwait(false);

            var hits = CollectHits(neighbours, topK, filters);

            var exhausted = neighbours.Count < window || window >= comparable;
            if (hits.Count >= topK || exhausted)
            {
                return Result<IReadOnlyList<MarketNote>>.Success(hits);
            }

            // Too few survivors and more rows exist beyond the window: widen, exactly.
            window = MarketRetrievalWindow.Next(window, comparable);
            useIndex = false;
        }
    }

    private async Task<IReadOnlyList<MarketNeighbour>> FetchNeighboursAsync(
        Vector queryVector,
        MarketEmbeddingIdentity queryIdentity,
        int window,
        bool useIndex,
        bool restrictToComparable,
        CancellationToken cancellationToken)
    {
        // SET LOCAL needs a transaction; it also keeps the setting from leaking onto the pooled
        // connection. Read-only, so the commit is a formality.
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        if (useIndex)
        {
            // hnsw.ef_search (default 40) caps how many rows an HNSW scan can return, whatever the
            // LIMIT says; raise it to the window or `LIMIT 100` would quietly return 40.
            var efSearch = MarketRetrievalWindow.EfSearch(window).ToString(CultureInfo.InvariantCulture);
            await dbContext.Database
                .ExecuteSqlAsync($"SELECT set_config('hnsw.ef_search', {efSearch}, true)", cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            // Exact scan: switch the index off for this statement so the widened (or part-re-embedded)
            // pass is a true top-N, not an approximate one.
            await dbContext.Database
                .ExecuteSqlRawAsync("SET LOCAL enable_indexscan = off", cancellationToken)
                .ConfigureAwait(false);
        }

        var neighbours = await NearestNeighbours(
                dbContext, queryVector, window, restrictToComparable ? queryIdentity : null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return neighbours;
    }

    /// <summary>
    /// The nearest-neighbour query (F7-T01): the <paramref name="limit"/> embeddings closest to
    /// <paramref name="queryVector"/> by cosine distance, joined to their record payload.
    /// <para>
    /// The <c>ORDER BY vector &lt;=&gt; @q LIMIT n</c> sits in a subquery over <c>market_embedding</c>
    /// alone -- the one shape in which Postgres can answer from the HNSW index
    /// (<c>vector_cosine_ops</c>) -- and the join to <c>market_record</c> happens on those n rows only.
    /// With <paramref name="restrictTo"/> set, only rows comparable with that embedding identity are
    /// considered (a part-re-embedded index).
    /// </para>
    /// Public so the generated SQL can be asserted without a database (<c>ToQueryString()</c>).
    /// </summary>
    public static IQueryable<MarketNeighbour> NearestNeighbours(
        MarketDbContext db, Vector queryVector, int limit, MarketEmbeddingIdentity? restrictTo = null)
    {
        ArgumentNullException.ThrowIfNull(db);

        IQueryable<MarketEmbeddingEntity> source = db.MarketEmbeddings.AsNoTracking();
        if (restrictTo is { } identity)
        {
            var model = identity.Model;
            var isFixture = identity.IsFixture;
            source = source.Where(e => e.Model == model && (e.IsFixture == null || e.IsFixture == isFixture));
        }

        var nearest = source
            .OrderBy(e => e.Vector.CosineDistance(queryVector))
            .Take(limit)
            .Select(e => new { e.RecordId, Distance = e.Vector.CosineDistance(queryVector) });

        return nearest
            .Join(
                db.MarketRecords.AsNoTracking(),
                n => n.RecordId,
                record => record.RecordId,
                (n, record) => new { record.PayloadJson, n.Distance })
            .OrderBy(x => x.Distance)
            .Select(x => new MarketNeighbour(x.PayloadJson, x.Distance));
    }

    private static List<MarketNote> CollectHits(
        IReadOnlyList<MarketNeighbour> neighbours, int topK, MarketKnowledgeSearchFilters? filters)
    {
        var hits = new List<MarketNote>(Math.Min(topK, neighbours.Count));
        var seenRecordIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in neighbours)
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

        return hits;
    }
}

/// <summary>One row of the nearest-neighbour query: the record payload and its cosine distance to the query.</summary>
/// <param name="PayloadJson">The record's <c>payload_json</c>.</param>
/// <param name="Distance">pgvector cosine distance, <c>[0, 2]</c>, lower is closer.</param>
public sealed record MarketNeighbour(string PayloadJson, double Distance);

/// <summary>
/// How many rows one vector query fetches (F7-T01) -- pure arithmetic, so the policy is testable
/// without a database.
/// </summary>
public static class MarketRetrievalWindow
{
    /// <summary>pgvector's ceiling for <c>hnsw.ef_search</c>: an HNSW scan returns at most this many
    /// rows, so a larger window is answered by an exact scan instead.</summary>
    public const int MaxIndexWindow = 1000;

    /// <summary>pgvector's default <c>hnsw.ef_search</c>; never set lower than this.</summary>
    public const int DefaultEfSearch = 40;

    /// <summary>Rows fetched per wanted hit when no filter is applied after the query (a record can
    /// only repeat once a record is split into several chunks).</summary>
    public const int UnfilteredOverFetch = 2;

    /// <summary>Rows fetched per wanted hit when category / geography are filtered afterwards.</summary>
    public const int FilteredOverFetch = 10;

    /// <summary>Smallest first window of a filtered search.</summary>
    public const int MinFilteredWindow = 100;

    /// <summary>How much each widening multiplies the window by.</summary>
    public const int GrowthFactor = 8;

    /// <summary>The first window: at least <paramref name="topK"/>, at most the rows that can match.</summary>
    public static int Initial(int topK, bool filtered, int comparableRows)
    {
        var wanted = (long)topK * (filtered ? FilteredOverFetch : UnfilteredOverFetch);
        var window = filtered ? Math.Max(wanted, MinFilteredWindow) : wanted;

        window = Math.Min(window, MaxIndexWindow);
        window = Math.Max(window, topK);
        return (int)Math.Max(1, Math.Min(window, Math.Max(comparableRows, 1)));
    }

    /// <summary>The next, wider window after <paramref name="window"/> returned too few survivors.</summary>
    public static int Next(int window, int comparableRows)
    {
        var grown = Math.Max((long)window * GrowthFactor, window + 1L);
        return (int)Math.Min(Math.Max(comparableRows, 1), grown);
    }

    /// <summary><c>hnsw.ef_search</c> for a window: the window itself, never below the default nor above the ceiling.</summary>
    public static int EfSearch(int window) => Math.Clamp(window, DefaultEfSearch, MaxIndexWindow);
}
