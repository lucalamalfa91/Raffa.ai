using Contigo.Market.Contracts;
using Contigo.SharedKernel;

namespace Contigo.Market.Retrieval;

/// <summary>
/// Projection 2 of R-MKT-03's "two projections, one ingestion job": market notes ("one narrative
/// per record") searched independently of the benchmark numbers
/// (<see cref="Benchmark.MarketFeedBenchmarkAdapter"/>). This task (T01) registers
/// <see cref="InMemoryMarketKnowledgeRetrieval"/> (token overlap over
/// <see cref="MarketNoteComposer"/>'s composed notes) as the default implementation; T02 swaps in
/// a pgvector-backed implementation over the shared, read-only <c>market_embedding</c> index
/// (R-MKT-03: "no tenant id, read by every tenant, written only by the ingestion job, own table —
/// never rows in the tenant `embedding` table") behind this same interface — callers (the future
/// Ask context-pack assembly) never notice the swap.
///
/// Same "expected failure via <see cref="Result{T}"/>, not an exception" convention as
/// <c>Contigo.Documents.Contracts.Application.EmbeddingRetrievalService.SearchAsync</c>, the
/// tenant-RAG equivalent of this interface.
/// </summary>
public interface IMarketKnowledgeRetrieval
{
    /// <summary>
    /// Returns up to <paramref name="topK"/> <see cref="MarketNote"/> hits for
    /// <paramref name="query"/>, most relevant first. An empty result is a normal "nothing
    /// matched" outcome (<see cref="Result{T}.Success"/> with an empty list) — <see cref="Result{T}.Failure"/>
    /// is reserved for a malformed request (<paramref name="query"/> blank, <paramref name="topK"/>
    /// not positive), never for "no hits".
    /// </summary>
    /// <param name="query">Free-text search query, e.g. <c>"uplift cap Salesforce"</c>.</param>
    /// <param name="topK">Maximum number of hits to return; must be positive.</param>
    /// <param name="filters">Optional narrowing filters; <see langword="null"/> (the default)
    /// searches the whole market-notes corpus.</param>
    Task<Result<IReadOnlyList<MarketNote>>> SearchAsync(
        string query,
        int topK,
        MarketKnowledgeSearchFilters? filters = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional narrowing filters for <see cref="IMarketKnowledgeRetrieval.SearchAsync"/> — both
/// <see langword="null"/> by default (no narrowing). Mirrors the two facets <see cref="MarketNote"/>
/// itself already exposes (<see cref="MarketNote.Category"/> / <see cref="MarketNote.Geography"/>),
/// so a caller can narrow to exactly what it can already see on a hit without inventing a new
/// vocabulary.
/// </summary>
/// <param name="Category">When set, only notes whose <see cref="MarketNote.Category"/> matches
/// (case-insensitive) are considered.</param>
/// <param name="Geography">When set, only notes whose <see cref="MarketNote.Geography"/> matches
/// (case-insensitive) are considered.</param>
public sealed record MarketKnowledgeSearchFilters(string? Category = null, string? Geography = null);
