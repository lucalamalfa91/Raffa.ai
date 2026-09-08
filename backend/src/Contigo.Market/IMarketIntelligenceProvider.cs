using Contigo.Market.Contracts;
using Contigo.SharedKernel;

namespace Contigo.Market;

/// <summary>
/// The adapter seam R-MKT-01 names: "No business module (Chat, Renewals, Savings, Quotes)
/// references the provider schema (spec §10.2)." <see cref="Mock.MockMarketIntelligenceProvider"/>
/// is this task's only implementation, reading the checked-in
/// <c>backend/fixtures/market-intelligence.mock.json</c>; R-MKT-05's later, council-justified
/// live third-party client is a second implementation behind this exact interface — "no Ask,
/// Chat or UI change" (R-MKT-05, verbatim) when that swap happens, mirroring how
/// <c>Contigo.Benchmark.Adapters.IBenchmarkProviderAdapter</c> and
/// <c>Contigo.AiGateway.IAiGateway</c> both let a provider swap stay a registration change, never
/// a call-site change.
///
/// Called <em>only</em> by the ingestion job (R-MKT-03: "the provider is called only by that
/// job; at question time Ask reads Contigo's own store and nothing else") — once T02 adds the
/// `seed-market-intelligence` ingestion job and the persisted `market_record` /
/// `market_embedding` tables, <see cref="Benchmark.MarketFeedBenchmarkAdapter"/> and
/// <see cref="Retrieval.IMarketKnowledgeRetrieval"/> are expected to read from that persisted
/// store instead of calling this interface directly at question time. This task (T01) has no
/// persisted store yet, so both of those types call this interface directly as their only
/// available data source — an interim, explicitly-scoped shortcut, not the steady-state shape
/// R-MKT-03 describes; see each of their own doc comments.
/// </summary>
public interface IMarketIntelligenceProvider
{
    /// <summary>
    /// Returns every <see cref="MarketDeal"/> in the feed, plus the feed's own version
    /// (R-MKT-03: "idempotent and versioned by feed version" — the version an ingestion job
    /// compares against what it already persisted to decide whether re-running changes
    /// anything, AC-1).
    /// </summary>
    /// <param name="feedVersion">
    /// When <see langword="null"/> (the common case), returns the provider's current feed.
    /// When supplied, requests that specific version — <see cref="Mock.MockMarketIntelligenceProvider"/>
    /// only ever has the one checked-in version, so a non-matching, non-null value fails rather
    /// than silently substituting the current feed (this provider has no historical versions to
    /// serve). A future live provider (R-MKT-05) may support real historical lookups the same way.
    /// </param>
    Task<Result<MarketFeedSnapshot>> GetDealsAsync(
        string? feedVersion = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// One feed read: every <see cref="MarketDeal"/> the provider currently has, plus the
/// <see cref="FeedVersion"/> they were served under (R-MKT-03 AC-1's idempotency key).
/// </summary>
public sealed record MarketFeedSnapshot(IReadOnlyList<MarketDeal> Deals, string FeedVersion);
