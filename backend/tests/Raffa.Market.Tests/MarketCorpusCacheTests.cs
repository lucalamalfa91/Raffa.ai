using Raffa.Benchmark.Contracts;
using Raffa.Market.Benchmark;
using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;
using Raffa.Market.Ingestion;
using Raffa.Market.Retrieval;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Market.Tests;

/// <summary>
/// F7-T05: the structured corpus is loaded once per corpus version, and a new version reloads it.
/// Tests that change the table behind the cache's back and expect to see it at once use a zero
/// revalidation interval (the check on every call); the interval itself is covered without a
/// database in <see cref="MarketCorpusCacheRevalidationTests"/>.
/// </summary>
public sealed class MarketCorpusCacheTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private MarketDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MarketDbContext>();
        MarketDbContextOptions.Configure(options, _postgres.GetConnectionString());
        return new MarketDbContext(options.Options);
    }

    private static MarketCorpusCache CheckEveryCall() =>
        new(new MarketCorpusCacheOptions { RevalidateInterval = TimeSpan.Zero });

    private async Task IngestAsync(StubFeed feed)
    {
        await using var db = CreateDbContext();
        var result = await new MarketIngestionService(
                db, feed, new BatchCountingGateway(), new TenantContext(), new FixedClock(DateTimeOffset.UtcNow))
            .IngestAsync();
        Assert.True(result.IsSuccess);
    }

    private static List<MarketDeal> Deals() =>
    [
        SampleDeal.Create(recordId: "MKT-CACHE-1", supplier: "Salesforce", updatedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
        SampleDeal.Create(recordId: "MKT-CACHE-2", supplier: "Allianz", category: "Insurance", updatedAt: new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)),
    ];

    [Fact]
    public async Task Repeated_reads_load_the_corpus_once_and_return_what_the_uncached_service_returns()
    {
        var feed = new StubFeed("v1", Deals());
        await IngestAsync(feed);
        var cache = new MarketCorpusCache();

        await using var db = CreateDbContext();
        var cached = new MarketRecordQueryService(db, cache);
        var plain = new MarketRecordQueryService(db);

        var first = await cached.GetAllAsync();
        var second = await cached.GetAllAsync();
        var bySupplier = await cached.GetBySupplierAsync("salesforce inc");

        Assert.Equal(1, cache.Loads);
        Assert.Equal(first, second);
        Assert.Equal((await plain.GetAllAsync()).Select(d => d.RecordId), first.Select(d => d.RecordId));
        Assert.Equal("MKT-CACHE-2", first[0].RecordId);
        Assert.Equal(["MKT-CACHE-1"], bySupplier.Select(d => d.RecordId));
    }

    [Fact]
    public async Task A_new_corpus_version_is_picked_up_without_an_explicit_invalidation()
    {
        var deals = Deals();
        var feed = new StubFeed("v1", deals);
        await IngestAsync(feed);
        var cache = CheckEveryCall();

        await using (var db = CreateDbContext())
        {
            await new MarketRecordQueryService(db, cache).GetAllAsync();
        }

        feed.Deals = [deals[0] with { UnitPriceP50 = 777m }, deals[1]];
        await IngestAsync(feed);

        await using (var db = CreateDbContext())
        {
            var after = await new MarketRecordQueryService(db, cache).GetAllAsync();

            Assert.Equal(2, cache.Loads);
            Assert.Equal(777m, after.Single(d => d.RecordId == "MKT-CACHE-1").UnitPriceP50);
        }
    }

    [Fact]
    public async Task A_removed_record_changes_the_version_too()
    {
        await IngestAsync(new StubFeed("v1", Deals()));
        var cache = CheckEveryCall();
        await using var db = CreateDbContext();
        var service = new MarketRecordQueryService(db, cache);
        await service.GetAllAsync();

        await db.Database.ExecuteSqlRawAsync("DELETE FROM market_record WHERE record_id = 'MKT-CACHE-1'");

        Assert.Single(await service.GetAllAsync());
        Assert.Equal(2, cache.Loads);
    }

    [Fact]
    public async Task Disabled_cache_reads_the_table_on_every_call()
    {
        await IngestAsync(new StubFeed("v1", Deals()));
        var cache = new MarketCorpusCache(new MarketCorpusCacheOptions { Enabled = false });
        await using var db = CreateDbContext();
        var service = new MarketRecordQueryService(db, cache);

        await service.GetAllAsync();
        await service.GetAllAsync();

        Assert.Equal(2, cache.Loads);
    }

    // Record ids that sort differently under the database's collation than ordinally (mixed case,
    // punctuation), all with the same updated_at, so the tie-break decides the order.
    private static List<MarketDeal> TiedDeals()
    {
        var at = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        return
        [
            SampleDeal.Create(recordId: "MKT-b-1", supplier: "Salesforce", updatedAt: at),
            SampleDeal.Create(recordId: "MKT-B-1", supplier: "Salesforce, Inc.", updatedAt: at),
            SampleDeal.Create(recordId: "mkt-a-2", supplier: "salesforce.com", updatedAt: at),
            SampleDeal.Create(recordId: "MKT_c", supplier: "Salesforce", category: "Cloud", updatedAt: at),
            SampleDeal.Create(recordId: "MKT-A-10", supplier: "Amazon Web Services", category: "Cloud", updatedAt: at),
            SampleDeal.Create(recordId: "MKT-A-9", supplier: "Web Services Ltd", category: "cloud", updatedAt: at.AddDays(1)),
            SampleDeal.Create(recordId: "MKT-Z", supplier: "Allianz SE", category: "Insurance", updatedAt: at.AddDays(-1)),
        ];
    }

    [Fact]
    public async Task Cached_lookups_return_what_the_sql_lookups_return_in_the_same_order()
    {
        await IngestAsync(new StubFeed("v1", TiedDeals()));
        await using var db = CreateDbContext();
        // Rows that predate the filter columns: NULL means "decided by the exact check".
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE market_record SET supplier_norm = NULL, product_norm = NULL, category = NULL WHERE record_id IN ('MKT-B-1', 'MKT-A-9')");

        var plain = new MarketRecordQueryService(db);
        var cached = new MarketRecordQueryService(db, new MarketCorpusCache());

        foreach (var supplier in new[] { "Salesforce", "salesforce inc", "Sales", "Web Services", "Amazon Web Services", "Allianz", "Nobody", "", "  " })
        {
            Assert.Equal(
                (await plain.GetBySupplierAsync(supplier)).Select(d => d.RecordId),
                (await cached.GetBySupplierAsync(supplier)).Select(d => d.RecordId));
        }

        foreach (var category in new[] { "Cloud", " CLOUD ", "Enterprise Software", "Insurance", "Unknown", "" })
        {
            Assert.Equal(
                (await plain.GetByCategoryAsync(category)).Select(d => d.RecordId),
                (await cached.GetByCategoryAsync(category)).Select(d => d.RecordId));
        }

        Assert.Equal(await plain.GetCorpusVersionAsync(), await cached.GetCorpusVersionAsync());
    }

    [Fact]
    public async Task The_corpus_version_is_the_same_cached_or_not_and_empty_on_an_empty_table()
    {
        await using var db = CreateDbContext();
        var cached = new MarketRecordQueryService(db, new MarketCorpusCache());
        var plain = new MarketRecordQueryService(db);

        Assert.Equal("empty", await plain.GetCorpusVersionAsync());
        Assert.Equal("empty", await cached.GetCorpusVersionAsync());

        await IngestAsync(new StubFeed("v7", Deals()));
        var fresh = new MarketRecordQueryService(db, new MarketCorpusCache());

        Assert.Equal("v7:2", await plain.GetCorpusVersionAsync());
        Assert.Equal("v7:2", await fresh.GetCorpusVersionAsync());
    }

    [Fact]
    public async Task An_unreadable_row_elsewhere_in_the_table_does_not_break_the_cached_supplier_lookup()
    {
        await IngestAsync(new StubFeed("v1", Deals()));
        await using var db = CreateDbContext();
        // A JSON array is not a MarketDeal: the snapshot cannot be built, the SQL path (which only parses
        // the rows it returns) answers instead, exactly as it does without a cache.
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO market_record (record_id, feed_version, provider, payload_json, provenance_label, updated_at, " +
            "supplier_norm, product_norm, category, geography, currency) " +
            "VALUES ('BAD', 'v1', 'p', '[]'::jsonb, 'l', now(), 'zzz', 'zzz', 'zzz', 'ZZ', 'ZZZ')");

        var service = new MarketRecordQueryService(db, new MarketCorpusCache());

        Assert.Equal(["MKT-CACHE-1"], (await service.GetBySupplierAsync("Salesforce")).Select(d => d.RecordId));
        Assert.Equal(["MKT-CACHE-2"], (await service.GetByCategoryAsync("insurance")).Select(d => d.RecordId));
    }

    [Fact]
    public async Task The_benchmark_adapter_answers_the_same_from_the_snapshot_as_from_the_table()
    {
        await IngestAsync(new StubFeed("v1", TiedDeals()));
        await using (var db = CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE market_record SET supplier_norm = NULL, product_norm = NULL WHERE record_id IN ('MKT-B-1', 'MKT-A-9')");
        }

        var factory = new PooledFactory(_postgres.GetConnectionString());
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var fromTable = new MarketFeedBenchmarkAdapter(factory, clock);
        var fromSnapshot = new MarketFeedBenchmarkAdapter(factory, clock, new MarketCorpusCache());

        foreach (var (supplier, product) in new[]
        {
            ("Salesforce", "Sales Cloud Enterprise"), ("salesforce", "sales cloud enterprise"), ("Salesforce", "Other"),
            ("Amazon Web Services", "Sales Cloud Enterprise"), ("Nobody", "Nothing"),
        })
        {
            var query = new BenchmarkQuery(supplier, product, null, "CH", 10m, "12 months", "CHF", new DateOnly(2026, 6, 1));
            var expected = await fromTable.GetBenchmarkAsync(query);
            var actual = await fromSnapshot.GetBenchmarkAsync(query);

            Assert.Equal(expected.IsSuccess, actual.IsSuccess);
            Assert.Equal(
                System.Text.Json.JsonSerializer.Serialize(expected.Value),
                System.Text.Json.JsonSerializer.Serialize(actual.Value));
        }
    }

    private sealed class PooledFactory(string connectionString) : IDbContextFactory<MarketDbContext>
    {
        public MarketDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<MarketDbContext>();
            MarketDbContextOptions.Configure(options, connectionString);
            return new MarketDbContext(options.Options);
        }
    }
}
