using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;
using Raffa.Market.Infrastructure.Entities;
using Raffa.Market.Ingestion;
using Raffa.Market.Retrieval;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Testcontainers.PostgreSql;

namespace Raffa.Market.Tests;

/// <summary>
/// F7-T02: the denormalised filter columns are written by ingestion and every read path filters on
/// them in SQL (<c>WHERE</c>), while rows that predate them keep answering exactly as before.
/// </summary>
public sealed class MarketFilterColumnsTests : IAsyncLifetime
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

    private static List<MarketDeal> Corpus() =>
    [
        SampleDeal.Create(recordId: "R1", supplier: "Salesforce", category: "Enterprise Software", geography: "CH", currency: "CHF", unitMetric: "per user per month"),
        SampleDeal.Create(recordId: "R2", supplier: "Salesforce, Inc.", category: "Enterprise Software", geography: "DE", currency: "EUR"),
        SampleDeal.Create(recordId: "R3", supplier: "Allianz SE", category: "Insurance", geography: "DE", currency: "EUR"),
        SampleDeal.Create(recordId: "R4", supplier: "Amazon Web Services", category: "Cloud", geography: "GB", currency: "GBP"),
        SampleDeal.Create(recordId: "R5", supplier: "Web Services Ltd", category: "cloud", geography: "GB", currency: "GBP"),
        SampleDeal.Create(recordId: "R6", supplier: "Microsoft", category: "Enterprise Software", geography: "CH", currency: "CHF"),
    ];

    private async Task IngestAsync(IReadOnlyList<MarketDeal> deals)
    {
        await using var db = CreateDbContext();
        var result = await new MarketIngestionService(
                db, new StubFeed("v1", deals), new BatchCountingGateway(), new TenantContext(),
                new FixedClock(DateTimeOffset.UtcNow))
            .IngestAsync();
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Ingestion_writes_every_filter_column()
    {
        await IngestAsync(Corpus());

        await using var db = CreateDbContext();
        var r2 = await db.MarketRecords.SingleAsync(r => r.RecordId == "R2");
        var r1 = await db.MarketRecords.SingleAsync(r => r.RecordId == "R1");

        Assert.Equal("salesforce", r2.SupplierNorm);
        Assert.Equal("sales cloud enterprise", r2.ProductNorm);
        Assert.Equal("enterprise software", r2.Category);
        Assert.Equal("DE", r2.Geography);
        Assert.Equal("EUR", r2.Currency);
        Assert.Equal(12, r2.TermMonths);
        Assert.Null(r2.UnitMetric);
        Assert.Equal("per user per month", r1.UnitMetric);
    }

    [Theory]
    [InlineData("Salesforce")]
    [InlineData("salesforce inc")]
    [InlineData("Salesforce Inc.")]
    [InlineData("Web Services")]
    [InlineData("Amazon Web Services Inc")]
    [InlineData("Allianz")]
    [InlineData("Nobody")]
    [InlineData("Micro")]
    public async Task Supplier_lookup_in_sql_equals_the_in_memory_match(string wanted)
    {
        var corpus = Corpus();
        await IngestAsync(corpus);
        await using var db = CreateDbContext();

        var found = await new MarketRecordQueryService(db).GetBySupplierAsync(wanted);

        var expected = corpus.Where(d => MarketSupplierMatch.Matches(d.Supplier, wanted)).Select(d => d.RecordId).Order();
        Assert.Equal(expected, found.Select(d => d.RecordId).Order());
    }

    [Theory]
    [InlineData("Cloud", 2)]
    [InlineData("  enterprise software ", 3)]
    [InlineData("Unknown", 0)]
    public async Task Category_lookup_is_case_insensitive_and_runs_in_sql(string category, int expected)
    {
        await IngestAsync(Corpus());
        await using var db = CreateDbContext();

        Assert.Equal(expected, (await new MarketRecordQueryService(db).GetByCategoryAsync(category)).Count);
    }

    [Fact]
    public async Task A_supplier_or_category_lookup_never_reads_the_payload_of_rows_that_do_not_match()
    {
        await IngestAsync(Corpus());
        await using var db = CreateDbContext();
        // A row that would blow up if it were ever deserialized (a JSON array is not a MarketDeal).
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO market_record (record_id, feed_version, provider, payload_json, provenance_label, updated_at, " +
            "supplier_norm, product_norm, category, geography, currency) " +
            "VALUES ('BAD', 'v1', 'p', '[]'::jsonb, 'l', now(), 'zzz', 'zzz', 'zzz', 'ZZ', 'ZZZ')");

        var service = new MarketRecordQueryService(db);

        Assert.Equal(2, (await service.GetBySupplierAsync("Salesforce")).Count);
        Assert.Equal(2, (await service.GetByCategoryAsync("cloud")).Count);
    }

    [Fact]
    public async Task Rows_that_predate_the_columns_still_answer_and_the_next_ingest_back_fills_them()
    {
        var corpus = Corpus();
        await IngestAsync(corpus);
        await using (var db = CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE market_record SET supplier_norm = NULL, product_norm = NULL, category = NULL, " +
                "geography = NULL, currency = NULL, term_months = NULL, unit_metric = NULL");
        }

        await using (var db = CreateDbContext())
        {
            var service = new MarketRecordQueryService(db);
            Assert.Equal(2, (await service.GetBySupplierAsync("Salesforce")).Count);
            Assert.Equal(2, (await service.GetByCategoryAsync("Cloud")).Count);
        }

        await using (var db = CreateDbContext())
        {
            var gateway = new BatchCountingGateway();
            var result = await new MarketIngestionService(
                    db, new StubFeed("v1", corpus), gateway, new TenantContext(), new FixedClock(DateTimeOffset.UtcNow))
                .IngestAsync();

            Assert.Equal(corpus.Count, result.Value.Unchanged);
            Assert.Equal(0, gateway.SingleCalls);
        }

        await using var verify = CreateDbContext();
        Assert.All(await verify.MarketRecords.ToListAsync(), r => Assert.NotNull(r.SupplierNorm));
    }

    [Fact]
    public async Task The_benchmark_adapter_reads_only_the_candidates_of_the_query()
    {
        await IngestAsync(Corpus());
        await using (var db = CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO market_record (record_id, feed_version, provider, payload_json, provenance_label, updated_at, " +
                "supplier_norm, product_norm) VALUES ('BAD', 'v1', 'p', '[]'::jsonb, 'l', now(), 'zzz', 'zzz')");
        }

        var factory = new SimpleFactory(_postgres.GetConnectionString());
        var adapter = new Raffa.Market.Benchmark.MarketFeedBenchmarkAdapter(factory, new FixedClock(DateTimeOffset.UtcNow));

        var result = await adapter.GetBenchmarkAsync(new Raffa.Benchmark.Contracts.BenchmarkQuery(
            "Salesforce", "Sales Cloud Enterprise", null, "CH", 100m, "12 months", "CHF", new DateOnly(2026, 6, 1)));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.NotNull(result.Value.Distribution);
    }

    [Fact]
    public void The_vector_query_filters_category_and_geography_in_sql()
    {
        var options = new DbContextOptionsBuilder<MarketDbContext>();
        MarketDbContextOptions.Configure(options, "Host=localhost;Database=offline;Username=u;Password=p");
        using var db = new MarketDbContext(options.Options);
        var vector = new float[MarketEmbeddingEntity.VectorDimensions];

        var sql = PgVectorMarketKnowledgeRetrieval
            .NearestNeighbours(db, new Vector(vector), 10, null, new MarketKnowledgeSearchFilters("Cloud", "gb"))
            .ToQueryString();

        Assert.Contains("category", sql, StringComparison.Ordinal);
        Assert.Contains("geography", sql, StringComparison.Ordinal);
    }

    private sealed class SimpleFactory(string connectionString) : IDbContextFactory<MarketDbContext>
    {
        public MarketDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<MarketDbContext>();
            MarketDbContextOptions.Configure(options, connectionString);
            return new MarketDbContext(options.Options);
        }
    }
}
