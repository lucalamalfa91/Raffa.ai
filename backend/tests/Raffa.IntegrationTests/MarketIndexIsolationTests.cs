using System.Text.Json;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;
using Raffa.Market.Infrastructure.Entities;
using Raffa.Market.Retrieval;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Testcontainers.PostgreSql;

namespace Raffa.IntegrationTests;

/// <summary>
/// Proves the Definition of Done for task E13/F02/US01/T02 (parent story
/// us-01-market-intelligence AC-3, verbatim): "a tenant search never returns a market note; a
/// market search never returns tenant chunks (separate tables, separate services)".
///
/// Both halves of Ask Raffa's retrieval run against the <b>same physical Postgres database</b> in
/// this test — <see cref="DocumentsContractsDbContext"/>'s own tenant-scoped, RLS-backed
/// <c>embedding</c> table (see <see cref="AskRaffaRagCrossTenantIsolationTests"/>) and
/// <see cref="MarketDbContext"/>'s own shared, <c>tenant_id</c>-free <c>market_embedding</c> table
/// (see that type's own doc comment) — so this is not a coincidence of two searches that simply
/// never share a database; it is a structural guarantee: <see cref="EmbeddingRetrievalService.SearchAsync"/>
/// only ever queries <c>embedding</c>, <see cref="PgVectorMarketKnowledgeRetrieval.SearchAsync"/>
/// only ever queries <c>market_embedding</c>, and neither type references the other module's
/// DbContext at all (<c>Raffa.ArchitectureTests.DependencyDirectionTests</c>'s own allow-list:
/// <c>Raffa.Market</c> → <c>[SharedKernel, AiGateway, Benchmark]</c> does not include
/// <c>Raffa.Documents.Contracts</c>, and the reverse direction is never allowed either).
///
/// Lives here — not in <c>Raffa.Market.Tests</c> or <c>Raffa.Documents.Contracts.Tests</c> — per
/// this task's own file assignment: the same "cross-module proof lives in the integration project"
/// convention <see cref="SupplierCrossTenantIsolationTests"/> and
/// <see cref="AskRaffaRagCrossTenantIsolationTests"/> already establish. Neither module is wired
/// into <c>Raffa.Api</c>'s <c>Program.cs</c> together yet in a way this project's own
/// <c>WebApplicationFactory&lt;Program&gt;</c> fixtures could exercise both through HTTP at once, so
/// — mirroring <c>SupplierCrossTenantIsolationTests</c> — this test drives
/// <see cref="EmbeddingRetrievalService"/> and <see cref="PgVectorMarketKnowledgeRetrieval"/>
/// directly, each constructed by hand against one shared Testcontainers connection string, rather
/// than through either host's own DI container.
///
/// Rows are seeded directly (not through a full upload pipeline or <c>MarketIngestionService</c>) —
/// same technique <c>MarketFeedBenchmarkAdapterDbBackedTests.SeedRecordAsync</c> and
/// <c>PgVectorMarketKnowledgeRetrievalTests.SeedAsync</c> already use in <c>Raffa.Market.Tests</c>
/// — so each test can pick the exact, distinctive content its own cross-contamination assertion
/// needs. The embedding vector itself is irrelevant to what this test proves (query text never even
/// has to resemble the seeded content for the isolation guarantee to hold — a query only ever reaches
/// its own module's table), so both sides share one constant-vector stub gateway rather than a real
/// embedding model.
/// </summary>
public sealed class MarketIndexIsolationTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        // Both modules' migrations land in the SAME database -- see the type doc comment for why
        // that is the point, not an incidental setup detail. Mirrors R0IntegrationFixture's own
        // "several DbContexts, one connection string, migrated in sequence" shape.
        var documentsOptions = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(documentsOptions, connectionString);
        await using (var db = new DocumentsContractsDbContext(documentsOptions.Options))
        {
            await db.Database.MigrateAsync();
        }

        var marketOptions = new DbContextOptionsBuilder<MarketDbContext>();
        MarketDbContextOptions.Configure(marketOptions, connectionString);
        await using (var db = new MarketDbContext(marketOptions.Options))
        {
            await db.Database.MigrateAsync();
        }
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    /// <summary>Stub <see cref="IAiGateway"/> shared by both sides of this test. This proof is about
    /// which <em>table</em> a search reaches, never about vector-similarity ranking
    /// (<c>PgVectorMarketKnowledgeRetrievalTests</c> already proves ranking against real,
    /// geometrically-meaningful vectors) -- every call returns the same fixed, valid-dimension
    /// vector regardless of input text.</summary>
    private sealed class ConstantEmbeddingGateway : IAiGateway
    {
        public Task<Result<AiClassificationResult>> ClassifyAsync(
            AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this isolation test.");

        public Task<Result<AiExtractionResult>> ExtractAsync(
            AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this isolation test.");

        public Task<Result<AiEmbeddingResult>> EmbedAsync(
            AiEmbeddingRequest request, CancellationToken cancellationToken = default)
        {
            var vector = new float[MarketEmbeddingEntity.VectorDimensions];
            Array.Fill(vector, 0.1f);
            var result = new AiEmbeddingResult(
                vector, new AiCallMetadata("stub-embed-model", "v1", "stub-v1", DateTimeOffset.UtcNow, "n/a"));
            return Task.FromResult(Result<AiEmbeddingResult>.Success(result));
        }

        public Task<Result<AiAnswerResult>> AnswerAsync(
            AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this isolation test.");

        public Task<Result<AiOcrResult>> OcrAsync(
            AiOcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this isolation test.");
    }

    private DocumentsContractsDbContext CreateDocumentsContext()
    {
        var options = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(options, _postgres.GetConnectionString());
        return new DocumentsContractsDbContext(options.Options);
    }

    private MarketDbContext CreateMarketContext()
    {
        var options = new DbContextOptionsBuilder<MarketDbContext>();
        MarketDbContextOptions.Configure(options, _postgres.GetConnectionString());
        return new MarketDbContext(options.Options);
    }

    private EmbeddingRetrievalService CreateTenantRetrieval() =>
        new(CreateDocumentsContext(), new ConstantEmbeddingGateway(), new TenantContext(), new SystemClock());

    private PgVectorMarketKnowledgeRetrieval CreateMarketRetrieval() =>
        new(CreateMarketContext(), new ConstantEmbeddingGateway(), new TenantContext());

    private static MarketDeal SampleMarketDeal(string recordId, string supplier) => new(
        Provider: "Internal Dataset",
        RecordId: recordId,
        Supplier: supplier,
        Category: "Insurance",
        Product: "Commercial Property",
        Geography: "CH",
        Currency: "CHF",
        CompanySizeBand: "500-2000",
        TermMonths: 12,
        AnnualValueBand: "250k-500k",
        UnitPriceP25: 118m,
        UnitPriceP50: 132m,
        UnitPriceP75: 149m,
        NegotiatedClauses: [],
        ClosingPeriod: "2026-Q1",
        SampleSize: 64,
        Source: "mock",
        Representative: true,
        UpdatedAt: FixedNow);

    /// <summary>Seeds one <c>market_record</c> + <c>market_embedding</c> row directly, bypassing
    /// <c>MarketIngestionService</c> (same technique <c>PgVectorMarketKnowledgeRetrievalTests.SeedAsync</c>
    /// already uses) -- this test only needs the row to exist and be searchable, not a full
    /// ingestion pass.</summary>
    private async Task SeedMarketNoteAsync(string recordId, string supplier, string chunkText)
    {
        var deal = SampleMarketDeal(recordId, supplier);
        var vectorValues = new float[MarketEmbeddingEntity.VectorDimensions];
        Array.Fill(vectorValues, 0.1f);

        await using var db = CreateMarketContext();
        db.MarketRecords.Add(new MarketRecordEntity
        {
            RecordId = recordId,
            FeedVersion = "v1",
            Provider = deal.Provider,
            PayloadJson = JsonSerializer.Serialize(deal, JsonOptions),
            ProvenanceLabel = MarketProvenance.Label(deal),
            UpdatedAt = deal.UpdatedAt,
        });
        db.MarketEmbeddings.Add(new MarketEmbeddingEntity
        {
            Id = EntityId.New(),
            RecordId = recordId,
            ChunkIndex = 0,
            ChunkText = chunkText,
            Vector = new Vector(vectorValues),
            Model = "stub-embed-model",
            CreatedAt = FixedNow,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Tenant_search_never_returns_a_market_note()
    {
        var tenantId = TenantId.New();
        var tenantDocumentId = EntityId.New();

        await SeedMarketNoteAsync(
            recordId: "MKT-ISO-0001",
            supplier: "Zurich",
            chunkText: "Companies buying Zurich commercial property insurance paid a representative "
                + "premium with a 4% uplift cap -- market note, never a tenant's own contract.");

        var tenantRetrieval = CreateTenantRetrieval();
        var indexResult = await tenantRetrieval.IndexChunkAsync(
            tenantId, "Document", tenantDocumentId, 0,
            "This tenant's own liability cap is $1,000,000 under its AWS master services agreement.");
        Assert.True(indexResult.IsSuccess, indexResult.IsFailure ? indexResult.Error : string.Empty);

        var searchResult = await tenantRetrieval.SearchAsync(
            tenantId, "Zurich insurance premium uplift cap market note", topK: 10);

        Assert.True(searchResult.IsSuccess, searchResult.IsFailure ? searchResult.Error : string.Empty);
        var hits = searchResult.Value;

        // The `embedding` table holds exactly one row for this tenant (the one just indexed) --
        // EmbeddingRetrievalService.SearchAsync structurally cannot return the market_embedding row
        // seeded above (different DbContext, different table, no join between them anywhere in this
        // codebase), so the only possible hit is the tenant's own.
        var hit = Assert.Single(hits);
        Assert.Equal(tenantDocumentId, hit.SourceId);
        Assert.Equal("Document", hit.SourceType);

        // Belt-and-suspenders, mirroring AskRaffaRagCrossTenantIsolationTests's own
        // Assert.DoesNotContain: the market note's own distinctive phrase, verbatim, never appears
        // in a tenant search's ChunkText (which echoes exactly what was indexed -- see
        // EmbeddingRetrievalService.SearchAsync's own projection).
        Assert.DoesNotContain(
            hits, h => h.ChunkText.Contains("uplift cap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Market_search_never_returns_tenant_chunks()
    {
        var tenantId = TenantId.New();
        var tenantDocumentId = EntityId.New();
        const string marketRecordId = "MKT-ISO-0002";

        var tenantRetrieval = CreateTenantRetrieval();
        var indexResult = await tenantRetrieval.IndexChunkAsync(
            tenantId, "Document", tenantDocumentId, 0,
            "This tenant's own liability cap is $2,000,000 under a completely different agreement.");
        Assert.True(indexResult.IsSuccess, indexResult.IsFailure ? indexResult.Error : string.Empty);

        await SeedMarketNoteAsync(
            recordId: marketRecordId,
            supplier: "Zurich",
            chunkText: "Companies buying Zurich commercial property insurance paid a representative premium.");

        var marketRetrieval = CreateMarketRetrieval();
        var searchResult = await marketRetrieval.SearchAsync(
            "tenant liability cap master services agreement", topK: 10);

        Assert.True(searchResult.IsSuccess, searchResult.IsFailure ? searchResult.Error : string.Empty);
        var hits = searchResult.Value;

        // The `market_embedding` table holds exactly one row (the market note just seeded) --
        // PgVectorMarketKnowledgeRetrieval.SearchAsync structurally cannot return the tenant's
        // `embedding` row indexed above (different DbContext, different table, no join between them
        // anywhere in this codebase), so the only possible hit is the market note's own record.
        // (Unlike the tenant side, MarketNote.Snippet is recomposed from the MarketDeal's own fields
        // by MarketNoteComposer -- never an echo of the raw seeded chunk text -- so RecordId, not
        // Snippet content, is this assertion's meaningful check; see PgVectorMarketKnowledgeRetrieval
        // .SearchAsync's own "var note = MarketNoteComposer.Compose(deal)" line.)
        var hit = Assert.Single(hits);
        Assert.Equal(marketRecordId, hit.RecordId);
    }
}
