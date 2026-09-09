using Contigo.Documents.Contracts.Application;
using Contigo.Documents.Contracts.Domain;
using Contigo.Documents.Contracts.Infrastructure;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Testcontainers.PostgreSql;

namespace Contigo.Documents.Contracts.Tests;

/// <summary>
/// <see cref="ContractEvidenceQueryService"/> — the read behind <c>GET /api/contracts/{id}/evidence</c>:
/// the latest evidence row per field, the file name and model behind it, and the sentence of the
/// indexed page text around the span with the span's own offsets — or no passage at all when the
/// span cannot be found, never an approximated one. Real Postgres, same Testcontainer pattern as
/// the other application-service tests in this project (the query joins four RLS-scoped tables).
/// </summary>
public sealed class ContractEvidenceQueryServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 15, 0, 0, TimeSpan.Zero);

    private const string PageOneText =
        "MASTER SERVICES AGREEMENT between Contigo Demo AG (\"Customer\") and Northwind Traders SA (\"Supplier\"), " +
        "effective 2026-01-01. The annual subscription fee is EUR 48,000, invoiced yearly in\nadvance. " +
        "All invoices are payable within thirty (30) days of receipt.";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

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

    private sealed record Seed(Contract Contract, Document Document, ExtractionJob Job);

    private static async Task<Seed> SeedContractWithEvidenceAsync(DocumentsContractsDbContext db, TenantId tenantId)
    {
        var contract = new Contract
        {
            TenantId = tenantId,
            Type = ContractDocumentType.Msa,
            Status = "active",
            Currency = "EUR",
            AnnualSpend = 48000m,
            CreatedAt = Now,
        };
        var document = new Document
        {
            TenantId = tenantId,
            ContractId = contract.Id,
            FileName = "northwind-msa.pdf",
            MimeType = "application/pdf",
            StoragePath = $"{tenantId.Value:D}/documents/northwind-msa.pdf",
            Checksum = "checksum",
            ProcessingStatus = DocumentProcessingStatus.NeedsReview,
            PageCount = 1,
            CreatedAt = Now,
        };
        var job = new ExtractionJob
        {
            TenantId = tenantId,
            DocumentId = document.Id,
            Stage = ExtractionStage.CommercialTerms,
            Status = ExtractionJobStatus.Completed,
            ModelId = "fixture-extract-model",
            QueuedAt = Now,
        };

        db.Contracts.Add(contract);
        db.Documents.Add(document);
        db.ExtractionJobs.Add(job);
        db.Embeddings.Add(new Embedding
        {
            TenantId = tenantId,
            SourceType = "Document",
            SourceId = document.Id,
            ChunkIndex = 0,
            Page = 1,
            ChunkText = PageOneText,
            Vector = new Vector(new float[Embedding.VectorDimensions]),
            Model = "fixture-embed-model",
            CreatedAt = Now,
        });

        // annualSpend was extracted twice (an original run, then a reprocess): the newer row wins.
        db.ExtractionEvidences.AddRange(
            new ExtractionEvidence
            {
                TenantId = tenantId,
                ContractId = contract.Id,
                SourceDocumentId = document.Id,
                ExtractionJobId = job.Id,
                FieldName = "annualSpend",
                Value = "47000",
                SourceSpan = "EUR 47,000",
                SourcePage = 1,
                Confidence = 0.4,
                CreatedAt = Now.AddHours(-1),
            },
            new ExtractionEvidence
            {
                TenantId = tenantId,
                ContractId = contract.Id,
                SourceDocumentId = document.Id,
                ExtractionJobId = job.Id,
                FieldName = "annualSpend",
                Value = "48000",
                // A line break inside the quoted span, the way a model reading a PDF often quotes it.
                SourceSpan = "EUR 48,000, invoiced yearly\nin advance",
                SourcePage = 1,
                Confidence = 0.96,
                CreatedAt = Now,
            },
            new ExtractionEvidence
            {
                TenantId = tenantId,
                ContractId = contract.Id,
                SourceDocumentId = document.Id,
                ExtractionJobId = null,
                FieldName = "type",
                Value = "Msa",
                SourceSpan = null,
                SourcePage = null,
                Confidence = 0.99,
                CreatedAt = Now,
            },
            new ExtractionEvidence
            {
                TenantId = tenantId,
                ContractId = contract.Id,
                SourceDocumentId = document.Id,
                ExtractionJobId = job.Id,
                FieldName = "paymentTerms",
                Value = "Net 30",
                SourceSpan = "this span is not on the page",
                SourcePage = 1,
                Confidence = 0.9,
                CreatedAt = Now,
            });

        await db.SaveChangesAsync();
        return new Seed(contract, document, job);
    }

    [Fact]
    public async Task Returns_the_latest_row_per_field_with_file_model_and_a_highlighted_passage()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        await using var seedDb = CreateContext(tenantContext);
        var seed = await SeedContractWithEvidenceAsync(seedDb, tenantId);

        await using var queryDb = CreateContext(tenantContext);
        var service = new ContractEvidenceQueryService(queryDb, tenantContext);

        var evidence = await service.GetLatestAsync(tenantId, seed.Contract.Id);

        Assert.NotNull(evidence);
        Assert.Equal(["annualSpend", "paymentTerms", "type"], evidence.Select(e => e.FieldName));

        var annualSpend = evidence.Single(e => e.FieldName == "annualSpend");
        Assert.Equal("48000", annualSpend.Value); // the newer of the two rows
        Assert.Equal(0.96, annualSpend.Confidence);
        Assert.Equal(1, annualSpend.SourcePage);
        Assert.Equal("northwind-msa.pdf", annualSpend.SourceFileName);
        Assert.Equal("fixture-extract-model", annualSpend.ModelId);
        Assert.Equal(seed.Document.Id, annualSpend.SourceDocumentId);

        // The passage is the sentence around the span; the offsets point at the span inside it,
        // whitespace-normalized on both sides so the line break in the quote still matches.
        Assert.NotNull(annualSpend.Passage);
        Assert.Equal("The annual subscription fee is EUR 48,000, invoiced yearly in advance.", annualSpend.Passage);
        Assert.Equal(
            "EUR 48,000, invoiced yearly in advance",
            annualSpend.Passage.Substring(annualSpend.HighlightStart!.Value, annualSpend.HighlightLength!.Value));

        // No span (a classification verdict): no passage, honestly.
        var type = evidence.Single(e => e.FieldName == "type");
        Assert.Equal("Msa", type.Value);
        Assert.Null(type.Passage);
        Assert.Null(type.HighlightStart);
        Assert.Null(type.ModelId);

        // A span the page text does not contain: the span is reported, the passage is not invented.
        var paymentTerms = evidence.Single(e => e.FieldName == "paymentTerms");
        Assert.Equal("this span is not on the page", paymentTerms.SourceSpan);
        Assert.Null(paymentTerms.Passage);
    }

    [Fact]
    public async Task A_contract_without_evidence_is_an_empty_list_and_an_unknown_or_foreign_contract_is_null()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        await using var seedDb = CreateContext(tenantContext);
        var bare = new Contract
        {
            TenantId = tenantId,
            Type = ContractDocumentType.Msa,
            Status = "processing",
            Currency = "USD",
            CreatedAt = Now,
        };
        seedDb.Contracts.Add(bare);
        await seedDb.SaveChangesAsync();
        var seeded = await SeedContractWithEvidenceAsync(seedDb, tenantId);

        await using var queryDb = CreateContext(tenantContext);
        var service = new ContractEvidenceQueryService(queryDb, tenantContext);

        var empty = await service.GetLatestAsync(tenantId, bare.Id);
        Assert.NotNull(empty);
        Assert.Empty(empty);

        Assert.Null(await service.GetLatestAsync(tenantId, EntityId.New()));
        Assert.Null(await service.GetLatestAsync(TenantId.New(), seeded.Contract.Id));
    }
}
