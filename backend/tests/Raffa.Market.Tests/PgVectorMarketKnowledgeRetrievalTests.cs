using System.Text.Json;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;
using Raffa.Market.Infrastructure.Entities;
using Raffa.Market.Retrieval;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Testcontainers.PostgreSql;

namespace Raffa.Market.Tests;

/// <summary>
/// Proves task E13/F02/US01/T02's own <see cref="PgVectorMarketKnowledgeRetrieval"/>: "cosine
/// distance, top-k, optional category / geography filters" — nearest-first ordering, the topK cut,
/// and both filters, against a real Postgres+pgvector database. Rows are seeded directly (not via
/// <c>MarketIngestionService</c>) so each test can pick exact, geometrically-meaningful vectors —
/// same technique
/// <c>Raffa.Documents.Contracts.Tests.EmbeddingRetrievalServiceTests.PlaneVector</c> already
/// uses for its own, provider-independent similarity-ordering proof.
/// </summary>
public sealed class PgVectorMarketKnowledgeRetrievalTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

    /// <summary>Full-width vector with <paramref name="x"/>/<paramref name="y"/> in dimensions 0/1
    /// and zero elsewhere -- same construction as
    /// <c>EmbeddingRetrievalServiceTests.PlaneVector</c>, so cosine distance between any two
    /// vectors built this way is exactly computable by hand.</summary>
    private static float[] PlaneVector(float x, float y)
    {
        var vector = new float[MarketEmbeddingEntity.VectorDimensions];
        vector[0] = x;
        vector[1] = y;
        return vector;
    }

    private sealed class StubEmbeddingGateway(IReadOnlyDictionary<string, float[]> vectorsByText) : IAiGateway
    {
        public Task<Result<AiClassificationResult>> ClassifyAsync(
            AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by PgVectorMarketKnowledgeRetrieval.");

        public Task<Result<AiExtractionResult>> ExtractAsync(
            AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by PgVectorMarketKnowledgeRetrieval.");

        public Task<Result<AiEmbeddingResult>> EmbedAsync(
            AiEmbeddingRequest request, CancellationToken cancellationToken = default)
        {
            if (!vectorsByText.TryGetValue(request.Text, out var vector))
            {
                throw new InvalidOperationException($"Stub has no configured vector for '{request.Text}'.");
            }

            var result = new AiEmbeddingResult(
                vector, new AiCallMetadata("stub-embed-model", "v1", "stub-v1", DateTimeOffset.UtcNow, "n/a"));
            return Task.FromResult(Result<AiEmbeddingResult>.Success(result));
        }

        public Task<Result<AiAnswerResult>> AnswerAsync(
            AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by PgVectorMarketKnowledgeRetrieval.");

        public Task<Result<AiOcrResult>> OcrAsync(
            AiOcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by PgVectorMarketKnowledgeRetrieval.");
    }

    private async Task SeedAsync(MarketDeal deal, string chunkText, float[] vector)
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
        db.MarketEmbeddings.Add(new MarketEmbeddingEntity
        {
            Id = EntityId.New(),
            RecordId = deal.RecordId,
            ChunkIndex = 0,
            ChunkText = chunkText,
            Vector = new Vector(vector),
            Model = "stub-embed-model",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private PgVectorMarketKnowledgeRetrieval CreateRetrieval(IAiGateway gateway)
    {
        var options = new DbContextOptionsBuilder<MarketDbContext>();
        MarketDbContextOptions.Configure(options, _postgres.GetConnectionString());
        var dbContext = new MarketDbContext(options.Options);
        return new PgVectorMarketKnowledgeRetrieval(dbContext, gateway, new TenantContext());
    }

    [Fact]
    public async Task Search_returns_hits_nearest_first_and_respects_topK()
    {
        const string query = "uplift cap Salesforce";
        const string nearText = "near note: Salesforce uplift cap 4%";
        const string mediumText = "medium note: somewhat related indemnification clause";
        const string orthogonalText = "orthogonal note: completely unrelated shipping terms";

        await SeedAsync(
            SampleDeal.Create(recordId: "MKT-VEC-ORTHO", supplier: "Zoom"),
            orthogonalText, PlaneVector(0f, 1f));
        await SeedAsync(
            SampleDeal.Create(recordId: "MKT-VEC-NEAR", supplier: "Salesforce"),
            nearText, PlaneVector(0.99f, 0.1f));
        await SeedAsync(
            SampleDeal.Create(recordId: "MKT-VEC-MEDIUM", supplier: "HubSpot"),
            mediumText, PlaneVector(0.5f, 0.866f));

        var gateway = new StubEmbeddingGateway(new Dictionary<string, float[]>
        {
            [query] = PlaneVector(1f, 0f),
        });
        var retrieval = CreateRetrieval(gateway);

        var result = await retrieval.SearchAsync(query, topK: 2);

        Assert.True(result.IsSuccess);
        var hits = result.Value;
        Assert.Equal(2, hits.Count);
        Assert.Equal("MKT-VEC-NEAR", hits[0].RecordId);
        Assert.Equal("MKT-VEC-MEDIUM", hits[1].RecordId);
        Assert.True(hits[0].Score > hits[1].Score);
        Assert.DoesNotContain(hits, h => h.RecordId == "MKT-VEC-ORTHO");
    }

    [Fact]
    public async Task Search_applies_category_and_geography_filters()
    {
        const string query = "insurance premium query";

        await SeedAsync(
            SampleDeal.Create(recordId: "MKT-VEC-CH-INSURANCE", supplier: "Allianz",
                category: "Insurance", geography: "CH"),
            "CH insurance note", PlaneVector(1f, 0f));
        await SeedAsync(
            SampleDeal.Create(recordId: "MKT-VEC-US-INSURANCE", supplier: "Allianz",
                category: "Insurance", geography: "US"),
            "US insurance note", PlaneVector(0.99f, 0.05f));
        await SeedAsync(
            SampleDeal.Create(recordId: "MKT-VEC-CH-SOFTWARE", supplier: "Salesforce",
                category: "Enterprise Software", geography: "CH"),
            "CH software note", PlaneVector(0.98f, 0.02f));

        var gateway = new StubEmbeddingGateway(new Dictionary<string, float[]>
        {
            [query] = PlaneVector(1f, 0f),
        });
        var retrieval = CreateRetrieval(gateway);

        var geographyFiltered = await retrieval.SearchAsync(
            query, topK: 10, new MarketKnowledgeSearchFilters(Geography: "CH"));
        Assert.True(geographyFiltered.IsSuccess);
        Assert.All(geographyFiltered.Value, hit => Assert.Equal("CH", hit.Geography));
        Assert.Equal(2, geographyFiltered.Value.Count);

        var categoryFiltered = await retrieval.SearchAsync(
            query, topK: 10, new MarketKnowledgeSearchFilters(Category: "Insurance"));
        Assert.True(categoryFiltered.IsSuccess);
        Assert.All(categoryFiltered.Value, hit => Assert.Equal("Insurance", hit.Category));
        Assert.Equal(2, categoryFiltered.Value.Count);

        var bothFiltered = await retrieval.SearchAsync(
            query, topK: 10, new MarketKnowledgeSearchFilters(Category: "Insurance", Geography: "CH"));
        Assert.True(bothFiltered.IsSuccess);
        var onlyHit = Assert.Single(bothFiltered.Value);
        Assert.Equal("MKT-VEC-CH-INSURANCE", onlyHit.RecordId);
    }

    [Fact]
    public async Task Search_rejects_non_positive_topK_without_calling_the_gateway()
    {
        var gateway = new StubEmbeddingGateway(new Dictionary<string, float[]>());
        var retrieval = CreateRetrieval(gateway);

        var result = await retrieval.SearchAsync("anything", topK: 0);

        Assert.True(result.IsFailure);
    }
}
