using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;
using Raffa.Market.Ingestion;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Market.Tests;

/// <summary>A-11: the ingestion job runs every batch of changed records through the curator before embedding and storing them.</summary>
public sealed class MarketIngestionCurationTests : IAsyncLifetime
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

    /// <summary>Rejects the records named in <see cref="Reject"/> and fills a unit on the others; records every batch it sees.</summary>
    private sealed class FakeCurator : IMarketBatchCurator
    {
        public HashSet<string> Reject { get; } = [];

        public List<IReadOnlyList<string>> Batches { get; } = [];

        public Task<MarketCurationOutcome> CurateAsync(IReadOnlyList<MarketDeal> deals, CancellationToken cancellationToken = default)
        {
            Batches.Add(deals.Select(d => d.RecordId).ToList());
            var rejected = deals.Where(d => Reject.Contains(d.RecordId))
                .Select(d => new MarketRejection(d.RecordId, ["implausible-spread"])).ToList();
            var accepted = deals.Where(d => !Reject.Contains(d.RecordId))
                .Select(d => d with { UnitMetric = d.UnitMetric ?? "per user per month" }).ToList();
            return Task.FromResult(new MarketCurationOutcome(accepted, rejected));
        }
    }

    private static List<MarketDeal> Deals() =>
        Enumerable.Range(1, 5).Select(i => SampleDeal.Create(recordId: $"C{i}", supplier: $"Vendor {i}")).ToList();

    private async Task<IngestionSummary> RunAsync(
        StubFeed feed, BatchCountingGateway gateway, FakeCurator curator, int batchSize = 1)
    {
        await using var db = CreateDbContext();
        var result = await new MarketIngestionService(
                db, feed, gateway, new TenantContext(), new FixedClock(DateTimeOffset.UtcNow),
                new MarketIngestionOptions { EmbedBatchSize = batchSize }, null, curator)
            .IngestAsync();
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        return result.Value;
    }

    [Fact]
    public async Task Rejected_records_are_neither_embedded_nor_stored_and_accepted_ones_are_stored_as_curated()
    {
        var curator = new FakeCurator();
        curator.Reject.Add("C2");
        var gateway = new BatchCountingGateway();

        var summary = await RunAsync(new StubFeed("v1", Deals()), gateway, curator, batchSize: 5);

        Assert.Equal(4, summary.Inserted);
        var rejection = Assert.Single(summary.Rejections!);
        Assert.Equal("C2", rejection.RecordId);
        Assert.Equal([4], gateway.BatchSizes);

        await using var db = CreateDbContext();
        Assert.Equal(["C1", "C3", "C4", "C5"], await db.MarketRecords.Select(r => r.RecordId).OrderBy(x => x).ToListAsync());
        Assert.All(await db.MarketRecords.ToListAsync(), r => Assert.Contains("per user per month", r.PayloadJson, StringComparison.Ordinal));
        Assert.Equal(4, await db.MarketEmbeddings.CountAsync());
        Assert.Single(curator.Batches);
    }

    [Fact]
    public async Task A_curated_record_is_still_unchanged_on_the_next_run_so_only_the_rejected_are_reviewed_again()
    {
        var curator = new FakeCurator();
        curator.Reject.Add("C2");
        var feed = new StubFeed("v1", Deals());
        await RunAsync(feed, new BatchCountingGateway(), curator, batchSize: 5);

        var gateway = new BatchCountingGateway();
        var second = new FakeCurator();
        second.Reject.Add("C2");
        var summary = await RunAsync(feed, gateway, second, batchSize: 5);

        Assert.Equal(4, summary.Unchanged);
        Assert.Equal(0, summary.Inserted + summary.Updated);
        Assert.Equal(0, gateway.SingleCalls + gateway.BatchSizes.Count);
        Assert.Equal([["C2"]], second.Batches);
    }

    [Fact]
    public async Task Without_a_curator_the_feed_is_stored_as_it_is()
    {
        await using var db = CreateDbContext();
        var result = await new MarketIngestionService(
                db, new StubFeed("v1", Deals()), new BatchCountingGateway(), new TenantContext(),
                new FixedClock(DateTimeOffset.UtcNow))
            .IngestAsync();

        Assert.Equal(5, result.Value.Inserted);
        Assert.Empty(result.Value.Rejections ?? []);
        Assert.DoesNotContain("per user per month", (await db.MarketRecords.FirstAsync()).PayloadJson, StringComparison.Ordinal);
    }
}
