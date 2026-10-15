using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;
using Raffa.Market.Ingestion;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Market.Tests;

/// <summary>
/// T-05 / F7-T03: the ingestion job embeds in batches, behind a TPM limiter, with an optional commit
/// per batch -- and with the defaults it behaves exactly as before (one <c>EmbedAsync</c> per record).
/// Real Postgres + pgvector.
/// </summary>
public sealed class MarketIngestionBatchTests : IAsyncLifetime
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

    private static IReadOnlyList<MarketDeal> Deals(int count) =>
        Enumerable.Range(1, count)
            .Select(i => SampleDeal.Create(recordId: $"MKT-BATCH-{i:0000}", supplier: $"Supplier {i}", product: $"Product {i}"))
            .ToList();

    private static MarketIngestionService Service(
        MarketDbContext db,
        StubFeed feed,
        BatchCountingGateway gateway,
        MarketIngestionOptions? options = null,
        EmbeddingRateLimiter? limiter = null) =>
        new(db, feed, gateway, new TenantContext(), new FixedClock(DateTimeOffset.UtcNow), options, limiter);

    [Fact]
    public async Task Defaults_embed_one_record_per_call_through_EmbedAsync()
    {
        var gateway = new BatchCountingGateway();
        await using var db = CreateDbContext();

        var result = await Service(db, new StubFeed("v1", Deals(4)), gateway).IngestAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(4, gateway.SingleCalls);
        Assert.Empty(gateway.BatchSizes);
    }

    [Fact]
    public async Task A_batch_size_groups_the_records_and_a_rerun_embeds_nothing()
    {
        var feed = new StubFeed("v1", Deals(7));
        var gateway = new BatchCountingGateway();
        var options = new MarketIngestionOptions { EmbedBatchSize = 3 };

        await using (var db = CreateDbContext())
        {
            var first = await Service(db, feed, gateway, options).IngestAsync();

            Assert.True(first.IsSuccess);
            Assert.Equal(7, first.Value.Inserted);
        }

        Assert.Equal([3, 3, 1], gateway.BatchSizes);
        Assert.Equal(0, gateway.SingleCalls);

        await using (var db = CreateDbContext())
        {
            Assert.Equal(7, await db.MarketRecords.CountAsync());
            Assert.Equal(7, await db.MarketEmbeddings.CountAsync());
            var second = await Service(db, feed, gateway, options).IngestAsync();

            Assert.Equal(7, second.Value.Unchanged);
        }

        Assert.Equal([3, 3, 1], gateway.BatchSizes);
    }

    [Fact]
    public async Task Each_record_gets_the_vector_of_its_own_note_when_batched()
    {
        var feed = new StubFeed("v1", Deals(4));
        var gateway = new BatchCountingGateway();

        await using var db = CreateDbContext();
        await Service(db, feed, gateway, new MarketIngestionOptions { EmbedBatchSize = 4 }).IngestAsync();

        var rows = await db.MarketEmbeddings.AsNoTracking().ToListAsync();
        Assert.Equal(4, rows.Count);
        Assert.All(rows, row => Assert.Contains(gateway.AllTexts, t => t == row.ChunkText));
        Assert.Equal(4, rows.Select(r => r.ChunkText).Distinct().Count());
    }

    [Fact]
    public async Task A_failed_batch_without_checkpointing_persists_nothing()
    {
        var gateway = new BatchCountingGateway { FailOnBatchNumber = 2 };
        await using var db = CreateDbContext();

        var result = await Service(db, new StubFeed("v1", Deals(4)), gateway, new MarketIngestionOptions { EmbedBatchSize = 2 })
            .IngestAsync();

        Assert.True(result.IsFailure);
        Assert.Contains("stub batch failure", result.Error, StringComparison.Ordinal);
        await using var verify = CreateDbContext();
        Assert.Equal(0, await verify.MarketRecords.CountAsync());
        Assert.Equal(0, await verify.MarketEmbeddings.CountAsync());
    }

    [Fact]
    public async Task Checkpointing_keeps_the_finished_batches_and_the_next_run_does_only_the_rest()
    {
        var feed = new StubFeed("v1", Deals(4));
        var options = new MarketIngestionOptions { EmbedBatchSize = 2, CheckpointPerBatch = true };

        await using (var db = CreateDbContext())
        {
            var crashed = await Service(db, feed, new BatchCountingGateway { FailOnBatchNumber = 2 }, options).IngestAsync();
            Assert.True(crashed.IsFailure);
        }

        await using (var verify = CreateDbContext())
        {
            Assert.Equal(2, await verify.MarketRecords.CountAsync());
            Assert.Equal(2, await verify.MarketEmbeddings.CountAsync());
        }

        var healthy = new BatchCountingGateway();
        await using (var db = CreateDbContext())
        {
            var resumed = await Service(db, feed, healthy, options).IngestAsync();

            Assert.True(resumed.IsSuccess);
            Assert.Equal(2, resumed.Value.Inserted);
            Assert.Equal(2, resumed.Value.Unchanged);
        }

        Assert.Equal([2], healthy.BatchSizes);
        await using var final = CreateDbContext();
        Assert.Equal(4, await final.MarketEmbeddings.CountAsync());
    }

    [Fact]
    public async Task The_tpm_limiter_holds_the_second_batch_until_the_window_has_room()
    {
        var feed = new StubFeed("v1", Deals(4));
        var gateway = new BatchCountingGateway();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero));
        // A one-token budget: every batch is larger, so each goes through only on an empty window.
        var limiter = new EmbeddingRateLimiter(1, time);

        await using var db = CreateDbContext();
        var run = Service(db, feed, gateway, new MarketIngestionOptions { EmbedBatchSize = 2 }, limiter).IngestAsync();

        await WaitUntilAsync(() => time.PendingTimers == 1);
        Assert.Equal([2], gateway.BatchSizes);
        Assert.False(run.IsCompleted);

        time.Advance(TimeSpan.FromMinutes(1));
        var result = await run.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(result.IsSuccess);
        Assert.Equal([2, 2], gateway.BatchSizes);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition was not reached in time");
            await Task.Delay(20);
        }
    }
}
