using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;
using Raffa.Market.Ingestion;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Market.Tests;

/// <summary>
/// Proves the Definition of Done for task E13/F02/US01/T02 (parent story AC-4; task objective):
/// "a second run with the same feed version and payload hash changes zero rows" — against a real
/// Postgres+pgvector database, not an in-memory stand-in, so <see cref="MarketDbContext"/>'s own
/// snake_case/jsonb/vector mapping is exercised too.
/// </summary>
public sealed class MarketIngestionServiceTests : IAsyncLifetime
{
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

    private MarketDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MarketDbContext>();
        MarketDbContextOptions.Configure(options, _postgres.GetConnectionString());
        return new MarketDbContext(options.Options);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    /// <summary>Serves a caller-supplied, mutable deal list under one feed version — unlike
    /// <c>Mock.MockMarketIntelligenceProvider</c>, this stub lets each test control exactly which
    /// deals ingestion sees on a given call, including a deliberately-changed deal between two
    /// ingestion runs.</summary>
    private sealed class StubMarketIntelligenceProvider(string feedVersion, IReadOnlyList<MarketDeal> deals)
        : IMarketIntelligenceProvider
    {
        public Task<Result<MarketFeedSnapshot>> GetDealsAsync(
            string? requestedFeedVersion = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<MarketFeedSnapshot>.Success(new MarketFeedSnapshot(deals, feedVersion)));
    }

    /// <summary>Deterministic <see cref="IAiGateway"/> stand-in: maps each configured composed-note
    /// text to a hand-picked vector (never a live model) and records every embedded text, so a test
    /// can assert exactly how many times — and on what input — ingestion actually called
    /// <see cref="IAiGateway.EmbedAsync"/>. Mirrors
    /// <c>Raffa.Documents.Contracts.Tests.EmbeddingRetrievalServiceTests.StubEmbeddingGateway</c>.
    /// Deliberately not wrapped in <c>Raffa.AiGateway.Logging.LoggingAiGateway</c>: this test
    /// proves <see cref="MarketIngestionService"/>'s own insert/update/unchanged counting, not the
    /// gateway decorator's own audit-writing mechanics (covered by <c>Raffa.AiGateway.Tests</c>).
    /// </summary>
    private sealed class StubEmbeddingGateway : IAiGateway
    {
        public List<string> EmbeddedTexts { get; } = [];

        public bool FailNextEmbed { get; set; }

        public Task<Result<AiClassificationResult>> ClassifyAsync(
            AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by MarketIngestionService.");

        public Task<Result<AiExtractionResult>> ExtractAsync(
            AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by MarketIngestionService.");

        public Task<Result<AiEmbeddingResult>> EmbedAsync(
            AiEmbeddingRequest request, CancellationToken cancellationToken = default)
        {
            EmbeddedTexts.Add(request.Text);

            if (FailNextEmbed)
            {
                return Task.FromResult(Result<AiEmbeddingResult>.Failure("stub embed failure"));
            }

            var vector = new float[Infrastructure.Entities.MarketEmbeddingEntity.VectorDimensions];
            var seed = request.Text.GetHashCode();
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] = ((seed + i) % 1000) / 1000f;
            }

            var result = new AiEmbeddingResult(
                vector, new AiCallMetadata("stub-embed-model", "v1", "stub-v1", DateTimeOffset.UtcNow, "n/a"));
            return Task.FromResult(Result<AiEmbeddingResult>.Success(result));
        }

        public Task<Result<AiAnswerResult>> AnswerAsync(
            AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by MarketIngestionService.");

        public Task<Result<AiOcrResult>> OcrAsync(
            AiOcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by MarketIngestionService.");
    }

    private static IReadOnlyList<MarketDeal> ThreeSampleDeals() =>
    [
        SampleDeal.Create(recordId: "MKT-ING-0001", supplier: "Salesforce", product: "Sales Cloud Enterprise"),
        SampleDeal.Create(recordId: "MKT-ING-0002", supplier: "Allianz", category: "Insurance", product: "Commercial Property Insurance"),
        SampleDeal.Create(recordId: "MKT-ING-0003", supplier: "AWS", product: "EC2 Compute", sku: "m5.large"),
    ];

    [Fact]
    public async Task First_ingest_inserts_every_record_and_embeds_each_composed_note_once()
    {
        var deals = ThreeSampleDeals();
        var provider = new StubMarketIntelligenceProvider("v1", deals);
        var gateway = new StubEmbeddingGateway();
        var tenantContext = new TenantContext();

        await using var db = CreateDbContext();
        var service = new MarketIngestionService(db, provider, gateway, tenantContext, new FixedClock(DateTimeOffset.UtcNow));

        var result = await service.IngestAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("v1", result.Value.FeedVersion);
        Assert.Equal(3, result.Value.Inserted);
        Assert.Equal(0, result.Value.Updated);
        Assert.Equal(0, result.Value.Unchanged);
        Assert.Equal(3, gateway.EmbeddedTexts.Count);

        await using var verifyDb = CreateDbContext();
        Assert.Equal(3, await verifyDb.MarketRecords.CountAsync());
        Assert.Equal(3, await verifyDb.MarketEmbeddings.CountAsync());
    }

    [Fact]
    public async Task Second_ingest_of_the_same_feed_changes_zero_rows_and_calls_the_gateway_zero_times()
    {
        var deals = ThreeSampleDeals();
        var provider = new StubMarketIntelligenceProvider("v1", deals);
        var gateway = new StubEmbeddingGateway();
        var tenantContext = new TenantContext();

        await using (var firstDb = CreateDbContext())
        {
            var firstService = new MarketIngestionService(
                firstDb, provider, gateway, tenantContext, new FixedClock(DateTimeOffset.UtcNow));
            var firstResult = await firstService.IngestAsync();
            Assert.True(firstResult.IsSuccess);
            Assert.Equal(3, firstResult.Value.Inserted);
        }

        Assert.Equal(3, gateway.EmbeddedTexts.Count);

        await using var secondDb = CreateDbContext();
        var secondService = new MarketIngestionService(
            secondDb, provider, gateway, tenantContext, new FixedClock(DateTimeOffset.UtcNow));
        var secondResult = await secondService.IngestAsync();

        Assert.True(secondResult.IsSuccess);
        Assert.Equal(0, secondResult.Value.Inserted);
        Assert.Equal(0, secondResult.Value.Updated);
        Assert.Equal(3, secondResult.Value.Unchanged);

        // The literal "changes zero rows" claim: no new embed call at all on the unchanged path.
        Assert.Equal(3, gateway.EmbeddedTexts.Count);

        await using var verifyDb = CreateDbContext();
        Assert.Equal(3, await verifyDb.MarketRecords.CountAsync());
        Assert.Equal(3, await verifyDb.MarketEmbeddings.CountAsync());
    }

    [Fact]
    public async Task Changed_deal_updates_and_replaces_its_single_embedding_row()
    {
        var originalDeals = ThreeSampleDeals();
        var provider = new StubMarketIntelligenceProvider("v1", originalDeals);
        var gateway = new StubEmbeddingGateway();
        var tenantContext = new TenantContext();

        await using (var firstDb = CreateDbContext())
        {
            var firstService = new MarketIngestionService(
                firstDb, provider, gateway, tenantContext, new FixedClock(DateTimeOffset.UtcNow));
            await firstService.IngestAsync();
        }

        string originalChunkText;
        await using (var readDb = CreateDbContext())
        {
            originalChunkText = (await readDb.MarketEmbeddings
                .SingleAsync(e => e.RecordId == "MKT-ING-0002")).ChunkText;
        }

        // A materially different Salesforce/Allianz/AWS feed -- MKT-ING-0002's own price moved,
        // so its composed narrative (and therefore its embedded text) changes; the other two are
        // byte-identical to the first run.
        var changedDeals = new List<MarketDeal>(originalDeals);
        var changedIndex = changedDeals.FindIndex(d => d.RecordId == "MKT-ING-0002");
        changedDeals[changedIndex] = changedDeals[changedIndex] with { UnitPriceP50 = 999_000m };
        var changedProvider = new StubMarketIntelligenceProvider("v1", changedDeals);

        await using var secondDb = CreateDbContext();
        var secondService = new MarketIngestionService(
            secondDb, changedProvider, gateway, tenantContext, new FixedClock(DateTimeOffset.UtcNow));
        var secondResult = await secondService.IngestAsync();

        Assert.True(secondResult.IsSuccess);
        Assert.Equal(0, secondResult.Value.Inserted);
        Assert.Equal(1, secondResult.Value.Updated);
        Assert.Equal(2, secondResult.Value.Unchanged);

        // Only the changed record's own note text was (re-)embedded on this run.
        Assert.Equal(4, gateway.EmbeddedTexts.Count); // 3 from the first run + 1 here.

        await using var verifyDb = CreateDbContext();
        // Replaced, not duplicated: still exactly one embedding chunk for the changed record.
        var embeddingsForChangedRecord = await verifyDb.MarketEmbeddings
            .Where(e => e.RecordId == "MKT-ING-0002")
            .ToListAsync();
        var onlyEmbedding = Assert.Single(embeddingsForChangedRecord);
        Assert.NotEqual(originalChunkText, onlyEmbedding.ChunkText);

        var updatedRecord = await verifyDb.MarketRecords.SingleAsync(r => r.RecordId == "MKT-ING-0002");
        Assert.Contains("999000", updatedRecord.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Embed_failure_mid_run_fails_the_whole_call_and_persists_nothing()
    {
        // Fixture order matters here: StubEmbeddingGateway.FailNextEmbed is sticky (every
        // subsequent EmbedAsync call also fails), so with three deals to process the very first
        // embed call already fails -- this is the "nothing yet saved" case by construction, not by
        // ordering luck.
        var deals = ThreeSampleDeals();
        var provider = new StubMarketIntelligenceProvider("v1", deals);
        var gateway = new StubEmbeddingGateway { FailNextEmbed = true };
        var tenantContext = new TenantContext();

        await using var db = CreateDbContext();
        var service = new MarketIngestionService(db, provider, gateway, tenantContext, new FixedClock(DateTimeOffset.UtcNow));

        var result = await service.IngestAsync();

        Assert.True(result.IsFailure);
        Assert.Contains("stub embed failure", result.Error, StringComparison.Ordinal);

        await using var verifyDb = CreateDbContext();
        Assert.Equal(0, await verifyDb.MarketRecords.CountAsync());
        Assert.Equal(0, await verifyDb.MarketEmbeddings.CountAsync());
    }
}
