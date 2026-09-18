using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// Proves the Definition of Done for task E02/F02/US02/T02 (us-02-embedding-search-index,
/// AC-2/AC-3): <see cref="EmbeddingRetrievalService.SearchAsync"/> returns same-tenant vectors
/// ordered nearest-first and excludes a different tenant's vectors even when that tenant's vector
/// is the closest possible match, and both <see cref="EmbeddingRetrievalService.IndexChunkAsync"/>
/// and <see cref="EmbeddingRetrievalService.SearchAsync"/> obtain every vector from
/// <see cref="IAiGateway.EmbedAsync"/> — never a provider SDK — against a real
/// Postgres+pgvector+RLS database, mirroring <see cref="PortfolioQueryServiceTests"/>'s own
/// unprivileged-role rationale so a passing "a different tenant gets nothing back" assertion is a
/// real RLS proof, not a tautology from a superuser connection that unconditionally bypasses row
/// security.
/// </summary>
public sealed class EmbeddingRetrievalServiceTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_embedding_app";
    private const string AppRolePassword = "raffa_embedding_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());

        await using (var adminDb = new DocumentsContractsDbContext(adminOptions.Options))
        {
            // Applies Initial + AddTenantRowLevelSecurity (covers `embedding` — see task T01's
            // migration TenantScopedTables list) + the later migrations in this module.
            await adminDb.Database.MigrateAsync();

            await adminDb.Database.ExecuteSqlRawAsync(
                $"""
                CREATE ROLE {AppRoleName} LOGIN PASSWORD '{AppRolePassword}' NOSUPERUSER NOBYPASSRLS;
                GRANT USAGE ON SCHEMA public TO {AppRoleName};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRoleName};
                """);
        }

        _appConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = AppRoleName,
            Password = AppRolePassword,
        }.ConnectionString;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private DocumentsContractsDbContext CreateAppContext(ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(optionsBuilder, _appConnectionString, tenantContext);
        return new DocumentsContractsDbContext(optionsBuilder.Options);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    /// <summary>
    /// Deterministic, geometrically-meaningful stand-in for a real Foundry embed model (AC-3: the
    /// service under test must obtain every vector from here, never a provider SDK — there is
    /// nothing else it could call for one). Maps each configured input text to a hand-picked
    /// vector so the similarity ordering asserted below is exact and provider-independent, unlike
    /// <c>Raffa.AiGateway.Fixtures.FixtureAiGateway</c>'s SHA-256-derived pseudo-embedding
    /// (deterministic per text, but not geometrically meaningful for ordering assertions).
    /// </summary>
    private sealed class StubEmbeddingGateway(IReadOnlyDictionary<string, float[]> vectorsByText) : IAiGateway
    {
        public List<string> EmbeddedTexts { get; } = [];

        public Task<Result<AiClassificationResult>> ClassifyAsync(
            AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by EmbeddingRetrievalService.");

        public Task<Result<AiExtractionResult>> ExtractAsync(
            AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by EmbeddingRetrievalService.");

        public Task<Result<AiEmbeddingResult>> EmbedAsync(
            AiEmbeddingRequest request, CancellationToken cancellationToken = default)
        {
            EmbeddedTexts.Add(request.Text);

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
            throw new NotSupportedException("Not exercised by EmbeddingRetrievalService.");

        // IAiGateway grew this 5th role after this stub was written (task E02/F01/US02/T02,
        // hybrid-ocr, ADR-017) — not exercised by EmbeddingRetrievalService either.
        public Task<Result<AiOcrResult>> OcrAsync(
            AiOcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by EmbeddingRetrievalService.");
    }

    /// <summary>
    /// Full-width (<see cref="Embedding.VectorDimensions"/>) vector with <paramref name="x"/>/
    /// <paramref name="y"/> in dimensions 0/1 and zero elsewhere, so cosine distance between any
    /// two vectors built this way is exactly computable by hand (only dimensions 0/1 ever
    /// contribute to the dot product or magnitude): distance 0 for identical direction, 0.5 for a
    /// 60-degree angle, 1 for orthogonal, 2 for opposite.
    /// </summary>
    private static float[] PlaneVector(float x, float y)
    {
        var vector = new float[Embedding.VectorDimensions];
        vector[0] = x;
        vector[1] = y;
        return vector;
    }

    /// <summary>Task E28/F02/US01/T01 (NW-81) fixture: a validated-or-not contract row, varying
    /// <see cref="ContractDocumentType"/> so the "similar types" peer slice can be proven to key off
    /// it. Mirrors <c>ValidatedContractCountTests.NewContract</c>'s own shape.</summary>
    private static Contract NewContract(TenantId tenantId, ContractDocumentType type) => new()
    {
        TenantId = tenantId,
        Type = type,
        Status = "processing",
        Currency = "USD",
        AutoRenewal = false,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>Mirrors <c>ValidatedContractCountTests.NewDocument</c>'s own shape — "validated"
    /// (<see cref="PortfolioQueryService.CountValidatedContractsAsync"/>'s definition,
    /// <see cref="EmbeddingRetrievalService.SearchByContractAsync"/>'s own doc comment) means at
    /// least one linked document reached <see cref="DocumentProcessingStatus.Completed"/>.</summary>
    private static Document NewDocument(
        TenantId tenantId, EntityId contractId, DocumentProcessingStatus processingStatus, string fileName) => new()
    {
        TenantId = tenantId,
        ContractId = contractId,
        FileName = fileName,
        MimeType = "application/pdf",
        StoragePath = $"{tenantId.Value}/{fileName}",
        Checksum = $"checksum-{fileName}",
        ProcessingStatus = processingStatus,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private async Task SeedContractAsync(ITenantContext tenantContext, Contract contract)
    {
        await using var db = CreateAppContext(tenantContext);
        using var scope = tenantContext.BeginScope(contract.TenantId);
        db.Contracts.Add(contract);
        await db.SaveChangesAsync();
    }

    private async Task SeedDocumentAsync(ITenantContext tenantContext, Document document)
    {
        await using var db = CreateAppContext(tenantContext);
        using var scope = tenantContext.BeginScope(document.TenantId);
        db.Documents.Add(document);
        await db.SaveChangesAsync();
    }

    private async Task<Result<EmbeddingIndexResult>> IndexAsync(
        ITenantContext tenantContext,
        IAiGateway gateway,
        DateTimeOffset now,
        TenantId tenantId,
        string sourceType,
        EntityId sourceId,
        int chunkIndex,
        string chunkText)
    {
        // Fresh DbContext per call (mirrors PortfolioQueryServiceTests.SeedContractAsync /
        // DocumentQueryServiceTests): each EmbeddingRetrievalService call opens its own tenant
        // scope right before it touches the connection (see the service's own doc comment), so a
        // fresh context per call is the simplest way to keep that connection-open/close boundary
        // unambiguous across a test that seeds more than one tenant.
        await using var db = CreateAppContext(tenantContext);
        var service = new EmbeddingRetrievalService(db, gateway, tenantContext, new FixedClock(now));
        return await service.IndexChunkAsync(tenantId, sourceType, sourceId, chunkIndex, chunkText);
    }

    [Fact]
    public async Task Search_returns_same_tenant_matches_nearest_first_and_excludes_cross_tenant()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var tenantContext = new TenantContext();
        var now = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

        const string query = "liability cap question";
        const string nearText = "near chunk: liability cap is $1,000,000";
        const string mediumText = "medium chunk: somewhat related indemnification clause";
        const string orthogonalText = "orthogonal chunk: completely unrelated shipping terms";
        const string tenantBText = "tenant B's own liability clause";

        var vectors = new Dictionary<string, float[]>
        {
            [query] = PlaneVector(1f, 0f),
            [nearText] = PlaneVector(0.99f, 0.1f), // ~6 degrees off the query
            [mediumText] = PlaneVector(0.5f, 0.866f), // 60 degrees off the query
            [orthogonalText] = PlaneVector(0f, 1f), // 90 degrees off the query
            [tenantBText] = PlaneVector(1f, 0f), // identical direction to the query...
        };
        var gateway = new StubEmbeddingGateway(vectors);

        // Seed tenant A's three chunks out of near/medium/orthogonal order, through the real
        // IndexChunkAsync path (AC-3 proof for the write side too).
        Assert.True((await IndexAsync(
            tenantContext, gateway, now, tenantA, "Clause", EntityId.New(), 0, orthogonalText)).IsSuccess);
        Assert.True((await IndexAsync(
            tenantContext, gateway, now, tenantA, "Clause", EntityId.New(), 0, nearText)).IsSuccess);
        Assert.True((await IndexAsync(
            tenantContext, gateway, now, tenantA, "Clause", EntityId.New(), 0, mediumText)).IsSuccess);

        // ...but tenant B's row must never come back for tenant A's search, even though its vector
        // is the closest possible match (identical direction to the query).
        Assert.True((await IndexAsync(
            tenantContext, gateway, now, tenantB, "Clause", EntityId.New(), 0, tenantBText)).IsSuccess);

        await using var searchDb = CreateAppContext(tenantContext);
        var searchService = new EmbeddingRetrievalService(searchDb, gateway, tenantContext, new FixedClock(now));

        var result = await searchService.SearchAsync(tenantA, query, topK: 2);

        Assert.True(result.IsSuccess);
        var hits = result.Value;

        // topK: 2 asked for, exactly 2 returned even though tenant A has 3 candidate rows.
        Assert.Equal(2, hits.Count);

        // Nearest-first: the near chunk beats the medium chunk; the orthogonal chunk (3rd nearest
        // for tenant A) is excluded by topK entirely. AC-2: no tenant B row anywhere in the
        // result, despite its vector being a perfect match for the query.
        Assert.Equal(nearText, hits[0].ChunkText);
        Assert.Equal(mediumText, hits[1].ChunkText);
        Assert.True(hits[0].Distance < hits[1].Distance);
        Assert.DoesNotContain(hits, h => h.ChunkText == tenantBText);
        Assert.DoesNotContain(hits, h => h.ChunkText == orthogonalText);

        // AC-3: the query text itself was embedded through the gateway, not synthesised locally.
        Assert.Contains(query, gateway.EmbeddedTexts);
    }

    [Fact]
    public async Task Index_persists_the_gateways_model_id_and_the_full_width_vector()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var now = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
        const string chunkText = "sample clause text";
        var vectors = new Dictionary<string, float[]> { [chunkText] = PlaneVector(1f, 1f) };
        var gateway = new StubEmbeddingGateway(vectors);
        var sourceId = EntityId.New();

        var result = await IndexAsync(
            tenantContext, gateway, now, tenantId, "Document", sourceId, chunkIndex: 3, chunkText);

        Assert.True(result.IsSuccess);
        Assert.Equal("stub-embed-model", result.Value.Model);
        Assert.Equal(now, result.Value.CreatedAt);

        using (tenantContext.BeginScope(tenantId))
        {
            await using var readDb = CreateAppContext(tenantContext);
            var stored = await readDb.Embeddings.SingleAsync(e => e.Id == result.Value.EmbeddingId);

            Assert.Equal(tenantId, stored.TenantId);
            Assert.Equal("Document", stored.SourceType);
            Assert.Equal(sourceId, stored.SourceId);
            Assert.Equal(3, stored.ChunkIndex);
            Assert.Equal(chunkText, stored.ChunkText);
            Assert.Equal(Embedding.VectorDimensions, stored.Vector.ToArray().Length);
            Assert.Equal("stub-embed-model", stored.Model);
        }
    }

    [Fact]
    public async Task Index_rejects_empty_chunk_text_without_calling_the_gateway()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var gateway = new StubEmbeddingGateway(new Dictionary<string, float[]>());

        var result = await IndexAsync(
            tenantContext, gateway, DateTimeOffset.UtcNow, tenantId, "Document", EntityId.New(), 0, "   ");

        Assert.True(result.IsFailure);
        Assert.Empty(gateway.EmbeddedTexts);
    }

    [Fact]
    public async Task Search_rejects_non_positive_topK_without_calling_the_gateway()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var gateway = new StubEmbeddingGateway(new Dictionary<string, float[]>());
        await using var db = CreateAppContext(tenantContext);
        var service = new EmbeddingRetrievalService(db, gateway, tenantContext, new FixedClock(DateTimeOffset.UtcNow));

        var result = await service.SearchAsync(tenantId, "some query", topK: 0);

        Assert.True(result.IsFailure);
        Assert.Empty(gateway.EmbeddedTexts);
    }

    /// <summary>
    /// Proves the Definition of Done for task E28/F02/US01/T01 (NW-81, parent story
    /// us-01-rag-contract-filter AC-1/AC-2): "this contract" search does not return another
    /// supplier's MSA unless in the labelled peer slice. Four contracts, one tenant:
    /// <list type="bullet">
    /// <item><description>the scoped contract itself (Msa, validated);</description></item>
    /// <item><description>another supplier's MSA (Msa, validated) — embedded at the <em>identical</em>
    /// direction as the query, i.e. the single closest possible match in the whole tenant, so a pass
    /// here proves a hard scope rather than "whatever happens to be nearest";</description></item>
    /// <item><description>a same-type contract that is not validated yet (its only document is still
    /// <see cref="DocumentProcessingStatus.Processing"/>);</description></item>
    /// <item><description>a validated contract of a different <see cref="ContractDocumentType"/>.</description></item>
    /// </list>
    /// Only the first belongs in <see cref="EmbeddingContractScopedSearchResult.ThisContract"/>; only
    /// the second belongs in <see cref="EmbeddingContractScopedSearchResult.SimilarTypes"/> — the
    /// third and fourth belong in neither slice.
    /// </summary>
    [Fact]
    public async Task SearchByContract_excludes_another_suppliers_msa_from_this_contract_and_labels_it_as_a_peer()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var now = new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

        const string query = "liability cap question";
        const string thisContractText = "this-contract chunk: liability cap is $1,000,000";
        const string otherSupplierMsaText = "other supplier's MSA chunk: identical liability wording";
        const string unvalidatedSameTypeText = "unvalidated same-type chunk: still processing";
        const string differentTypeText = "different-type chunk: order form terms";

        var vectors = new Dictionary<string, float[]>
        {
            [query] = PlaneVector(1f, 0f),
            [thisContractText] = PlaneVector(0.99f, 0.1f), // ~6 degrees off the query
            // Identical direction to the query -- the single closest possible match in the whole
            // tenant. Proves the this-contract slice is a hard scope, not just "whatever is nearest".
            [otherSupplierMsaText] = PlaneVector(1f, 0f),
            [unvalidatedSameTypeText] = PlaneVector(0.97f, 0.2f),
            [differentTypeText] = PlaneVector(0.96f, 0.25f),
        };
        var gateway = new StubEmbeddingGateway(vectors);

        // This contract -- the scope under test.
        var thisContract = NewContract(tenantId, ContractDocumentType.Msa);
        await SeedContractAsync(tenantContext, thisContract);
        var thisDocument = NewDocument(tenantId, thisContract.Id, DocumentProcessingStatus.Completed, "this-contract.pdf");
        await SeedDocumentAsync(tenantContext, thisDocument);
        Assert.True((await IndexAsync(
            tenantContext, gateway, now, tenantId, "Document", thisDocument.Id, 0, thisContractText)).IsSuccess);

        // Another supplier's MSA: same type, validated -- NW-81's own named case. Must surface only
        // in the labelled peer slice, never in ThisContract.
        var otherSupplierContract = NewContract(tenantId, ContractDocumentType.Msa);
        await SeedContractAsync(tenantContext, otherSupplierContract);
        var otherSupplierDocument = NewDocument(
            tenantId, otherSupplierContract.Id, DocumentProcessingStatus.Completed, "other-supplier-msa.pdf");
        await SeedDocumentAsync(tenantContext, otherSupplierDocument);
        Assert.True((await IndexAsync(
            tenantContext, gateway, now, tenantId, "Document", otherSupplierDocument.Id, 0, otherSupplierMsaText)).IsSuccess);

        // Same type, but not validated (its only document is still Processing): excluded from the
        // peer slice too -- "similar types" draws only from validated contracts.
        var unvalidatedContract = NewContract(tenantId, ContractDocumentType.Msa);
        await SeedContractAsync(tenantContext, unvalidatedContract);
        var unvalidatedDocument = NewDocument(
            tenantId, unvalidatedContract.Id, DocumentProcessingStatus.Processing, "unvalidated.pdf");
        await SeedDocumentAsync(tenantContext, unvalidatedDocument);
        Assert.True((await IndexAsync(
            tenantContext, gateway, now, tenantId, "Document", unvalidatedDocument.Id, 0, unvalidatedSameTypeText)).IsSuccess);

        // Validated, but a different ContractDocumentType: excluded from the peer slice -- "similar
        // types" means same type/category, never every other validated contract.
        var differentTypeContract = NewContract(tenantId, ContractDocumentType.OrderForm);
        await SeedContractAsync(tenantContext, differentTypeContract);
        var differentTypeDocument = NewDocument(
            tenantId, differentTypeContract.Id, DocumentProcessingStatus.Completed, "different-type.pdf");
        await SeedDocumentAsync(tenantContext, differentTypeDocument);
        Assert.True((await IndexAsync(
            tenantContext, gateway, now, tenantId, "Document", differentTypeDocument.Id, 0, differentTypeText)).IsSuccess);

        await using var searchDb = CreateAppContext(tenantContext);
        var searchService = new EmbeddingRetrievalService(searchDb, gateway, tenantContext, new FixedClock(now));

        var result = await searchService.SearchByContractAsync(
            new EmbeddingSearchQuery(tenantId, query, TopK: 5, ContractId: thisContract.Id, PeerTopK: 5));

        Assert.True(result.IsSuccess);
        var thisContractHits = result.Value.ThisContract;
        var peerHits = result.Value.SimilarTypes;

        // AC-1: this-contract slice is exactly this contract's own chunk -- never the other
        // supplier's MSA, even though it is the single closest vector in the whole tenant.
        Assert.Single(thisContractHits);
        Assert.Equal(thisContractText, thisContractHits[0].ChunkText);
        Assert.DoesNotContain(thisContractHits, h => h.ChunkText == otherSupplierMsaText);
        Assert.DoesNotContain(thisContractHits, h => h.ChunkText == unvalidatedSameTypeText);
        Assert.DoesNotContain(thisContractHits, h => h.ChunkText == differentTypeText);

        // AC-2: the peer ("similar types") slice is exactly the other supplier's validated,
        // same-type MSA -- never this contract's own chunk, the unvalidated same-type contract, or
        // the validated different-type contract.
        Assert.Single(peerHits);
        Assert.Equal(otherSupplierMsaText, peerHits[0].ChunkText);
        Assert.DoesNotContain(peerHits, h => h.ChunkText == thisContractText);
        Assert.DoesNotContain(peerHits, h => h.ChunkText == unvalidatedSameTypeText);
        Assert.DoesNotContain(peerHits, h => h.ChunkText == differentTypeText);
    }

    [Fact]
    public async Task SearchByContract_with_zero_peerTopK_skips_the_peer_slice_entirely()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var now = new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

        const string query = "notice period question";
        const string thisContractText = "this-contract chunk: notice period is 90 days";
        const string otherSupplierMsaText = "other supplier's MSA chunk: notice period is 60 days";

        var vectors = new Dictionary<string, float[]>
        {
            [query] = PlaneVector(1f, 0f),
            [thisContractText] = PlaneVector(0.99f, 0.1f),
            [otherSupplierMsaText] = PlaneVector(1f, 0f),
        };
        var gateway = new StubEmbeddingGateway(vectors);

        var thisContract = NewContract(tenantId, ContractDocumentType.Msa);
        await SeedContractAsync(tenantContext, thisContract);
        var thisDocument = NewDocument(tenantId, thisContract.Id, DocumentProcessingStatus.Completed, "this-contract.pdf");
        await SeedDocumentAsync(tenantContext, thisDocument);
        Assert.True((await IndexAsync(
            tenantContext, gateway, now, tenantId, "Document", thisDocument.Id, 0, thisContractText)).IsSuccess);

        var otherSupplierContract = NewContract(tenantId, ContractDocumentType.Msa);
        await SeedContractAsync(tenantContext, otherSupplierContract);
        var otherSupplierDocument = NewDocument(
            tenantId, otherSupplierContract.Id, DocumentProcessingStatus.Completed, "other-supplier-msa.pdf");
        await SeedDocumentAsync(tenantContext, otherSupplierDocument);
        Assert.True((await IndexAsync(
            tenantContext, gateway, now, tenantId, "Document", otherSupplierDocument.Id, 0, otherSupplierMsaText)).IsSuccess);

        await using var searchDb = CreateAppContext(tenantContext);
        var searchService = new EmbeddingRetrievalService(searchDb, gateway, tenantContext, new FixedClock(now));

        var result = await searchService.SearchByContractAsync(
            new EmbeddingSearchQuery(tenantId, query, TopK: 5, ContractId: thisContract.Id, PeerTopK: 0));

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.ThisContract);
        Assert.Empty(result.Value.SimilarTypes);
    }
}
