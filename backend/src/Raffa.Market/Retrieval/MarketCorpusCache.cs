using System.Text.Json;
using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Market.Retrieval;

/// <summary>Options of <see cref="MarketCorpusCache"/> (<c>Market:CorpusCache</c>).</summary>
public sealed class MarketCorpusCacheOptions
{
    /// <summary>Configuration section these options bind from.</summary>
    public const string SectionName = "Market:CorpusCache";

    /// <summary>Whether the structured corpus is kept in memory per corpus version. On by default: the
    /// result of every read is identical, only the repeated whole-table read and JSON parse go away.
    /// Switch off to read the table on every call.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>One immutable view of the whole corpus, newest record first, tagged with its version.</summary>
/// <param name="Version">The corpus fingerprint it was built from (<see cref="MarketCorpusCache.FingerprintAsync"/>).</param>
/// <param name="Deals">Every record, deserialized, ordered by <c>UpdatedAt</c> descending.</param>
/// <param name="TableOrder">The same records in the order the table returned them (what a plain
/// <c>SELECT</c> gave before the cache; the benchmark adapter's tie-breaking relies on it).</param>
public sealed record MarketCorpusSnapshot(
    string Version, IReadOnlyList<MarketDeal> Deals, IReadOnlyList<MarketDeal> TableOrder);

/// <summary>
/// F7-T05: the structured corpus (every <see cref="MarketDeal"/>, ~20 MB of jsonb for 25.7k rows)
/// cached per corpus version, so a read path no longer re-reads and re-parses the whole table on
/// every call. The version is a cheap fingerprint computed in SQL (row count, newest feed version and
/// a hash over the per-record <c>payload_hash</c>es): any ingest that adds, removes or changes a
/// record changes it, and the next read reloads. Registered as a singleton and shared by every read
/// path of this module. Never tenant data -- the market corpus is the shared system corpus.
/// </summary>
public sealed class MarketCorpusCache(MarketCorpusCacheOptions? options = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Same SemaphoreSlim discipline as the pgvector retrieval: one loader at a time; the others wait
    // for it and then find the snapshot already current.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly bool _enabled = (options ?? new MarketCorpusCacheOptions()).Enabled;
    private volatile MarketCorpusSnapshot? _current;

    /// <summary>Number of times the corpus was actually loaded from the database (for tests/metrics).</summary>
    public int Loads { get; private set; }

    /// <summary>Drops the cached snapshot; the next read reloads.</summary>
    public void Invalidate() => _current = null;

    /// <summary>
    /// SQL fingerprint of the corpus: changes whenever a record is added, removed or its payload
    /// changes. Rows that predate <c>payload_hash</c> contribute the hash of their jsonb text.
    /// </summary>
    public static async Task<string> FingerprintAsync(MarketDbContext db, CancellationToken cancellationToken)
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

    /// <summary>The current corpus: the cached snapshot while its version still matches the table, a fresh load otherwise.</summary>
    public async Task<MarketCorpusSnapshot> GetAsync(MarketDbContext db, CancellationToken cancellationToken = default)
    {
        var version = await FingerprintAsync(db, cancellationToken).ConfigureAwait(false);
        if (_enabled && _current is { } hit && string.Equals(hit.Version, version, StringComparison.Ordinal))
        {
            return hit;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_enabled && _current is { } again && string.Equals(again.Version, version, StringComparison.Ordinal))
            {
                return again;
            }

            var records = await db.MarketRecords.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
            var deals = new List<MarketDeal>(records.Count);
            foreach (var record in records)
            {
                if (JsonSerializer.Deserialize<MarketDeal>(record.PayloadJson, JsonOptions) is { } deal)
                {
                    deals.Add(deal);
                }
            }

            var snapshot = new MarketCorpusSnapshot(
                version, deals.OrderByDescending(d => d.UpdatedAt).ToList(), deals);
            Loads++;
            if (_enabled)
            {
                _current = snapshot;
            }

            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }
}
