using System.Text.Json;
using Raffa.Benchmark;
using Raffa.Benchmark.Contracts;
using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;
using Raffa.Market.Infrastructure.Entities;
using Raffa.Market.Retrieval;
using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pgvector;
using Testcontainers.PostgreSql;

namespace Raffa.Market.Tests;

/// <summary>
/// Proves the Definition of Done for task E13/F02/US01/T02 (parent story AC-2; task objective):
/// "with a throwing IMarketIntelligenceProvider, IBenchmarkService and IMarketKnowledgeRetrieval
/// still answer from the store" — through the real
/// <see cref="ServiceCollectionExtensions.AddMarketModule"/> composition, not a hand-constructed
/// adapter instance that would trivially never call a provider it was never given. A throwing
/// <see cref="IMarketIntelligenceProvider"/> is registered in the exact same container as every
/// other Market service; if the DB-backed benchmark adapter or retrieval ever regressed into
/// calling it at question time (ADR-024 violation), this test would fail with the stub's own
/// exception instead of a successful result.
/// </summary>
public sealed class MarketModuleQuestionTimeIsolationTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private sealed class ThrowingMarketIntelligenceProvider : IMarketIntelligenceProvider
    {
        public Task<Result<MarketFeedSnapshot>> GetDealsAsync(
            string? feedVersion = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "IMarketIntelligenceProvider must never be called at question time (ADR-024) -- " +
                "only Ingestion.MarketIngestionService may call it.");
    }

    /// <summary>Minimal, always-succeeding <see cref="IAuditWriter"/> stand-in so
    /// <c>Raffa.AiGateway.Logging.LoggingAiGateway</c> (which every real <see cref="Raffa.AiGateway.IAiGateway"/>
    /// consumer in this composition resolves behind) can construct without pulling in the whole
    /// <c>Raffa.Audit</c> module and its own database for this test.</summary>
    private sealed class NoOpAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private async Task<ServiceProvider> BuildMigratedContainerAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        // Registered before AddMarketModule so its own TryAddSingleton<IMarketIntelligenceProvider,
        // MockMarketIntelligenceProvider> (still present, harmlessly, in the in-memory half of that
        // method) becomes a no-op -- this throwing stub occupies the slot instead. Mirrors every
        // AddXxxModule's own documented "any module may register defensively, first wins" rule.
        services.AddSingleton<IMarketIntelligenceProvider, ThrowingMarketIntelligenceProvider>();
        services.AddSingleton<IAuditWriter, NoOpAuditWriter>();

        services.AddBenchmarkModule();
        services.AddMarketModule(_postgres.GetConnectionString());

        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<MarketDbContext>();
            await dbContext.Database.MigrateAsync();
        }

        return provider;
    }

    [Fact]
    public async Task Benchmark_and_retrieval_answer_from_the_store_even_with_a_throwing_provider_registered()
    {
        await using var provider = await BuildMigratedContainerAsync();

        var deal = SampleDeal.Create(
            recordId: "MKT-ISO-0001",
            supplier: "Salesforce",
            product: "Sales Cloud Enterprise",
            geography: "CH",
            currency: "CHF",
            termMonths: 12,
            sampleSize: 64,
            unitPriceP25: 118m,
            unitPriceP50: 132m,
            unitPriceP75: 149m);

        using (var seedScope = provider.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<MarketDbContext>();
            dbContext.MarketRecords.Add(new MarketRecordEntity
            {
                RecordId = deal.RecordId,
                FeedVersion = "v1",
                Provider = deal.Provider,
                PayloadJson = JsonSerializer.Serialize(deal, JsonOptions),
                ProvenanceLabel = MarketProvenance.Label(deal),
                UpdatedAt = deal.UpdatedAt,
            });
            dbContext.MarketEmbeddings.Add(new MarketEmbeddingEntity
            {
                Id = EntityId.New(),
                RecordId = deal.RecordId,
                ChunkIndex = 0,
                ChunkText = "Salesforce Sales Cloud Enterprise note",
                Vector = new Vector(new float[MarketEmbeddingEntity.VectorDimensions]),
                Model = "seed-model",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await dbContext.SaveChangesAsync();
        }

        using var scope = provider.CreateScope();

        var benchmarkService = scope.ServiceProvider.GetRequiredService<IBenchmarkService>();
        var benchmarkResult = await benchmarkService.GetBenchmarkAsync(new BenchmarkQuery(
            Supplier: "Salesforce",
            Product: "Sales Cloud Enterprise",
            Sku: null,
            Geography: "CH",
            Quantity: 500m,
            Term: "12 months",
            Currency: "CHF",
            PurchaseDate: new DateOnly(2026, 6, 1)));

        Assert.True(benchmarkResult.IsSuccess);
        Assert.True(benchmarkResult.Value.HasSufficientData);
        Assert.Equal(132m, benchmarkResult.Value.Distribution!.P50);

        var retrieval = scope.ServiceProvider.GetRequiredService<IMarketKnowledgeRetrieval>();
        var retrievalResult = await retrieval.SearchAsync("Salesforce uplift cap", topK: 5);

        Assert.True(retrievalResult.IsSuccess);
        Assert.Contains(retrievalResult.Value, hit => hit.RecordId == "MKT-ISO-0001");
    }
}
