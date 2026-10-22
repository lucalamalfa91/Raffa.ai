using System.Text.Json;
using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;
using Raffa.Market.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Market.Retrieval;

/// <summary>What <see cref="MarketCorpusCache"/> reads from the database: kept behind a seam so the
/// cache's timing and single-flight behaviour can be tested without one.</summary>
internal interface IMarketCorpusSource
{
    /// <summary>The corpus fingerprint (<see cref="MarketCorpusCache.FingerprintAsync"/>).</summary>
    Task<string> FingerprintAsync(MarketDbContext db, CancellationToken cancellationToken);

    /// <summary>The corpus version string (<c>"{max feed_version}:{row count}"</c> or <c>"empty"</c>).</summary>
    Task<string> CorpusVersionAsync(MarketDbContext db, CancellationToken cancellationToken);

    /// <summary>Every record of the active corpus, parsed.</summary>
    Task<MarketCorpusRead> ReadAsync(MarketDbContext db, CancellationToken cancellationToken);
}

/// <summary>The records of one corpus read: the rows that parsed (in table order) with their deals,
/// the ids in <c>ORDER BY updated_at DESC, record_id</c> order, and the newest <c>updated_at</c> of the table.</summary>
internal sealed record MarketCorpusRead(
    IReadOnlyList<(MarketRecordEntity Row, MarketDeal Deal)> Records,
    IReadOnlyList<string> QueryOrder,
    DateTimeOffset? LatestUpdatedAt);

/// <summary>The Postgres-backed <see cref="IMarketCorpusSource"/>.</summary>
internal sealed class SqlMarketCorpusSource : IMarketCorpusSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static SqlMarketCorpusSource Instance { get; } = new();

    public async Task<string> FingerprintAsync(MarketDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT count(*)::text || ':' || coalesce(max(feed_version), '') || ':' ||
                       coalesce(md5(string_agg(coalesce(payload_hash, md5(payload_json::text)), '' ORDER BY record_id)), '')
                       AS "Value"
                FROM market_record
                WHERE corpus_version = (SELECT version FROM market_corpus_version WHERE state = 'active')
                """)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Single();
    }

    public async Task<string> CorpusVersionAsync(MarketDbContext db, CancellationToken cancellationToken)
    {
        // The same row count and newest feed version the two EF queries (COUNT, MAX) of the uncached
        // path return, in one statement and over the same active-corpus filter as the fingerprint.
        var rows = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT CASE WHEN count(*) = 0 THEN 'empty'
                            ELSE coalesce(max(feed_version), '') || ':' || count(*)::text END
                       AS "Value"
                FROM market_record
                WHERE corpus_version = (SELECT version FROM market_corpus_version WHERE state = 'active')
                """)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Single();
    }

    public async Task<MarketCorpusRead> ReadAsync(MarketDbContext db, CancellationToken cancellationToken)
    {
        var records = await db.MarketRecords.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var parsed = new List<(MarketRecordEntity Row, MarketDeal Deal)>(records.Count);
        foreach (var record in records)
        {
            if (JsonSerializer.Deserialize<MarketDeal>(record.PayloadJson, JsonOptions) is { } deal)
            {
                parsed.Add((record, deal));
            }
        }

        // The order the database gives "newest first, then record id" -- its collation, not ordinal --
        // which the per-supplier / per-category lookups reproduce. Ids only, so it stays small.
        var queryOrder = await db.MarketRecords
            .AsNoTracking()
            .OrderByDescending(r => r.UpdatedAt)
            .ThenBy(r => r.RecordId)
            .Select(r => r.RecordId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset? latest = records.Count == 0 ? null : records.Max(r => r.UpdatedAt);
        return new MarketCorpusRead(parsed, queryOrder, latest);
    }
}
