using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;
using Raffa.Market.Ingestion;
using Raffa.Market.Retrieval;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Market.Tests;

/// <summary>F7-T07 against a real Postgres + pgvector: hybrid search and diversification, off by default.</summary>
public sealed class MarketHybridSearchTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        var deals = Enumerable.Range(1, 30)
            .Select(i => SampleDeal.Create(recordId: $"H{i:00}", supplier: $"Vendor{i}", product: $"Tool{i}"))
            .Concat(Enumerable.Range(1, 6).Select(i => SampleDeal.Create(
                recordId: $"BIG{i}", supplier: "Bigco", product: $"Suite{i}", category: "Crowd")))
            .ToList();
        await using var ingest = CreateDbContext();
        var result = await new MarketIngestionService(
                ingest, new StubFeed("v1", deals), new BatchCountingGateway(), new TenantContext(),
                new FixedClock(DateTimeOffset.UtcNow))
            .IngestAsync();
        Assert.True(result.IsSuccess);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private MarketDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MarketDbContext>();
        MarketDbContextOptions.Configure(options, _postgres.GetConnectionString());
        return new MarketDbContext(options.Options);
    }

    private PgVectorMarketKnowledgeRetrieval Search(MarketDbContext db, MarketRetrievalOptions? options) =>
        new(db, new BatchCountingGateway(), new TenantContext(), null, options);

    [Fact]
    public async Task Defaults_are_the_plain_vector_search()
    {
        await using var db = CreateDbContext();

        var plain = await Search(db, null).SearchAsync("vendor7 tool7 pricing", 5);
        var explicitOff = await Search(db, new MarketRetrievalOptions()).SearchAsync("vendor7 tool7 pricing", 5);

        Assert.True(plain.IsSuccess);
        Assert.Equal(plain.Value.Select(n => (n.RecordId, n.Score)), explicitOff.Value.Select(n => (n.RecordId, n.Score)));
    }

    [Fact]
    public async Task Hybrid_puts_the_record_named_in_the_query_first()
    {
        await using var db = CreateDbContext();

        var hybrid = await Search(db, new MarketRetrievalOptions { Hybrid = true }).SearchAsync("vendor7 tool7 pricing", 5);

        Assert.True(hybrid.IsSuccess, hybrid.IsFailure ? hybrid.Error : null);
        Assert.Equal("H07", hybrid.Value[0].RecordId);
        Assert.Equal(5, hybrid.Value.Count);
        Assert.Equal(hybrid.Value.Count, hybrid.Value.Select(n => n.RecordId).Distinct().Count());
    }

    [Fact]
    public async Task Hybrid_respects_the_category_filter_on_the_lexical_side_too()
    {
        await using var db = CreateDbContext();

        var hybrid = await Search(db, new MarketRetrievalOptions { Hybrid = true })
            .SearchAsync("vendor7 tool7", 5, new MarketKnowledgeSearchFilters(Category: "Crowd"));

        Assert.True(hybrid.IsSuccess);
        Assert.All(hybrid.Value, n => Assert.Equal("Crowd", n.Category));
    }

    [Fact]
    public async Task Diversification_limits_one_supplier_in_the_top_k()
    {
        await using var db = CreateDbContext();

        var diversified = await Search(db, new MarketRetrievalOptions { Diversify = true, MaxPerSupplier = 1 })
            .SearchAsync("bigco suite", 6);

        Assert.True(diversified.IsSuccess);
        Assert.Equal(6, diversified.Value.Count);
        Assert.True(diversified.Value.Count(n => n.RecordId.StartsWith("BIG", StringComparison.Ordinal)) <= 1);
    }
}
