using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;
using Raffa.Market.Ingestion;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Market.Tests;

/// <summary>F7-T04: ingestion is idempotent through <c>payload_hash</c>; a crash loses nothing and duplicates nothing.</summary>
public sealed class MarketIngestionIdempotencyTests : IAsyncLifetime
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

    private static List<MarketDeal> Deals(int count) =>
        Enumerable.Range(1, count)
            .Select(i => SampleDeal.Create(recordId: $"MKT-IDEM-{i:0000}", supplier: $"Supplier {i}"))
            .ToList();

    private static MarketIngestionService Service(
        MarketDbContext db, StubFeed feed, BatchCountingGateway gateway, MarketIngestionOptions? options = null) =>
        new(db, feed, gateway, new TenantContext(), new FixedClock(DateTimeOffset.UtcNow), options);

    private async Task AssertNoDuplicatesAsync(int expectedRecords)
    {
        await using var db = CreateDbContext();
        Assert.Equal(expectedRecords, await db.MarketRecords.CountAsync());
        var perRecord = await db.MarketEmbeddings.GroupBy(e => e.RecordId).Select(g => g.Count()).ToListAsync();
        Assert.Equal(expectedRecords, perRecord.Count);
        Assert.All(perRecord, n => Assert.Equal(1, n));
    }

    [Fact]
    public async Task The_stored_hash_is_the_sha256_of_the_canonical_payload()
    {
        var feed = new StubFeed("v1", Deals(1));
        await using (var db = CreateDbContext())
        {
            await Service(db, feed, new BatchCountingGateway()).IngestAsync();
        }

        await using var verify = CreateDbContext();
        var record = await verify.MarketRecords.SingleAsync();
        var canonical = System.Text.Json.JsonSerializer.Serialize(
            feed.Deals[0], new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal(MarketPayloadHash.Of(canonical), record.PayloadHash);
        Assert.Equal(64, record.PayloadHash!.Length);
    }

    [Fact]
    public async Task Re_ingesting_the_same_feed_changes_nothing_and_embeds_nothing()
    {
        var feed = new StubFeed("v1", Deals(5));
        var gateway = new BatchCountingGateway();
        await using (var db = CreateDbContext())
        {
            await Service(db, feed, gateway).IngestAsync();
        }

        var singleCalls = gateway.SingleCalls;
        await using (var db = CreateDbContext())
        {
            var again = await Service(db, feed, gateway).IngestAsync();

            Assert.Equal(0, again.Value.Inserted);
            Assert.Equal(0, again.Value.Updated);
            Assert.Equal(5, again.Value.Unchanged);
            Assert.Equal(0, await db.SaveChangesAsync());
        }

        Assert.Equal(singleCalls, gateway.SingleCalls);
        await AssertNoDuplicatesAsync(5);
    }

    [Fact]
    public async Task A_changed_payload_changes_the_hash_and_only_that_record()
    {
        var deals = Deals(3);
        var feed = new StubFeed("v1", deals);
        await using (var db = CreateDbContext())
        {
            await Service(db, feed, new BatchCountingGateway()).IngestAsync();
        }

        await using var before = CreateDbContext();
        var oldHash = (await before.MarketRecords.SingleAsync(r => r.RecordId == "MKT-IDEM-0002")).PayloadHash;

        feed.Deals = [deals[0], deals[1] with { UnitPriceP50 = 999m }, deals[2]];
        await using (var db = CreateDbContext())
        {
            var result = await Service(db, feed, new BatchCountingGateway()).IngestAsync();

            Assert.Equal(1, result.Value.Updated);
            Assert.Equal(2, result.Value.Unchanged);
        }

        await using var after = CreateDbContext();
        Assert.NotEqual(oldHash, (await after.MarketRecords.SingleAsync(r => r.RecordId == "MKT-IDEM-0002")).PayloadHash);
        await AssertNoDuplicatesAsync(3);
    }

    [Fact]
    public async Task A_row_without_a_hash_is_unchanged_when_its_payload_matches_and_gets_the_hash_back_filled()
    {
        var feed = new StubFeed("v1", Deals(2));
        await using (var db = CreateDbContext())
        {
            await Service(db, feed, new BatchCountingGateway()).IngestAsync();
            await db.Database.ExecuteSqlRawAsync("UPDATE market_record SET payload_hash = NULL");
        }

        var gateway = new BatchCountingGateway();
        await using (var db = CreateDbContext())
        {
            var result = await Service(db, feed, gateway).IngestAsync();

            Assert.Equal(2, result.Value.Unchanged);
        }

        Assert.Equal(0, gateway.SingleCalls);
        await using var verify = CreateDbContext();
        Assert.All(await verify.MarketRecords.ToListAsync(), r => Assert.NotNull(r.PayloadHash));
    }

    [Fact]
    public async Task A_crash_mid_run_then_a_re_run_loses_nothing_and_duplicates_nothing()
    {
        var feed = new StubFeed("v1", Deals(6));
        var options = new MarketIngestionOptions { EmbedBatchSize = 2, CheckpointPerBatch = true };

        await using (var db = CreateDbContext())
        {
            var crashed = await Service(db, feed, new BatchCountingGateway { FailOnBatchNumber = 2 }, options).IngestAsync();
            Assert.True(crashed.IsFailure);
        }

        await using (var db = CreateDbContext())
        {
            var resumed = await Service(db, feed, new BatchCountingGateway(), options).IngestAsync();
            Assert.Equal(4, resumed.Value.Inserted);
            Assert.Equal(2, resumed.Value.Unchanged);
        }

        await AssertNoDuplicatesAsync(6);
    }
}
