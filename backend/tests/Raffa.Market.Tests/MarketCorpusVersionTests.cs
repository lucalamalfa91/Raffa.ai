using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;
using Raffa.Market.Infrastructure.Entities;
using Raffa.Market.Ingestion;
using Raffa.Market.Retrieval;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Market.Tests;

/// <summary>F7-T08: blue/green corpus versions with an atomic switch (real Postgres + pgvector).</summary>
public sealed class MarketCorpusVersionTests : IAsyncLifetime
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

    private static List<MarketDeal> FeedA() =>
    [
        SampleDeal.Create(recordId: "A1", supplier: "Salesforce"),
        SampleDeal.Create(recordId: "A2", supplier: "Allianz"),
        SampleDeal.Create(recordId: "A3", supplier: "Microsoft"),
    ];

    private async Task<IngestionSummary> IngestAsync(
        StubFeed feed, BatchCountingGateway gateway, MarketIngestionOptions? options = null)
    {
        await using var db = CreateDbContext();
        var result = await new MarketIngestionService(
                db, feed, gateway, new TenantContext(), new FixedClock(DateTimeOffset.UtcNow), options)
            .IngestAsync();
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        return result.Value;
    }

    private async Task<List<string>> VisibleRecordIdsAsync()
    {
        await using var db = CreateDbContext();
        return await db.MarketRecords.Select(r => r.RecordId).OrderBy(x => x).ToListAsync();
    }

    private static readonly MarketIngestionOptions BlueGreen = new() { BlueGreen = true };

    [Fact]
    public async Task The_migration_seeds_the_initial_version_as_the_single_active_one()
    {
        await using var db = CreateDbContext();

        var version = await db.CorpusVersions.SingleAsync();

        Assert.Equal(MarketCorpusVersions.Initial, version.Version);
        Assert.Equal(MarketCorpusStates.Active, version.State);
    }

    [Fact]
    public async Task In_place_ingestion_writes_into_the_active_version_as_before()
    {
        var summary = await IngestAsync(new StubFeed("v1", FeedA()), new BatchCountingGateway());

        Assert.Equal(MarketCorpusVersions.Initial, summary.CorpusVersion);
        await using var db = CreateDbContext();
        Assert.Single(await db.CorpusVersions.ToListAsync());
        Assert.Equal(["A1", "A2", "A3"], await VisibleRecordIdsAsync());
    }

    [Fact]
    public async Task Blue_green_builds_next_to_the_live_corpus_and_switches_atomically()
    {
        var feed = new StubFeed("v1", FeedA());
        await IngestAsync(feed, new BatchCountingGateway());

        // v2: A1 changed, A2 gone, A3 untouched, A4 new.
        var a = FeedA();
        feed.FeedVersion = "v2";
        feed.Deals = [a[0] with { UnitPriceP50 = 500m }, a[2], SampleDeal.Create(recordId: "A4", supplier: "SAP")];
        var gateway = new BatchCountingGateway();

        var summary = await IngestAsync(feed, gateway, BlueGreen);

        Assert.NotEqual(MarketCorpusVersions.Initial, summary.CorpusVersion);
        Assert.Equal(1, summary.Removed);
        Assert.Equal(["A1", "A3", "A4"], await VisibleRecordIdsAsync());

        await using var db = CreateDbContext();
        var versions = await db.CorpusVersions.ToListAsync();
        Assert.Equal(summary.CorpusVersion, versions.Single(v => v.State == MarketCorpusStates.Active).Version);
        Assert.Equal(MarketCorpusVersions.Initial, versions.Single(v => v.State == MarketCorpusStates.Retired).Version);

        // The retired version is intact, and the new one reused the unchanged embedding of A3.
        Assert.Equal(3, await db.MarketRecords.IgnoreQueryFilters().CountAsync(r => r.CorpusVersion == MarketCorpusVersions.Initial));
        Assert.Equal(3, await db.MarketEmbeddings.CountAsync());
        Assert.Equal(2, gateway.SingleCalls);
    }

    [Fact]
    public async Task A_blue_green_run_that_changes_nothing_leaves_no_extra_version()
    {
        var feed = new StubFeed("v1", FeedA());
        await IngestAsync(feed, new BatchCountingGateway());
        var gateway = new BatchCountingGateway();

        var summary = await IngestAsync(feed, gateway, BlueGreen);

        Assert.Equal(0, gateway.SingleCalls);
        Assert.Equal(3, summary.Unchanged);
        Assert.Equal(MarketCorpusVersions.Initial, summary.CorpusVersion);
        await using var db = CreateDbContext();
        Assert.Single(await db.CorpusVersions.ToListAsync());
        Assert.Equal(3, await db.MarketRecords.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task A_crash_while_building_leaves_the_live_corpus_untouched_and_the_next_run_resumes()
    {
        var feed = new StubFeed("v1", FeedA());
        await IngestAsync(feed, new BatchCountingGateway());

        var changed = Enumerable.Range(1, 4)
            .Select(i => SampleDeal.Create(recordId: $"B{i}", supplier: $"Supplier {i}")).ToList();
        feed.FeedVersion = "v2";
        feed.Deals = changed;
        var options = new MarketIngestionOptions { BlueGreen = true, EmbedBatchSize = 2, CheckpointPerBatch = true };

        await using (var db = CreateDbContext())
        {
            var crashed = await new MarketIngestionService(
                    db, feed, new BatchCountingGateway { FailOnBatchNumber = 2 }, new TenantContext(),
                    new FixedClock(DateTimeOffset.UtcNow), options)
                .IngestAsync();
            Assert.True(crashed.IsFailure);
        }

        // Readers still see the old corpus; a building version exists with the first batch.
        Assert.Equal(["A1", "A2", "A3"], await VisibleRecordIdsAsync());
        string building;
        await using (var db = CreateDbContext())
        {
            building = (await db.CorpusVersions.SingleAsync(v => v.State == MarketCorpusStates.Building)).Version;
        }

        var resumed = await IngestAsync(feed, new BatchCountingGateway(), options);

        Assert.Equal(building, resumed.CorpusVersion);
        Assert.Equal(["B1", "B2", "B3", "B4"], await VisibleRecordIdsAsync());
    }

    [Fact]
    public async Task Rollback_is_the_same_atomic_switch_and_purge_removes_the_retired_version()
    {
        var feed = new StubFeed("v1", FeedA());
        await IngestAsync(feed, new BatchCountingGateway());
        feed.FeedVersion = "v2";
        feed.Deals = [SampleDeal.Create(recordId: "N1", supplier: "SAP")];
        await IngestAsync(feed, new BatchCountingGateway(), BlueGreen);
        Assert.Equal(["N1"], await VisibleRecordIdsAsync());

        await using (var db = CreateDbContext())
        {
            await new MarketCorpusVersionService(db, new FixedClock(DateTimeOffset.UtcNow))
                .SwitchAsync(MarketCorpusVersions.Initial);
        }

        Assert.Equal(["A1", "A2", "A3"], await VisibleRecordIdsAsync());

        await using (var db = CreateDbContext())
        {
            var purged = await new MarketCorpusVersionService(db, new FixedClock(DateTimeOffset.UtcNow)).PurgeRetiredAsync(0);

            Assert.Equal(1, purged);
            Assert.Equal(0, await db.MarketRecords.IgnoreQueryFilters().CountAsync(r => r.RecordId == "N1"));
            Assert.Equal(0, await db.MarketEmbeddings.IgnoreQueryFilters().CountAsync(e => e.RecordId == "N1"));
        }
    }

    [Fact]
    public async Task The_active_version_cannot_be_deleted_an_empty_one_cannot_be_activated_and_two_active_rows_are_refused()
    {
        await IngestAsync(new StubFeed("v1", FeedA()), new BatchCountingGateway());
        await using var db = CreateDbContext();
        var service = new MarketCorpusVersionService(db, new FixedClock(DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(MarketCorpusVersions.Initial));

        db.CorpusVersions.Add(new MarketCorpusVersionEntity
        {
            Version = "empty", State = MarketCorpusStates.Building, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SwitchAsync("empty"));

        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO market_corpus_version (version, state, created_at) VALUES ('second', 'active', now())"));
    }

    [Fact]
    public async Task Search_and_lookups_see_only_the_active_version()
    {
        var feed = new StubFeed("v1", FeedA());
        await IngestAsync(feed, new BatchCountingGateway());
        feed.FeedVersion = "v2";
        feed.Deals = [SampleDeal.Create(recordId: "N1", supplier: "SAP"), FeedA()[2]];
        await IngestAsync(feed, new BatchCountingGateway(), BlueGreen);

        await using var db = CreateDbContext();
        var gateway = new BatchCountingGateway();
        var search = new PgVectorMarketKnowledgeRetrieval(db, gateway, new TenantContext());

        var hits = await search.SearchAsync("any query", 10);

        Assert.True(hits.IsSuccess, hits.IsFailure ? hits.Error : null);
        Assert.Equal(["A3", "N1"], hits.Value.Select(h => h.RecordId).Order().ToArray());
        var lookup = new MarketRecordQueryService(db);
        Assert.Empty(await lookup.GetBySupplierAsync("Salesforce"));
        Assert.Single(await lookup.GetBySupplierAsync("SAP"));
        Assert.Equal("v2:2", await lookup.GetCorpusVersionAsync());
    }
}
