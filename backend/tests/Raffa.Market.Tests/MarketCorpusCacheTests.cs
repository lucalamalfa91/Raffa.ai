using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;
using Raffa.Market.Ingestion;
using Raffa.Market.Retrieval;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Market.Tests;

/// <summary>F7-T05: the structured corpus is loaded once per corpus version, and a new version reloads it.</summary>
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
        var cache = new MarketCorpusCache();

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
        var cache = new MarketCorpusCache();
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
}
