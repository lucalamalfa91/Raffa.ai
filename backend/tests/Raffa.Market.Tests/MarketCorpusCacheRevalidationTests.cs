using System.Text.Json;
using Raffa.Benchmark.Contracts;
using Raffa.Market.Benchmark;
using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;
using Raffa.Market.Mock;
using Raffa.Market.Retrieval;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Market.Tests;

/// <summary>
/// When <see cref="MarketCorpusCache"/> asks the database and when it does not (the revalidation
/// interval, single-flight, the cheap corpus version), and that the read paths on top of it answer
/// from the snapshot. A fake <see cref="IMarketCorpusSource"/> stands in for Postgres and a manual
/// clock for time, so none of it needs Docker.
/// </summary>
public sealed class MarketCorpusCacheRevalidationTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private static List<(Raffa.Market.Infrastructure.Entities.MarketRecordEntity Row, MarketDeal Deal)> TwoRows() =>
        MarketCorpusTestKit.Rows(
        [
            SampleDeal.Create(recordId: "A", supplier: "Salesforce"),
            SampleDeal.Create(recordId: "B", supplier: "Allianz", category: "Insurance"),
        ]);

    private static (MarketCorpusCache Cache, FakeCorpusSource Source, ManualTimeProvider Time) Create(
        TimeSpan? interval = null, bool enabled = true)
    {
        var source = new FakeCorpusSource { Table = TwoRows() };
        var time = new ManualTimeProvider(Start);
        var options = new MarketCorpusCacheOptions { Enabled = enabled, RevalidateInterval = interval ?? Interval };
        return (new MarketCorpusCache(options, time, source), source, time);
    }

    [Fact]
    public void The_default_interval_is_thirty_seconds_and_the_cache_is_on()
    {
        var options = new MarketCorpusCacheOptions();

        Assert.True(options.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(30), options.RevalidateInterval);
    }

    [Fact]
    public async Task Within_the_interval_nothing_is_asked_of_the_database()
    {
        var (cache, source, time) = Create();
        using var db = MarketCorpusTestKit.OfflineDb();

        var first = await cache.GetAsync(db);
        time.Advance(TimeSpan.FromSeconds(29));
        var second = await cache.GetAsync(db);

        Assert.Same(first, second);
        Assert.Equal((1, 1), (source.Fingerprints, source.Reads));
        Assert.Equal(1, cache.Loads);
    }

    [Fact]
    public async Task After_the_interval_an_unchanged_corpus_costs_one_fingerprint_and_no_reload()
    {
        var (cache, source, time) = Create();
        using var db = MarketCorpusTestKit.OfflineDb();
        var first = await cache.GetAsync(db);

        time.Advance(TimeSpan.FromSeconds(31));
        var confirmed = await cache.GetAsync(db);
        // The verdict is good for another interval.
        time.Advance(TimeSpan.FromSeconds(29));
        await cache.GetAsync(db);

        Assert.Same(first, confirmed);
        Assert.Equal((2, 1), (source.Fingerprints, source.Reads));
        Assert.Equal(1, cache.Loads);
    }

    [Fact]
    public async Task After_the_interval_a_changed_corpus_is_reloaded()
    {
        var (cache, source, time) = Create();
        using var db = MarketCorpusTestKit.OfflineDb();
        var first = await cache.GetAsync(db);

        // The Worker's ingest commits: a record changes, so the fingerprint moves.
        source.Generation = 2;
        source.Table = [.. source.Table, .. MarketCorpusTestKit.Rows([SampleDeal.Create(recordId: "C", supplier: "SAP")])];
        // Not seen yet: the cached snapshot is still inside its interval.
        Assert.Same(first, await cache.GetAsync(db));

        time.Advance(TimeSpan.FromSeconds(31));
        var after = await cache.GetAsync(db);

        Assert.NotSame(first, after);
        Assert.Equal(3, after.Deals.Count);
        Assert.Equal(2, cache.Loads);
    }

    [Fact]
    public async Task Concurrent_callers_share_one_fingerprint_and_one_load()
    {
        var (cache, source, _) = Create();
        source.HoldFingerprint = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var db = MarketCorpusTestKit.OfflineDb();

        var calls = Enumerable.Range(0, 6).Select(_ => cache.GetAsync(db)).ToList();
        source.HoldFingerprint.SetResult();
        var snapshots = await Task.WhenAll(calls);

        Assert.All(snapshots, s => Assert.Same(snapshots[0], s));
        Assert.Equal((1, 1), (source.Fingerprints, source.Reads));
    }

    [Fact]
    public async Task A_waiter_honours_its_cancellation_token()
    {
        var (cache, source, _) = Create();
        source.HoldFingerprint = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var db = MarketCorpusTestKit.OfflineDb();
        var leader = cache.GetAsync(db);

        using var cts = new CancellationTokenSource();
        var waiter = cache.GetAsync(db, cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        source.HoldFingerprint.SetResult();
        await leader;
        Assert.Equal(1, source.Fingerprints);
    }

    [Fact]
    public async Task An_interval_of_zero_checks_the_fingerprint_on_every_call_as_before()
    {
        var (cache, source, _) = Create(TimeSpan.Zero);
        using var db = MarketCorpusTestKit.OfflineDb();

        await cache.GetAsync(db);
        await cache.GetAsync(db);
        await cache.GetAsync(db);
        await cache.GetCorpusVersionAsync(db);
        await cache.GetCorpusVersionAsync(db);

        Assert.Equal((3, 1, 2), (source.Fingerprints, source.Reads, source.Versions));
    }

    [Fact]
    public async Task A_disabled_cache_reads_the_table_on_every_call_and_keeps_nothing()
    {
        var (cache, source, _) = Create(enabled: false);
        using var db = MarketCorpusTestKit.OfflineDb();

        await cache.GetAsync(db);
        await cache.GetAsync(db);
        await cache.GetCorpusVersionAsync(db);
        await cache.GetCorpusVersionAsync(db);

        Assert.False(cache.Enabled);
        Assert.Equal((2, 2, 2), (source.Fingerprints, source.Reads, source.Versions));
        Assert.Equal(2, cache.Loads);
    }

    [Fact]
    public async Task A_clock_stepped_backwards_never_extends_the_interval()
    {
        var (cache, source, time) = Create();
        using var db = MarketCorpusTestKit.OfflineDb();
        await cache.GetAsync(db);

        time.Advance(TimeSpan.FromHours(-2));
        await cache.GetAsync(db);

        Assert.Equal(2, source.Fingerprints);
    }

    [Fact]
    public async Task Invalidate_forces_the_next_read_to_check_and_reload()
    {
        var (cache, source, _) = Create();
        using var db = MarketCorpusTestKit.OfflineDb();
        await cache.GetAsync(db);

        cache.Invalidate();
        await cache.GetAsync(db);

        Assert.Equal((2, 2), (source.Fingerprints, source.Reads));
    }

    [Fact]
    public async Task The_corpus_version_is_read_once_per_interval_without_loading_the_corpus()
    {
        var (cache, source, time) = Create();
        using var db = MarketCorpusTestKit.OfflineDb();

        var first = await cache.GetCorpusVersionAsync(db);
        var second = await cache.GetCorpusVersionAsync(db);
        source.Generation = 2;
        time.Advance(TimeSpan.FromSeconds(31));
        var third = await cache.GetCorpusVersionAsync(db);

        Assert.Equal(["feed-1:2", "feed-1:2", "feed-2:2"], [first, second, third]);
        Assert.Equal((2, 0, 0), (source.Versions, source.Reads, source.Fingerprints));
    }

    [Fact]
    public async Task A_confirmed_snapshot_also_answers_the_corpus_version_without_a_query()
    {
        var (cache, source, _) = Create();
        using var db = MarketCorpusTestKit.OfflineDb();
        await cache.GetAsync(db);

        var version = await cache.GetCorpusVersionAsync(db);

        // Same string the version query would return for this table.
        Assert.Equal(await source.CorpusVersionAsync(db, CancellationToken.None), version);
        Assert.Equal(1, source.Versions);
    }

    [Fact]
    public async Task A_version_read_past_the_interval_is_followed_by_a_snapshot_check_never_by_stale_data()
    {
        var (cache, source, time) = Create();
        using var db = MarketCorpusTestKit.OfflineDb();
        var first = await cache.GetAsync(db);

        // The Worker ingests a new record and the interval passes: the version read sees the new
        // corpus, so work priced right after must not be priced from the old snapshot.
        source.Generation = 2;
        source.Table = [.. source.Table, .. MarketCorpusTestKit.Rows([SampleDeal.Create(recordId: "C", supplier: "SAP")])];
        time.Advance(TimeSpan.FromSeconds(31));
        var version = await cache.GetCorpusVersionAsync(db);
        var pricedFrom = await cache.GetAsync(db);

        Assert.Equal("feed-2:3", version);
        Assert.NotSame(first, pricedFrom);
        Assert.Contains(pricedFrom.Deals, d => d.RecordId == "C");
        Assert.Equal(version, MarketCorpusCache.CorpusVersionOf(pricedFrom.Version));
    }

    [Fact]
    public async Task An_unreadable_payload_fails_once_per_interval_not_once_per_call()
    {
        var (cache, source, time) = Create();
        source.FailRead = new JsonException("not a MarketDeal");
        using var db = MarketCorpusTestKit.OfflineDb();

        await Assert.ThrowsAsync<JsonException>(() => cache.GetAsync(db));
        await Assert.ThrowsAsync<JsonException>(() => cache.GetAsync(db));
        await Assert.ThrowsAsync<JsonException>(() => cache.GetAsync(db));

        Assert.Equal((1, 1), (source.Fingerprints, source.Reads));

        // Once the interval passes the table is read again, and a repaired row is picked up.
        source.FailRead = null;
        time.Advance(TimeSpan.FromSeconds(31));
        var snapshot = await cache.GetAsync(db);

        Assert.Equal(2, snapshot.Deals.Count);
        Assert.Equal((2, 2), (source.Fingerprints, source.Reads));
    }

    [Fact]
    public async Task With_an_interval_of_zero_an_unreadable_payload_is_retried_on_every_call()
    {
        var (cache, source, _) = Create(TimeSpan.Zero);
        source.FailRead = new JsonException("not a MarketDeal");
        using var db = MarketCorpusTestKit.OfflineDb();

        await Assert.ThrowsAsync<JsonException>(() => cache.GetAsync(db));
        await Assert.ThrowsAsync<JsonException>(() => cache.GetAsync(db));

        Assert.Equal(2, source.Reads);
    }

    [Theory]
    [InlineData("25727:mock-2026.09.3:0a1b2c3d4e5f60718293a4b5c6d7e8f9", "mock-2026.09.3:25727")]
    [InlineData("1:v1:", "v1:1")]
    [InlineData("0::", "empty")]
    [InlineData("12:feed:with:colons:0a1b2c3d4e5f60718293a4b5c6d7e8f9", "feed:with:colons:12")]
    public void The_corpus_version_is_derived_from_the_fingerprint(string fingerprint, string expected) =>
        Assert.Equal(expected, MarketCorpusCache.CorpusVersionOf(fingerprint));

    [Fact]
    public void A_string_that_is_not_a_fingerprint_is_rejected() =>
        Assert.Throws<FormatException>(() => MarketCorpusCache.CorpusVersionOf("nonsense"));

    [Fact]
    public async Task The_query_service_answers_from_the_snapshot_and_loads_the_corpus_once()
    {
        var (cache, source, _) = Create();
        using var db = MarketCorpusTestKit.OfflineDb();
        var service = new MarketRecordQueryService(db, cache);

        var bySupplier = await service.GetBySupplierAsync("salesforce inc");
        var byCategory = await service.GetByCategoryAsync(" insurance ");
        var again = await service.GetBySupplierAsync("Allianz");
        var version = await service.GetCorpusVersionAsync();
        var all = await service.GetAllAsync();

        Assert.Equal(["A"], bySupplier.Select(d => d.RecordId));
        Assert.Equal(["B"], byCategory.Select(d => d.RecordId));
        Assert.Equal(["B"], again.Select(d => d.RecordId));
        Assert.Equal("feed-1:2", version);
        Assert.Equal(2, all.Count);
        Assert.Equal((1, 1, 0), (source.Fingerprints, source.Reads, source.Versions));
        Assert.Empty(await service.GetBySupplierAsync("  "));
        Assert.Empty(await service.GetByCategoryAsync(""));
    }

    [Fact]
    public async Task The_benchmark_adapter_over_the_snapshot_answers_like_the_feed_backed_adapter_on_the_whole_fixture()
    {
        var deals = (await new MockMarketIntelligenceProvider().GetDealsAsync()).Value.Deals;
        var table = MarketCorpusTestKit.Rows(deals);
        for (var i = 0; i < table.Count; i += 997)
        {
            // Rows that predate the filter columns still have to be considered.
            MarketCorpusTestKit.BlankFilterColumns(table[i].Row);
        }

        var source = new FakeCorpusSource { Table = table };
        var cache = new MarketCorpusCache(new MarketCorpusCacheOptions(), new ManualTimeProvider(Start), source);
        var clock = new FixedClock(Start);
        var viaSnapshot = new MarketFeedBenchmarkAdapter(new OfflineFactory(), clock, cache);
        var viaFeed = new MarketFeedBenchmarkAdapter(new MockMarketIntelligenceProvider(), clock);

        var queries = deals
            .Where((_, i) => i % 83 == 0)
            .SelectMany(d => new[]
            {
                new BenchmarkQuery(d.Supplier, d.Product, d.Sku, d.Geography, 10m, $"{d.TermMonths} months", d.Currency, new DateOnly(2026, 6, 1)),
                // Same supplier and product, other geography / currency / term: the weak-match branch.
                new BenchmarkQuery(d.Supplier.ToUpperInvariant(), d.Product.ToLowerInvariant(), null, "XX", 1m, "unknown", "XXX", new DateOnly(2026, 6, 1)),
                // A product nothing sells: the "latest update of the corpus" branch.
                new BenchmarkQuery(d.Supplier, "Nothing At All", null, d.Geography, 1m, "12 months", d.Currency, new DateOnly(2026, 6, 1)),
            })
            .Append(new BenchmarkQuery("Nobody", "Nothing", null, "CH", 1m, "12 months", "CHF", new DateOnly(2026, 6, 1)))
            .ToList();

        foreach (var query in queries)
        {
            var expected = Describe(await viaFeed.GetBenchmarkAsync(query));
            var actual = Describe(await viaSnapshot.GetBenchmarkAsync(query));
            Assert.Equal(expected, actual);
        }

        Assert.Equal((1, 1), (source.Fingerprints, source.Reads));
    }

    private static string Describe(Raffa.SharedKernel.Result<BenchmarkResult> result) =>
        result.IsSuccess ? JsonSerializer.Serialize(result.Value) : "failure: " + result.Error;

    private sealed class OfflineFactory : IDbContextFactory<MarketDbContext>
    {
        public MarketDbContext CreateDbContext() => MarketCorpusTestKit.OfflineDb();
    }
}
