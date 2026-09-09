using System.Text.Json;
using Contigo.Benchmark.Contracts;
using Contigo.Market.Benchmark;
using Contigo.Market.Contracts;
using Contigo.Market.Infrastructure;
using Contigo.Market.Infrastructure.Entities;
using Contigo.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Contigo.Market.Tests;

/// <summary>
/// Proves task E13/F02/US01/T02's own "MarketFeedBenchmarkAdapter likewise switches to read
/// `market_record` when the connection string is present": every matching/confidence/provenance
/// rule <c>MarketFeedBenchmarkAdapterTests</c> already proves against the mock provider holds
/// identically against <see cref="MarketFeedBenchmarkAdapter"/>'s
/// <see cref="IDbContextFactory{TContext}"/> constructor reading the same shape of data back out
/// of a real Postgres <c>market_record</c> table — seeded directly (no
/// <see cref="Contigo.Market.Ingestion.MarketIngestionService"/>/<c>IAiGateway</c> in this file at
/// all: this adapter's own <c>LoadDealsAsync</c> never touches <c>market_embedding</c>).
/// </summary>
public sealed class MarketFeedBenchmarkAdapterDbBackedTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MarketDbContext>();
        MarketDbContextOptions.Configure(options, _postgres.GetConnectionString());
        await using var db = new MarketDbContext(options.Options);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    /// <summary>Directly seeds a <c>market_record</c> row from a <see cref="MarketDeal"/> --
    /// deliberately bypassing <c>MarketIngestionService</c> (and therefore <c>IAiGateway</c>
    /// entirely): this adapter's DB-backed path never reads <c>market_embedding</c>, so a test that
    /// only exercises it needs no embedding at all.</summary>
    private async Task SeedRecordAsync(MarketDeal deal)
    {
        var options = new DbContextOptionsBuilder<MarketDbContext>();
        MarketDbContextOptions.Configure(options, _postgres.GetConnectionString());
        await using var db = new MarketDbContext(options.Options);

        db.MarketRecords.Add(new MarketRecordEntity
        {
            RecordId = deal.RecordId,
            FeedVersion = "v1",
            Provider = deal.Provider,
            PayloadJson = JsonSerializer.Serialize(deal, JsonOptions),
            ProvenanceLabel = MarketProvenance.Label(deal),
            UpdatedAt = deal.UpdatedAt,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Minimal <see cref="IDbContextFactory{TContext}"/> for this test only -- the real DI
    /// container never constructs one by hand (<c>ServiceCollectionExtensions.AddMarketModule</c>
    /// calls <c>AddDbContextFactory&lt;MarketDbContext&gt;</c>, which registers EF Core's own pooled
    /// implementation); this test needs only the interface <see cref="MarketFeedBenchmarkAdapter"/>'s
    /// constructor actually depends on -- a fresh <see cref="MarketDbContext"/> per call.</summary>
    private sealed class TestDbContextFactory(DbContextOptions<MarketDbContext> options)
        : IDbContextFactory<MarketDbContext>
    {
        public MarketDbContext CreateDbContext() => new(options);
    }

    private MarketFeedBenchmarkAdapter CreateAdapter()
    {
        var options = new DbContextOptionsBuilder<MarketDbContext>();
        MarketDbContextOptions.Configure(options, _postgres.GetConnectionString());
        var factory = new TestDbContextFactory(options.Options);
        return new MarketFeedBenchmarkAdapter(factory, new FixedClock(FixedNow));
    }

    [Fact]
    public async Task Name_is_market_feed_for_the_db_backed_constructor_too()
    {
        var adapter = CreateAdapter();
        Assert.Equal("market-feed", adapter.Name);
    }

    [Fact]
    public async Task Confident_match_is_served_from_the_persisted_market_record_table()
    {
        await SeedRecordAsync(SampleDeal.Create(
            recordId: "MKT-DB-0001",
            supplier: "Salesforce",
            product: "Sales Cloud Enterprise",
            geography: "CH",
            currency: "CHF",
            termMonths: 12,
            sampleSize: 64,
            unitPriceP25: 118m,
            unitPriceP50: 132m,
            unitPriceP75: 149m,
            updatedAt: FixedNow));

        var adapter = CreateAdapter();
        var query = new BenchmarkQuery(
            Supplier: "Salesforce",
            Product: "Sales Cloud Enterprise",
            Sku: null,
            Geography: "CH",
            Quantity: 500m,
            Term: "12 months",
            Currency: "CHF",
            PurchaseDate: new DateOnly(2026, 6, 1));

        var result = await adapter.GetBenchmarkAsync(query);

        Assert.True(result.IsSuccess);
        var benchmark = result.Value;
        Assert.True(benchmark.HasSufficientData);
        Assert.Equal(118m, benchmark.Distribution!.P25);
        Assert.Equal(132m, benchmark.Distribution.P50);
        Assert.Equal(149m, benchmark.Distribution.P75);
        Assert.Contains("mock", benchmark.Source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Thin_row_in_the_store_returns_insufficient_data_not_a_fabricated_number()
    {
        await SeedRecordAsync(SampleDeal.Create(
            recordId: "MKT-DB-0002",
            supplier: "Swiss Re",
            product: "Reinsurance Treaty",
            geography: "CH",
            currency: "CHF",
            sampleSize: 2, // below MarketFeedBenchmarkAdapter.MinimumViableSampleSize (5).
            updatedAt: FixedNow));

        var adapter = CreateAdapter();
        var query = new BenchmarkQuery(
            Supplier: "Swiss Re",
            Product: "Reinsurance Treaty",
            Sku: null,
            Geography: "CH",
            Quantity: 1m,
            Term: "12 months",
            Currency: "CHF",
            PurchaseDate: new DateOnly(2026, 2, 1));

        var result = await adapter.GetBenchmarkAsync(query);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HasSufficientData);
        Assert.Null(result.Value.Distribution);
        Assert.Equal(2, result.Value.SampleSize);
        Assert.Contains("mock", result.Value.Source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_store_returns_insufficient_data_with_no_dimensions()
    {
        var adapter = CreateAdapter();
        var query = new BenchmarkQuery(
            Supplier: "Nobody",
            Product: "Nothing",
            Sku: null,
            Geography: "US",
            Quantity: 1m,
            Term: "12 months",
            Currency: "USD",
            PurchaseDate: new DateOnly(2026, 1, 1));

        var result = await adapter.GetBenchmarkAsync(query);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HasSufficientData);
        Assert.Null(result.Value.SampleSize);
        Assert.Empty(result.Value.ComparisonDimensions);
    }
}
