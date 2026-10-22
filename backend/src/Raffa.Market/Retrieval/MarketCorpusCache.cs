using System.Runtime.ExceptionServices;
using System.Text.Json;
using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;

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

    /// <summary>
    /// How long a cached snapshot (and the corpus version derived from it) is trusted before the SQL
    /// fingerprint is run again to check the table has not changed. The fingerprint is a scan of the
    /// whole corpus, so running it on every read costs about as much as the reads it protects; the
    /// corpus is written only by the ingestion job (another process, normally once a day), so a change
    /// is picked up at most this long after the job commits. Within the interval nothing touches the
    /// database. <see cref="TimeSpan.Zero"/> checks on every call, as the cache did before this
    /// option existed. Ignored when <see cref="Enabled"/> is off.
    /// </summary>
    public TimeSpan RevalidateInterval { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>One immutable view of the whole corpus, newest record first, tagged with its version.</summary>
/// <param name="Version">The corpus fingerprint it was built from (<see cref="MarketCorpusCache.FingerprintAsync"/>).</param>
/// <param name="Deals">Every record, deserialized, ordered by <c>UpdatedAt</c> descending.</param>
/// <param name="TableOrder">The same records in the order the table returned them (what a plain
/// <c>SELECT</c> gave before the cache; the benchmark adapter's tie-breaking relies on it).</param>
/// <param name="Index">Per-supplier / per-category / benchmark-candidate lookups over the same records.</param>
public sealed record MarketCorpusSnapshot(
    string Version,
    IReadOnlyList<MarketDeal> Deals,
    IReadOnlyList<MarketDeal> TableOrder,
    MarketCorpusIndex Index);

/// <summary>
/// F7-T05: the structured corpus (every <see cref="MarketDeal"/>, ~20 MB of jsonb for 25.7k rows)
/// cached per corpus version, so a read path no longer re-reads and re-parses the whole table on
/// every call. The version is a cheap fingerprint computed in SQL (row count, newest feed version and
/// a hash over the per-record <c>payload_hash</c>es): any ingest that adds, removes or changes a
/// record changes it, and the next read reloads. Registered as a singleton and shared by every read
/// path of this module. Never tenant data -- the market corpus is the shared system corpus.
///
/// <para>
/// The fingerprint is itself a scan of the whole corpus, so it is re-run at most once per
/// <see cref="MarketCorpusCacheOptions.RevalidateInterval"/> (single-flighted: one caller checks, the
/// others wait for it and reuse the verdict), not on every read. No strictly cheaper exact token
/// exists: in-place ingestion rewrites <c>market_record</c> rows without touching
/// <c>market_corpus_version</c>, and the ingestion job runs in another process. The consequence is a
/// bounded delay -- an ingest committed by the Worker reaches this process within the interval -- on
/// data that is operator-written, not user-written. An interval of zero restores the check on every
/// call.
/// </para>
/// </summary>
public sealed class MarketCorpusCache
{
    // Same SemaphoreSlim discipline as the pgvector retrieval: one loader at a time; the others wait
    // for it and then find the snapshot already current.
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Separate from _gate so reading the (cheap) corpus version never waits behind a snapshot load.
    private readonly SemaphoreSlim _versionGate = new(1, 1);
    private readonly bool _enabled;
    private readonly TimeSpan _revalidateAfter;
    private readonly TimeProvider _time;
    private readonly IMarketCorpusSource _source;
    private volatile Validated? _current;
    private volatile VersionToken? _token;
    private volatile LoadFailure? _failure;

    /// <summary>A snapshot and the moment it was last confirmed to match the table.</summary>
    private sealed record Validated(MarketCorpusSnapshot Snapshot, DateTimeOffset At);

    /// <summary>
    /// The corpus version string (<see cref="IMarketDealLookup.GetCorpusVersionAsync"/>) and when it was read.
    /// Whatever confirms a snapshot also refreshes this, so it is never older than the snapshot's last
    /// confirmation: a version read only goes to the database once the snapshot is past its interval
    /// too, and the pricing that follows revalidates the snapshot -- a stored comparison is never
    /// labelled with a newer corpus version than the data it was computed from.
    /// </summary>
    private sealed record VersionToken(string Value, DateTimeOffset At);

    /// <summary>A payload that did not deserialize, remembered for one interval so a corrupt row costs
    /// one failed whole-table read per interval, not one per call.</summary>
    private sealed record LoadFailure(JsonException Error, DateTimeOffset At);

    public MarketCorpusCache(MarketCorpusCacheOptions? options = null, TimeProvider? timeProvider = null)
        : this(options, timeProvider, SqlMarketCorpusSource.Instance)
    {
    }

    internal MarketCorpusCache(MarketCorpusCacheOptions? options, TimeProvider? timeProvider, IMarketCorpusSource source)
    {
        var effective = options ?? new MarketCorpusCacheOptions();
        _enabled = effective.Enabled;
        _revalidateAfter = effective.RevalidateInterval;
        _time = timeProvider ?? TimeProvider.System;
        _source = source;
    }

    /// <summary>Whether snapshots are kept; when off every read goes to the table, as before the cache.</summary>
    public bool Enabled => _enabled;

    /// <summary>Number of times the corpus was actually loaded from the database (for tests/metrics).</summary>
    public int Loads { get; private set; }

    /// <summary>Drops the cached snapshot; the next read reloads.</summary>
    public void Invalidate()
    {
        _current = null;
        _token = null;
        _failure = null;
    }

    /// <summary>
    /// SQL fingerprint of the corpus: changes whenever a record is added, removed or its payload
    /// changes. Rows that predate <c>payload_hash</c> contribute the hash of their jsonb text.
    /// </summary>
    public static Task<string> FingerprintAsync(MarketDbContext db, CancellationToken cancellationToken) =>
        SqlMarketCorpusSource.Instance.FingerprintAsync(db, cancellationToken);

    /// <summary>
    /// The corpus version string readers compare stored work against -- <c>"{max feed_version}:{row count}"</c>,
    /// or <c>"empty"</c> -- derived from a <see cref="FingerprintAsync"/> value (<c>count:feed:hash</c>;
    /// the feed version may itself contain colons, so it is what lies between the first and the last one).
    /// </summary>
    public static string CorpusVersionOf(string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);

        var first = fingerprint.IndexOf(':', StringComparison.Ordinal);
        var last = fingerprint.LastIndexOf(':');
        if (first < 0 || last <= first)
        {
            throw new FormatException($"'{fingerprint}' is not a corpus fingerprint (count:feed:hash).");
        }

        var count = fingerprint[..first];
        return string.Equals(count, "0", StringComparison.Ordinal)
            ? "empty"
            : $"{fingerprint[(first + 1)..last]}:{count}";
    }

    /// <summary>
    /// The corpus version string, exactly what a <c>COUNT(*)</c> + <c>MAX(feed_version)</c> over the active
    /// corpus returns, but read once per <see cref="MarketCorpusCacheOptions.RevalidateInterval"/> (shared
    /// with the snapshot check) instead of on every call, and without loading the corpus.
    /// </summary>
    public async Task<string> GetCorpusVersionAsync(MarketDbContext db, CancellationToken cancellationToken = default)
    {
        if (_enabled && TryFreshToken(out var fresh))
        {
            return fresh;
        }

        if (!_enabled || _revalidateAfter <= TimeSpan.Zero)
        {
            return await _source.CorpusVersionAsync(db, cancellationToken).ConfigureAwait(false);
        }

        await _versionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryFreshToken(out fresh))
            {
                return fresh;
            }

            var value = await _source.CorpusVersionAsync(db, cancellationToken).ConfigureAwait(false);
            _token = new VersionToken(value, _time.GetUtcNow());
            return value;
        }
        finally
        {
            _versionGate.Release();
        }
    }

    /// <summary>The current corpus: the cached snapshot while its version still matches the table, a fresh load otherwise.</summary>
    public async Task<MarketCorpusSnapshot> GetAsync(MarketDbContext db, CancellationToken cancellationToken = default)
    {
        if (_enabled && TryFreshSnapshot(out var snapshot))
        {
            return snapshot;
        }

        if (_enabled && _revalidateAfter > TimeSpan.Zero)
        {
            return await RevalidateAsync(db, cancellationToken).ConfigureAwait(false);
        }

        // Check on every call (interval zero) or no cache at all: the original flow.
        var version = await _source.FingerprintAsync(db, cancellationToken).ConfigureAwait(false);
        if (_enabled && _current is { } hit && string.Equals(hit.Snapshot.Version, version, StringComparison.Ordinal))
        {
            return hit.Snapshot;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_enabled && _current is { } again && string.Equals(again.Snapshot.Version, version, StringComparison.Ordinal))
            {
                return again.Snapshot;
            }

            return await LoadAsync(db, version, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // The snapshot is past its interval: one caller re-runs the fingerprint (and reloads when it moved);
    // the callers that queued behind it find the verdict fresh and return without a query.
    private async Task<MarketCorpusSnapshot> RevalidateAsync(MarketDbContext db, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryFreshSnapshot(out var snapshot))
            {
                return snapshot;
            }

            if (_failure is { } failure && IsFresh(failure.At))
            {
                ExceptionDispatchInfo.Capture(failure.Error).Throw();
            }

            var version = await _source.FingerprintAsync(db, cancellationToken).ConfigureAwait(false);
            if (_current is { } hit && string.Equals(hit.Snapshot.Version, version, StringComparison.Ordinal))
            {
                var now = _time.GetUtcNow();
                _current = hit with { At = now };
                _token = new VersionToken(CorpusVersionOf(version), now);
                return hit.Snapshot;
            }

            return await LoadAsync(db, version, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Reads the table and builds the snapshot. Called with _gate held.
    private async Task<MarketCorpusSnapshot> LoadAsync(MarketDbContext db, string version, CancellationToken cancellationToken)
    {
        MarketCorpusRead read;
        try
        {
            read = await _source.ReadAsync(db, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex) when (_enabled && _revalidateAfter > TimeSpan.Zero)
        {
            _failure = new LoadFailure(ex, _time.GetUtcNow());
            throw;
        }

        _failure = null;
        var deals = read.Records.Select(r => r.Deal).ToList();
        var snapshot = new MarketCorpusSnapshot(
            version,
            deals.OrderByDescending(d => d.UpdatedAt).ToList(),
            deals,
            MarketCorpusIndex.Build(read.Records, read.QueryOrder, read.LatestUpdatedAt));
        Loads++;
        if (_enabled)
        {
            var now = _time.GetUtcNow();
            _current = new Validated(snapshot, now);
            _token = new VersionToken(CorpusVersionOf(version), now);
        }

        return snapshot;
    }

    private bool TryFreshSnapshot(out MarketCorpusSnapshot snapshot)
    {
        if (_revalidateAfter > TimeSpan.Zero && _current is { } current && IsFresh(current.At))
        {
            snapshot = current.Snapshot;
            return true;
        }

        snapshot = null!;
        return false;
    }

    private bool TryFreshToken(out string value)
    {
        if (_revalidateAfter > TimeSpan.Zero && _token is { } token && IsFresh(token.At))
        {
            value = token.Value;
            return true;
        }

        value = null!;
        return false;
    }

    // A clock stepped backwards makes the age negative: treat that as expired, never as fresh.
    private bool IsFresh(DateTimeOffset validatedAt)
    {
        var age = _time.GetUtcNow() - validatedAt;
        return age >= TimeSpan.Zero && age < _revalidateAfter;
    }
}
