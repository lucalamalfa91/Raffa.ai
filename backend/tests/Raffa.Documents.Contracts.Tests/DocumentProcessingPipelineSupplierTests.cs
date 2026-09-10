using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Application.Admission;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// Proves task E13/F03/US01/T02's supplier-linking half (parent story us-01-supplier-identity AC-3,
/// requirements R-SUP-01/R-SUP-02/R-SUP-03): <see cref="DocumentProcessingPipeline"/> turns the
/// `metadata` stage's accepted <c>supplier</c> fact into <see cref="Contract.SupplierId"/> through
/// <see cref="ISupplierResolver"/>, skips it when the fact was too weak to trust, works at all
/// without the Suppliers module composed in, and — because the link is made on every processing
/// pass, not only the first — back-fills a contract that was extracted before any of this existed.
///
/// A separate class from <see cref="StagedExtractionServiceTests"/> on purpose: that one proves what
/// the extraction service decides (which facts are accepted, what evidence is recorded), this one
/// proves what the orchestrator does with that decision. Runs against the same real
/// Postgres+pgvector Testcontainer for the same reason — the pipeline persists through
/// <see cref="DocumentsContractsDbContext"/> and indexes real <see cref="Embedding"/> rows, neither
/// of which an in-memory provider would exercise honestly.
/// </summary>
public sealed class DocumentProcessingPipelineSupplierTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The supplier's legal name as the document writes it (requirements R-SUP-01: "legal
    /// name, page, span, confidence"). Kept verbatim through the pipeline — normalizing it for
    /// matching is <c>Raffa.Suppliers.Products</c>'s job.</summary>
    private const string SupplierLegalName = "Salesforce, Inc.";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var db = CreateContext(new TenantContext());
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private DocumentsContractsDbContext CreateContext(ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString(), tenantContext);
        return new DocumentsContractsDbContext(optionsBuilder.Options);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class NoOpAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    /// <summary>Scripts only the `extract` role (keyed by <see cref="AiExtractionRequest.StageName"/>)
    /// and delegates the rest to a real <see cref="FixtureAiGateway"/> — the pipeline genuinely calls
    /// <c>EmbedAsync</c> for every page it indexes, so a throw-everything fake would not get through
    /// <c>IndexForRetrievalAsync</c>. Same "wrap and override one role" shape
    /// <c>Raffa.IntegrationTests.ScriptedR1AiGateway</c> already uses.</summary>
    private sealed class ScriptedExtractGateway(IAiGateway inner, IReadOnlyDictionary<string, string> payloadByStage)
        : IAiGateway
    {
        public Task<Result<AiClassificationResult>> ClassifyAsync(
            AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            inner.ClassifyAsync(request, cancellationToken);

        public Task<Result<AiExtractionResult>> ExtractAsync(
            AiExtractionRequest request, CancellationToken cancellationToken = default)
        {
            var payloadJson = payloadByStage.TryGetValue(request.StageName, out var payload) ? payload : "{}";
            var metadata = new AiCallMetadata("test-extract-model", "1", "test-v1", Now, "test-input-hash");

            return Task.FromResult(Result<AiExtractionResult>.Success(new AiExtractionResult(payloadJson, metadata)));
        }

        public Task<Result<AiEmbeddingResult>> EmbedAsync(
            AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            inner.EmbedAsync(request, cancellationToken);

        public Task<Result<AiAnswerResult>> AnswerAsync(
            AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            inner.AnswerAsync(request, cancellationToken);

        public Task<Result<AiOcrResult>> OcrAsync(
            AiOcrRequest request, CancellationToken cancellationToken = default) =>
            inner.OcrAsync(request, cancellationToken);
    }

    /// <summary>In-memory stand-in for <c>Raffa.Suppliers.Products.Application.SupplierResolver</c>
    /// with the same observable contract: match-or-create, and the same id returned for every
    /// spelling of one supplier (parent story AC-2 — see <see cref="SupplierTestNames"/>). Records
    /// every raw name it was handed, so a test can assert the pipeline passed the legal name through
    /// verbatim rather than a pre-mangled one. This module may not reference the real implementation
    /// at all (ADR-002) — the SharedKernel port is the entire contract either side gets.</summary>
    private sealed class FakeSupplierResolver : ISupplierResolver
    {
        private readonly Dictionary<string, SupplierRef> _bySimplifiedName = new(StringComparer.Ordinal);

        public List<string> RawNamesSeen { get; } = [];

        public Task<Result<SupplierRef>> ResolveAsync(
            TenantId tenantId, string rawName, CancellationToken cancellationToken)
        {
            RawNamesSeen.Add(rawName);

            var key = SupplierTestNames.Simplify(rawName);
            if (!_bySimplifiedName.TryGetValue(key, out var existing))
            {
                existing = new SupplierRef(EntityId.New(), rawName.Trim());
                _bySimplifiedName[key] = existing;
            }

            return Task.FromResult(Result<SupplierRef>.Success(existing));
        }
    }

    private static Dictionary<string, string> PayloadsWithSupplierConfidence(double confidence) => new()
    {
        ["Metadata"] = $$"""
            {"facts":[
                {"field":"supplier","value":"{{SupplierLegalName}}","sourcePage":1,"sourceSpan":"between Salesforce, Inc. and Contoso Ltd","confidence":{{confidence}}},
                {"field":"currency","value":"USD","sourcePage":1,"confidence":0.95}
            ]}
            """,
    };

    private DocumentProcessingPipeline CreatePipeline(
        DocumentsContractsDbContext dbContext,
        ITenantContext tenantContext,
        IAiGateway gateway,
        ISupplierResolver? supplierResolver)
    {
        var clock = new FixedClock(Now);

        return new DocumentProcessingPipeline(
            dbContext,
            gateway,
            new HybridDocumentParsingService(gateway, new NativeDocumentTextExtractor()),
            new StagedExtractionService(dbContext, gateway, tenantContext, clock, new NoOpAuditWriter()),
            new EmbeddingRetrievalService(dbContext, gateway, tenantContext, clock),
            tenantContext,
            clock,
            // Task E13/F04/US01/T02 added the preview service as the pipeline's own optional
            // dependency ahead of this one; these tests exercise supplier resolution, not previews.
            previewService: null,
            supplierResolver);
    }

    private static async Task<Document> SeedDocumentAsync(
        DocumentsContractsDbContext db, TenantId tenantId, EntityId? contractId = null)
    {
        var document = new Document
        {
            TenantId = tenantId,
            FileName = "msa.pdf",
            MimeType = "application/pdf",
            StoragePath = $"{tenantId.Value:D}/documents/msa.pdf",
            Checksum = "test-checksum",
            ProcessingStatus = DocumentProcessingStatus.Uploaded,
            ContractId = contractId,
            CreatedAt = Now,
        };

        db.Documents.Add(document);
        await db.SaveChangesAsync();

        return document;
    }

    /// <summary>The pages+classification entry point (the one `POST /api/documents` uses after the
    /// admission gate has already parsed and classified) — this class is about what happens
    /// <em>after</em> extraction, so it never needs the parse/classify half.</summary>
    private static DocumentClassification Classification() =>
        new(ContractDocumentType.Msa, 0.95, new AiCallMetadata("test-classify-model", "1", "test-v1", Now, "hash"));

    private static IReadOnlyList<DocumentPageText> Pages() =>
        [new DocumentPageText(1, "MASTER SERVICES AGREEMENT between Salesforce, Inc. and Contoso Ltd.")];

    [Fact]
    public async Task An_accepted_supplier_fact_is_resolved_and_linked_onto_the_contract()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        await using var seedDb = CreateContext(tenantContext);
        var document = await SeedDocumentAsync(seedDb, tenantId);

        var gateway = new ScriptedExtractGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now)),
            PayloadsWithSupplierConfidence(0.98));
        var resolver = new FakeSupplierResolver();

        await using var runDb = CreateContext(tenantContext);
        var pipeline = CreatePipeline(runDb, tenantContext, gateway, resolver);

        var result = await pipeline.ProcessAsync(tenantId, document.Id, Pages(), Classification());

        Assert.True(result.IsSuccess);

        // The legal name reached the resolver exactly as the document wrote it — normalization is
        // the Suppliers module's job, and mangling it here would hide aliases from it.
        Assert.Equal(SupplierLegalName, Assert.Single(resolver.RawNamesSeen));

        await using var readDb = CreateContext(tenantContext);
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var contract = await readDb.Contracts.SingleAsync(c => c.Id == result.Value.ContractId);
        Assert.NotNull(contract.SupplierId);
        Assert.Equal(await ResolvedIdAsync(resolver, tenantId), contract.SupplierId);
    }

    [Fact]
    public async Task A_supplier_fact_below_the_critical_threshold_is_not_linked()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        await using var seedDb = CreateContext(tenantContext);
        var document = await SeedDocumentAsync(seedDb, tenantId);

        var gateway = new ScriptedExtractGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now)),
            PayloadsWithSupplierConfidence(0.64));
        var resolver = new FakeSupplierResolver();

        await using var runDb = CreateContext(tenantContext);
        var pipeline = CreatePipeline(runDb, tenantContext, gateway, resolver);

        var result = await pipeline.ProcessAsync(tenantId, document.Id, Pages(), Classification());

        Assert.True(result.IsSuccess);

        // Never resolved: creating a Supplier row off a fact a human has not confirmed would put a
        // guessed company into the tenant's supplier list (requirements R-SUP-03 sends it through
        // the review queue instead).
        Assert.Empty(resolver.RawNamesSeen);
        Assert.Equal(DocumentProcessingStatus.NeedsReview, result.Value.ProcessingStatus);

        await using var readDb = CreateContext(tenantContext);
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var contract = await readDb.Contracts.SingleAsync(c => c.Id == result.Value.ContractId);
        Assert.Null(contract.SupplierId);
    }

    /// <summary>
    /// Requirements R-SUP-03 ("a one-off job resolves suppliers for existing contracts"): because
    /// the link is made on every processing pass and re-processing re-runs extraction, a contract
    /// extracted before supplier identity existed picks its supplier up the next time it is
    /// processed — no bespoke back-fill job, and no schema migration. The first pass here stands in
    /// for "the old world" by running the identical pipeline with no resolver composed in, which is
    /// exactly what a host without the Suppliers module did.
    /// </summary>
    [Fact]
    public async Task Reprocessing_back_fills_the_supplier_of_a_contract_extracted_without_a_resolver()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        await using var seedDb = CreateContext(tenantContext);
        var document = await SeedDocumentAsync(seedDb, tenantId);

        var gateway = new ScriptedExtractGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now)),
            PayloadsWithSupplierConfidence(0.98));

        EntityId contractId;
        await using (var firstRunDb = CreateContext(tenantContext))
        {
            var pipelineWithoutSuppliers = CreatePipeline(firstRunDb, tenantContext, gateway, supplierResolver: null);
            var firstResult = await pipelineWithoutSuppliers.ProcessAsync(
                tenantId, document.Id, Pages(), Classification());

            Assert.True(firstResult.IsSuccess);
            contractId = firstResult.Value.ContractId;
        }

        await using (var afterFirstRunDb = CreateContext(tenantContext))
        {
            using var tenantScope = tenantContext.BeginScope(tenantId);
            Assert.Null((await afterFirstRunDb.Contracts.SingleAsync(c => c.Id == contractId)).SupplierId);
        }

        var resolver = new FakeSupplierResolver();
        await using (var secondRunDb = CreateContext(tenantContext))
        {
            var pipelineWithSuppliers = CreatePipeline(secondRunDb, tenantContext, gateway, resolver);
            var secondResult = await pipelineWithSuppliers.ProcessAsync(
                tenantId, document.Id, Pages(), Classification());

            Assert.True(secondResult.IsSuccess);

            // Re-processing stages into the *same* contract (Document.ContractId is already set),
            // so the back-fill lands on the existing row rather than creating a second one.
            Assert.Equal(contractId, secondResult.Value.ContractId);
        }

        await using var readDb = CreateContext(tenantContext);
        using var readScope = tenantContext.BeginScope(tenantId);

        var contract = await readDb.Contracts.SingleAsync(c => c.Id == contractId);
        Assert.Equal(await ResolvedIdAsync(resolver, tenantId), contract.SupplierId);
    }

    /// <summary>The id <paramref name="resolver"/> hands out for <see cref="SupplierLegalName"/> —
    /// asked of the fake itself rather than hard-coded, since it mints one on first sight (and
    /// returns the same one afterwards, which is the property being relied on).</summary>
    private static async Task<EntityId> ResolvedIdAsync(FakeSupplierResolver resolver, TenantId tenantId) =>
        (await resolver.ResolveAsync(tenantId, SupplierLegalName, CancellationToken.None)).Value.Id;
}
