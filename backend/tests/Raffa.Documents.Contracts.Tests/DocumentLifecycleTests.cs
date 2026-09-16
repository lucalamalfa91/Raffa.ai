using System.Text;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Application.Admission;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Application.Preview;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Storage;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// Task E13/F04/US01/T02 (documents-v2-api): the V2 document lifecycle against a real
/// Postgres + pgvector with RLS in force — the list (R-DOC-06/09), reprocess (R-DOC-07) and
/// deletion (R-DOC-10). Uses the same app-role/Testcontainers setup as
/// <c>DocumentQueryServiceTests</c>, so "tenant-scoped" here means the database enforces it too,
/// not just a LINQ predicate.
/// </summary>
public sealed class DocumentLifecycleTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_lifecycle_app";
    private const string AppRolePassword = "raffa_lifecycle_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();
    private string _appConnectionString = string.Empty;

    private static readonly DateTimeOffset Now = new(2026, 9, 9, 9, 0, 0, TimeSpan.Zero);

    private const string Actor = "admin@acme.example";

    /// <summary>A born-digital contract with enough text to parse natively and classify as an MSA.</summary>
    private const string ContractText =
        "MASTER SERVICES AGREEMENT between Acme Corp and Contoso Ltd, effective 2026-01-01. " +
        "This Agreement governs all Order Forms executed by the parties. Annual fees are EUR 48,000, " +
        "payable within thirty days of invoice. The initial term is thirty-six months and renews " +
        "automatically unless either party gives ninety days written notice.";

    /// <summary>Readable, born-digital, and not a contract: the fixture classifies it `Other`, so
    /// the content gate refuses it (task E16/F02/US03/T01's split-gate proof below).</summary>
    private const string RecipeText =
        "Spaghetti alla carbonara for four. Boil 400 g of spaghetti in salted water. Meanwhile fry " +
        "150 g of guanciale until crisp. Whisk four egg yolks with 100 g of grated pecorino and " +
        "plenty of black pepper. Drain the pasta, toss with the guanciale off the heat, then fold in " +
        "the egg mixture until creamy. Serve immediately with extra pecorino.";

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());
        await using (var adminDb = new DocumentsContractsDbContext(adminOptions.Options))
        {
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

    [Fact]
    public async Task List_pages_filters_by_status_and_reports_page_count_and_weak_facts()
    {
        var tenantId = TenantId.New();
        var otherTenant = TenantId.New();
        var tenantContext = new TenantContext();
        var harness = CreateHarness(tenantContext);

        // Three documents for this tenant, one for another — the fourth must never appear.
        var first = await UploadAndProcessAsync(harness, tenantContext, tenantId, "msa-one.pdf", Now);
        var second = await UploadAndProcessAsync(harness, tenantContext, tenantId, "msa-two.pdf", Now.AddMinutes(1));
        var third = await UploadAndProcessAsync(harness, tenantContext, tenantId, "msa-three.pdf", Now.AddMinutes(2));
        await UploadAndProcessAsync(harness, tenantContext, otherTenant, "not-yours.pdf", Now.AddMinutes(3));

        await using (var db = CreateAppContext(tenantContext))
        {
            var queryService = new DocumentQueryService(db, tenantContext);

            var page1 = await queryService.ListAsync(tenantId, status: null, page: 1, pageSize: 2);
            Assert.Equal(3, page1.TotalCount);
            Assert.Equal(2, page1.Items.Count);
            Assert.Equal(1, page1.Page);
            Assert.Equal(2, page1.PageSize);

            // Newest first.
            Assert.Equal(third, page1.Items[0].DocumentId);
            Assert.Equal(second, page1.Items[1].DocumentId);

            var page2 = await queryService.ListAsync(tenantId, status: null, page: 2, pageSize: 2);
            Assert.Equal(first, Assert.Single(page2.Items).DocumentId);

            var row = page1.Items[0];
            Assert.Equal("msa-three.pdf", row.FileName);
            Assert.Equal(ContractDocumentType.Msa, row.DocumentType);
            Assert.Equal(1, row.PageCount);
            Assert.NotNull(row.ContractId);
            Assert.Null(row.SupplierName); // no ISupplierNameLookup registered here
            Assert.Null(row.Stage);        // terminal status: no stage is reported
            Assert.True(row.WeakFactCount >= 0);

            // Status filter: the fixture gateway extracts nothing, so every document lands in the
            // same terminal status — asserting on that status is enough to prove the filter binds.
            var terminalStatus = row.ProcessingStatus;
            var filtered = await queryService.ListAsync(tenantId, terminalStatus, page: 1, pageSize: 25);
            Assert.Equal(3, filtered.TotalCount);

            var otherStatus = terminalStatus == DocumentProcessingStatus.Failed
                ? DocumentProcessingStatus.Completed
                : DocumentProcessingStatus.Failed;
            var empty = await queryService.ListAsync(tenantId, otherStatus, page: 1, pageSize: 25);
            Assert.Empty(empty.Items);
            Assert.Equal(0, empty.TotalCount);
        }
    }

    [Fact]
    public async Task List_counts_one_weak_fact_per_field_and_resolves_the_supplier_name_when_a_lookup_is_registered()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var harness = CreateHarness(tenantContext);
        var documentId = await UploadAndProcessAsync(harness, tenantContext, tenantId, "msa.pdf", Now);

        EntityId contractId;
        var supplierId = EntityId.New();

        // The fixture gateway reads real facts out of ContractText (FixtureContractFactExtractor),
        // so the pipeline has already written evidence rows for this contract -- including a weak,
        // unlabelled `supplier` proposal. Measure that baseline first; the assertion below is about
        // what the seeded rows add on top of it, not about the fixture's own reading of the text.
        int baselineWeakFactCount;
        await using (var db = CreateAppContext(tenantContext))
        {
            var queryService = new DocumentQueryService(db, tenantContext);
            baselineWeakFactCount = Assert.Single((await queryService.ListAsync(tenantId)).Items).WeakFactCount;
        }

        await using (var db = CreateAppContext(tenantContext))
        {
            using var scope = tenantContext.BeginScope(tenantId);
            var document = await db.Documents.SingleAsync(d => d.TenantId == tenantId && d.Id == documentId);
            contractId = document.ContractId!.Value;

            var contract = await db.Contracts.SingleAsync(c => c.TenantId == tenantId && c.Id == contractId);
            contract.SupplierId = supplierId;

            // Seeded strictly *after* every row the pipeline wrote (the harness clock is Now), so the
            // "latest row per field wins" rule is exercised deterministically, never on a timestamp tie.
            // Two rows for the same field: only the latest one counts, and it is strong -- annualSpend
            // was already strong in the baseline, so it adds nothing.
            db.ExtractionEvidences.Add(Evidence(tenantId, contractId, documentId, "annualSpend", 0.2, Now.AddMinutes(2)));
            db.ExtractionEvidences.Add(Evidence(tenantId, contractId, documentId, "annualSpend", 0.95, Now.AddMinutes(3)));
            // One weak field and one field with no confidence at all: both turn a strong baseline
            // field weak, so both count. currency and autoRenewal are explicit high-confidence
            // facts in ContractText; the derived dates already sit below the 90 % bar.
            db.ExtractionEvidences.Add(Evidence(tenantId, contractId, documentId, "currency", 0.35, Now.AddMinutes(2)));
            db.ExtractionEvidences.Add(Evidence(tenantId, contractId, documentId, "autoRenewal", null, Now.AddMinutes(2)));
            await db.SaveChangesAsync();
        }

        await using (var db = CreateAppContext(tenantContext))
        {
            var lookup = new StubSupplierNameLookup(supplierId, "Contoso Ltd");
            var queryService = new DocumentQueryService(db, tenantContext, lookup);

            var page = await queryService.ListAsync(tenantId);
            var row = Assert.Single(page.Items);

            Assert.Equal(baselineWeakFactCount + 2, row.WeakFactCount);
            Assert.Equal("Contoso Ltd", row.SupplierName);
        }
    }

    [Fact]
    public async Task Reprocess_replaces_unreadable_chunks_with_page_aware_ones()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var harness = CreateHarness(tenantContext);
        var documentId = await UploadAndProcessAsync(harness, tenantContext, tenantId, "msa.pdf", Now);

        // Simulate a V1-era index: raw PDF bytes as chunk text, no page, no section.
        await using (var db = CreateAppContext(tenantContext))
        {
            using var scope = tenantContext.BeginScope(tenantId);
            var stale = await db.Embeddings
                .Where(e => e.TenantId == tenantId && e.SourceId == documentId)
                .ToListAsync();
            foreach (var chunk in stale)
            {
                chunk.ChunkText = "%PDF-1.4  raw bytes nobody can read";
                chunk.Page = null;
                chunk.Section = null;
            }

            await db.SaveChangesAsync();
        }

        // Task E16/F02/US03/T01: the request queues; the Worker re-runs. Both halves are proven
        // here -- the queued contract first, then the pipeline played by hand as the Worker.
        var queue = new InMemoryExtractionQueue();
        Result<DocumentReprocessQueued>? result;
        await using (var db = CreateAppContext(tenantContext))
        {
            var service = CreateReprocessService(db, harness, tenantContext, queue);
            result = await service.ReprocessAsync(tenantId, documentId, Actor);
        }

        Assert.NotNull(result);
        Assert.True(result!.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(documentId, result.Value.DocumentId);

        // One pointer, at the job the row says is queued and unclaimed -- the state a fresh claim
        // requires (claimed_at IS NULL is the compare-and-swap).
        var published = Assert.Single(queue.Published);
        Assert.Equal(documentId.Value, published.DocumentId);
        Assert.Equal(result.Value.ExtractionJobId.Value, published.ExtractionJobId);
        await using (var db = CreateAppContext(tenantContext))
        {
            using var scope = tenantContext.BeginScope(tenantId);
            var job = await db.ExtractionJobs.SingleAsync(j => j.Id == result.Value.ExtractionJobId);
            Assert.Equal(ExtractionJobStatus.Queued, job.Status);
            Assert.Null(job.ClaimedAt);
            Assert.Null(job.ClaimedBy);
            Assert.Equal(
                DocumentProcessingStatus.Uploaded,
                (await db.Documents.SingleAsync(d => d.Id == documentId)).ProcessingStatus);
            // R-DOC-07 AC-1: the stale chunks are gone in the request, not at the Worker's leisure.
            Assert.Empty(await db.Embeddings.Where(e => e.TenantId == tenantId && e.SourceId == documentId).ToListAsync());
        }

        Assert.Contains(
            harness.Audit.Entries,
            e => e.Action == DocumentReprocessService.ReprocessedAuditAction && e.Actor == Actor
                && e.Detail != null && e.Detail.Contains("queued", StringComparison.Ordinal));

        // The Worker's half: the same pipeline a first upload goes through.
        await using (var db = CreateAppContext(tenantContext))
        {
            var pipeline = CreatePipeline(db, harness, tenantContext);
            var rerun = await pipeline.ProcessAsync(tenantId, documentId, "msa.pdf", "application/pdf", BuildPdf(ContractText));
            Assert.True(rerun.IsSuccess, rerun.IsFailure ? rerun.Error : string.Empty);
            Assert.Equal(1, rerun.Value.PagesParsed);
            Assert.Equal(1, rerun.Value.ChunksIndexed);
        }

        await using (var db = CreateAppContext(tenantContext))
        {
            using var scope = tenantContext.BeginScope(tenantId);
            var chunks = await db.Embeddings
                .Where(e => e.TenantId == tenantId && e.SourceId == documentId)
                .ToListAsync();

            // R-DOC-07 AC-1 / AC-2.
            var chunk = Assert.Single(chunks);
            Assert.DoesNotContain("%PDF", chunk.ChunkText, StringComparison.Ordinal);
            Assert.Contains("MASTER SERVICES AGREEMENT", chunk.ChunkText, StringComparison.Ordinal);
            Assert.Equal(1, chunk.Page);
        }
    }

    [Fact]
    public async Task Reprocess_returns_null_for_another_tenants_document()
    {
        var tenantId = TenantId.New();
        var otherTenant = TenantId.New();
        var tenantContext = new TenantContext();
        var harness = CreateHarness(tenantContext);
        var documentId = await UploadAndProcessAsync(harness, tenantContext, tenantId, "msa.pdf", Now);

        await using var db = CreateAppContext(tenantContext);
        var service = CreateReprocessService(db, harness, tenantContext, new InMemoryExtractionQueue());

        Assert.Null(await service.ReprocessAsync(otherTenant, documentId, Actor));
    }

    [Fact]
    public async Task Reprocess_resubmits_a_dead_lettered_pointer_instead_of_publishing_a_second_copy()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var harness = CreateHarness(tenantContext);
        var documentId = await UploadAndProcessAsync(harness, tenantContext, tenantId, "msa.pdf", Now);

        var queue = new InMemoryExtractionQueue();
        var deadLetter = new StubDeadLetterResubmitter(resubmitted: true);
        await using (var db = CreateAppContext(tenantContext))
        {
            var service = CreateReprocessService(db, harness, tenantContext, queue, deadLetter);
            var result = await service.ReprocessAsync(tenantId, documentId, Actor);
            Assert.NotNull(result);
            Assert.True(result!.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        }

        Assert.Empty(queue.Published);
        Assert.NotNull(deadLetter.LastPointer);
        Assert.Equal(documentId.Value, deadLetter.LastPointer!.DocumentId);
        Assert.Contains(
            harness.Audit.Entries,
            e => e.Action == DocumentReprocessService.ReprocessedAuditAction
                && e.Detail != null && e.Detail.Contains("deadLetter", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Delete_removes_objects_rows_and_chunks_and_leaves_the_contract_standing()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var harness = CreateHarness(tenantContext);
        var documentId = await UploadAndProcessAsync(harness, tenantContext, tenantId, "msa.pdf", Now);

        EntityId contractId;
        await using (var db = CreateAppContext(tenantContext))
        {
            using var scope = tenantContext.BeginScope(tenantId);
            var document = await db.Documents.SingleAsync(d => d.TenantId == tenantId && d.Id == documentId);
            contractId = document.ContractId!.Value;
            Assert.NotNull(document.PreviewPath);
        }

        Result<DocumentDeleteResult>? result;
        await using (var db = CreateAppContext(tenantContext))
        {
            var service = new DocumentDeleteService(
                db,
                harness.Storage,
                new EmbeddingRetrievalService(db, harness.Gateway, tenantContext, harness.Clock),
                tenantContext,
                harness.Audit,
                harness.Clock);
            result = await service.DeleteAsync(tenantId, documentId, Actor);
        }

        Assert.NotNull(result);
        Assert.True(result!.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(1, result.Value.ContractsDetached);
        Assert.True(result.Value.ChunksRemoved >= 1);
        Assert.Contains(harness.Storage.Deleted, path => path.EndsWith("/preview/page-1.png", StringComparison.Ordinal));
        Assert.Contains(harness.Storage.Deleted, path => path.EndsWith("msa.pdf", StringComparison.Ordinal));
        Assert.Contains(
            harness.Audit.Entries,
            e => e.Action == DocumentDeleteService.DeletedAuditAction && e.ResourceId == documentId.Value.ToString());

        await using (var db = CreateAppContext(tenantContext))
        {
            using var scope = tenantContext.BeginScope(tenantId);
            Assert.Empty(await db.Documents.Where(d => d.TenantId == tenantId && d.Id == documentId).ToListAsync());
            Assert.Empty(await db.DocumentVersions.Where(v => v.TenantId == tenantId && v.DocumentId == documentId).ToListAsync());
            Assert.Empty(await db.ExtractionJobs.Where(j => j.TenantId == tenantId && j.DocumentId == documentId).ToListAsync());
            Assert.Empty(await db.Embeddings.Where(e => e.TenantId == tenantId && e.SourceId == documentId).ToListAsync());

            // The contract itself is untouched: a document was removed, not a business object.
            Assert.NotNull(await db.Contracts.SingleOrDefaultAsync(c => c.TenantId == tenantId && c.Id == contractId));
        }
    }

    [Fact]
    public async Task Delete_returns_null_for_another_tenants_document()
    {
        var tenantId = TenantId.New();
        var otherTenant = TenantId.New();
        var tenantContext = new TenantContext();
        var harness = CreateHarness(tenantContext);
        var documentId = await UploadAndProcessAsync(harness, tenantContext, tenantId, "msa.pdf", Now);

        await using var db = CreateAppContext(tenantContext);
        var service = new DocumentDeleteService(
            db,
            harness.Storage,
            new EmbeddingRetrievalService(db, harness.Gateway, tenantContext, harness.Clock),
            tenantContext,
            harness.Audit,
            harness.Clock);

        Assert.Null(await service.DeleteAsync(otherTenant, documentId, Actor));
        Assert.Empty(harness.Storage.Deleted);
    }

    [Fact]
    public async Task Preview_is_rendered_at_upload_and_readable_back_only_by_the_owning_tenant()
    {
        var tenantId = TenantId.New();
        var otherTenant = TenantId.New();
        var tenantContext = new TenantContext();
        var harness = CreateHarness(tenantContext);
        var documentId = await UploadAndProcessAsync(harness, tenantContext, tenantId, "msa.pdf", Now);

        await using var db = CreateAppContext(tenantContext);
        var previewService = new DocumentPreviewService(
            db, harness.Storage, new PlaceholderDocumentPreviewRenderer(), tenantContext);

        var png = await previewService.LoadAsync(tenantId, documentId);
        Assert.NotNull(png);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png![..4]);

        Assert.Null(await previewService.LoadAsync(otherTenant, documentId));
        Assert.Null(await previewService.LoadAsync(tenantId, EntityId.New()));
    }

    /// <summary>
    /// Task E16/F02/US03/T01 (ADR-027 §D1/§D3/§D6): the split gate, proven on the real Postgres
    /// path. The request half — <see cref="DocumentUploadService"/> — stores the bytes, writes the
    /// row and the queued job and publishes one pointer; it calls no model and never sees the
    /// content gate. The content half — parse/OCR, the readable-text floor, <c>classify</c>, the
    /// threshold — is reachable only from the Worker path (<see cref="ExtractionRequestedHandler"/>),
    /// where a refusal becomes a <see cref="DocumentProcessingStatus.Rejected"/> row with its reason
    /// code, the blob deleted and the job completed. The real <see cref="ExtractionJobClaimStore"/>
    /// does the compare-and-swap here, so the claim — and the redelivery no-op — are proven on
    /// Postgres too, not on the InMemory stand-in the API host uses.
    /// </summary>
    [Fact]
    public async Task Upload_stores_and_queues_without_a_model_call_and_the_content_gate_runs_only_on_the_worker_path()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var gateway = new CountingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now), new AiGatewayOcrOptions()));
        var harness = new Harness(new RecordingDocumentStorage(), gateway, new FixedClock(Now), new RecordingAuditWriter());
        var queue = new InMemoryExtractionQueue();
        var bytes = BuildPdf(RecipeText);

        // The request half: durable bytes, a row, a queued job, one pointer -- and no model call.
        EntityId documentId;
        await using (var db = CreateAppContext(tenantContext))
        {
            var uploadService = new DocumentUploadService(
                db, harness.Storage, queue, tenantContext, harness.Clock, harness.Audit);
            using var content = new MemoryStream(bytes);
            var upload = await uploadService.UploadAsync(tenantId, "carbonara.pdf", "application/pdf", content, Actor);
            Assert.True(upload.IsSuccess, upload.IsFailure ? upload.Error : string.Empty);
            documentId = upload.Value.DocumentId;
        }

        Assert.Equal(0, gateway.OcrCalls);
        Assert.Equal(0, gateway.ClassifyCalls);
        Assert.Single(harness.Storage.Saved);
        Assert.DoesNotContain(harness.Audit.Entries, e => e.Action == DocumentAdmissionGate.RejectedAuditAction);
        var message = Assert.Single(queue.Published);
        Assert.Equal(tenantId.Value, message.TenantId);
        Assert.Equal(documentId.Value, message.DocumentId);
        var jobId = new EntityId(message.ExtractionJobId);

        await using (var db = CreateAppContext(tenantContext))
        {
            using var scope = tenantContext.BeginScope(tenantId);
            var document = await db.Documents.SingleAsync(d => d.Id == documentId);
            Assert.Equal(DocumentProcessingStatus.Uploaded, document.ProcessingStatus);
            Assert.Null(document.RejectionReason);
            var job = await db.ExtractionJobs.SingleAsync(j => j.Id == jobId);
            Assert.Equal(ExtractionJobStatus.Queued, job.Status);
            Assert.Null(job.ClaimedAt);
        }

        // The Worker half: the claim on Postgres, then the content gate -- OCR once, classify once
        // -- and the refusal written as a row, never thrown at a caller that has already gone.
        await using (var db = CreateAppContext(tenantContext))
        {
            await CreateHandler(db, harness, tenantContext).HandleAsync(message);
        }

        Assert.Equal(1, gateway.OcrCalls);
        Assert.Equal(1, gateway.ClassifyCalls);
        Assert.Equal(0, gateway.ExtractCalls);

        await using (var db = CreateAppContext(tenantContext))
        {
            using var scope = tenantContext.BeginScope(tenantId);
            var document = await db.Documents.SingleAsync(d => d.Id == documentId);
            Assert.Equal(DocumentProcessingStatus.Rejected, document.ProcessingStatus);
            Assert.Equal(AdmissionRejectionReason.NotAContract, document.RejectionReason);
            Assert.Equal(ContractDocumentType.Other, document.RejectionDetectedType);
            var job = await db.ExtractionJobs.SingleAsync(j => j.Id == jobId);
            Assert.Equal(ExtractionJobStatus.Completed, job.Status);
            Assert.NotNull(job.ClaimedAt);
            Assert.Equal(1, job.AttemptCount);
            Assert.Empty(await db.Embeddings.Where(e => e.TenantId == tenantId && e.SourceId == documentId).ToListAsync());
        }

        Assert.Contains(harness.Storage.Deleted, path => path.EndsWith("carbonara.pdf", StringComparison.Ordinal));
        var audit = Assert.Single(harness.Audit.Entries, e => e.Action == DocumentAdmissionGate.RejectedAuditAction);
        Assert.Equal(ExtractionRequestedHandler.WorkerActor, audit.Actor);

        // At-least-once delivery: the same pointer again is a no-op -- the claim is spent and the
        // row is terminal, so nothing is parsed, classified or rewritten.
        await using (var db = CreateAppContext(tenantContext))
        {
            await CreateHandler(db, harness, tenantContext).HandleAsync(message);
        }

        Assert.Equal(1, gateway.OcrCalls);
        Assert.Equal(1, gateway.ClassifyCalls);
        Assert.Single(harness.Audit.Entries, e => e.Action == DocumentAdmissionGate.RejectedAuditAction);
    }

    // ----- harness -----

    /// <summary>The Worker's handler over the real claim store (raw SQL on the app role), the real
    /// content gate and the same pipeline <see cref="CreatePipeline"/> plays for a first upload.</summary>
    private static ExtractionRequestedHandler CreateHandler(
        DocumentsContractsDbContext db, Harness harness, ITenantContext tenantContext) =>
        new(
            db,
            harness.Storage,
            new DocumentAdmissionGate(
                new HybridDocumentParsingService(harness.Gateway, new NativeDocumentTextExtractor()),
                harness.Gateway,
                new DocumentAdmissionOptions(),
                tenantContext,
                harness.Audit,
                harness.Clock),
            CreatePipeline(db, harness, tenantContext),
            new ExtractionJobClaimStore(db, harness.Clock),
            tenantContext,
            harness.Clock,
            NullLogger<ExtractionRequestedHandler>.Instance);

    private DocumentsContractsDbContext CreateAppContext(ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(optionsBuilder, _appConnectionString, tenantContext);
        return new DocumentsContractsDbContext(optionsBuilder.Options);
    }

    private static Harness CreateHarness(ITenantContext tenantContext) => new(
        new RecordingDocumentStorage(),
        new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now), new AiGatewayOcrOptions()),
        new FixedClock(Now),
        new RecordingAuditWriter());

    private async Task<EntityId> UploadAndProcessAsync(
        Harness harness, ITenantContext tenantContext, TenantId tenantId, string fileName, DateTimeOffset now)
    {
        var bytes = BuildPdf(ContractText);

        EntityId documentId;
        await using (var db = CreateAppContext(tenantContext))
        {
            var uploadService = new DocumentUploadService(
                db, harness.Storage, new NoOpExtractionQueuePublisher(), tenantContext, new FixedClock(now),
                new NoOpAuditWriter());
            using var content = new MemoryStream(bytes);
            var upload = await uploadService.UploadAsync(tenantId, fileName, "application/pdf", content, Actor);
            Assert.True(upload.IsSuccess, upload.IsFailure ? upload.Error : string.Empty);
            documentId = upload.Value.DocumentId;
        }

        await using (var db = CreateAppContext(tenantContext))
        {
            var pipeline = CreatePipeline(db, harness, tenantContext);
            var result = await pipeline.ProcessAsync(tenantId, documentId, fileName, "application/pdf", bytes);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        }

        return documentId;
    }

    private static DocumentProcessingPipeline CreatePipeline(
        DocumentsContractsDbContext db, Harness harness, ITenantContext tenantContext) =>
        new(
            db,
            harness.Gateway,
            new HybridDocumentParsingService(harness.Gateway, new NativeDocumentTextExtractor()),
            new StagedExtractionService(db, harness.Gateway, tenantContext, harness.Clock, new NoOpAuditWriter()),
            new EmbeddingRetrievalService(db, harness.Gateway, tenantContext, harness.Clock),
            tenantContext,
            harness.Clock,
            new DocumentPreviewService(db, harness.Storage, new PlaceholderDocumentPreviewRenderer(), tenantContext));

    /// <summary>Task E16/F02/US03/T01: the service no longer owns a pipeline — it publishes to
    /// the queue. The test passes its own <see cref="InMemoryExtractionQueue"/> so it can assert
    /// the pointer, then plays the Worker itself (<see cref="CreatePipeline"/>), exactly as
    /// <see cref="UploadAndProcessAsync"/> already does for a first upload.</summary>
    private static DocumentReprocessService CreateReprocessService(
        DocumentsContractsDbContext db,
        Harness harness,
        ITenantContext tenantContext,
        InMemoryExtractionQueue queue,
        IExtractionDeadLetterResubmitter? deadLetter = null) =>
        new(
            db,
            harness.Storage,
            queue,
            deadLetter ?? queue,
            new EmbeddingRetrievalService(db, harness.Gateway, tenantContext, harness.Clock),
            tenantContext,
            harness.Audit,
            harness.Clock);

    private static ExtractionEvidence Evidence(
        TenantId tenantId, EntityId contractId, EntityId documentId, string fieldName, double? confidence, DateTimeOffset createdAt) =>
        new()
        {
            TenantId = tenantId,
            ContractId = contractId,
            SourceDocumentId = documentId,
            FieldName = fieldName,
            Value = "value",
            Confidence = confidence,
            CreatedAt = createdAt,
        };

    /// <summary>Same hand-built PDF shape the other tests use: one page object, one text stream.</summary>
    private static byte[] BuildPdf(string text) => Encoding.Latin1.GetBytes(
        "%PDF-1.4\n" +
        "1 0 obj << /Type /Page >> endobj\n" +
        "2 0 obj << /Length 0 >>\n" +
        "stream\n" +
        $"BT ({text}) Tj ET\n" +
        "endstream\n" +
        "endobj\n" +
        "%%EOF\n");

    private sealed record Harness(
        RecordingDocumentStorage Storage, IAiGateway Gateway, IClock Clock, RecordingAuditWriter Audit);

    /// <summary>Counts each role's calls on the way to the real fixture gateway, so "no model call
    /// on the request path" and "classified once on the Worker path" are facts, not inference.</summary>
    private sealed class CountingAiGateway(IAiGateway inner) : IAiGateway
    {
        public int ClassifyCalls { get; private set; }
        public int ExtractCalls { get; private set; }
        public int OcrCalls { get; private set; }

        public Task<Result<AiClassificationResult>> ClassifyAsync(
            AiClassificationRequest request, CancellationToken cancellationToken = default)
        {
            ClassifyCalls++;
            return inner.ClassifyAsync(request, cancellationToken);
        }

        public Task<Result<AiExtractionResult>> ExtractAsync(
            AiExtractionRequest request, CancellationToken cancellationToken = default)
        {
            ExtractCalls++;
            return inner.ExtractAsync(request, cancellationToken);
        }

        public Task<Result<AiEmbeddingResult>> EmbedAsync(
            AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            inner.EmbedAsync(request, cancellationToken);

        public Task<Result<AiAnswerResult>> AnswerAsync(
            AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            inner.AnswerAsync(request, cancellationToken);

        public Task<Result<AiOcrResult>> OcrAsync(
            AiOcrRequest request, CancellationToken cancellationToken = default)
        {
            OcrCalls++;
            return inner.OcrAsync(request, cancellationToken);
        }
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class NoOpAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Task E16/F02/US02/T01 (durable-queue-transport): a no-op stand-in for the port
    /// <see cref="DocumentUploadService"/> now publishes through before it commits — out of scope
    /// for this suite's own assertions (queue transport is <c>Raffa.Worker.Tests</c>' scope).
    /// </summary>
    private sealed class NoOpExtractionQueuePublisher : IExtractionQueuePublisher
    {
        public Task PublishAsync(ExtractionRequested message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubDeadLetterResubmitter(bool resubmitted) : IExtractionDeadLetterResubmitter
    {
        public ExtractionRequested? LastPointer { get; private set; }

        public Task<bool> TryResubmitAsync(ExtractionRequested pointer, CancellationToken cancellationToken = default)
        {
            LastPointer = pointer;
            return Task.FromResult(resubmitted);
        }
    }

    private sealed class StubSupplierNameLookup(EntityId supplierId, string name) : ISupplierNameLookup
    {
        public Task<IReadOnlyDictionary<EntityId, string>> GetNamesAsync(
            TenantId tenantId, IReadOnlyCollection<EntityId> supplierIds, CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<EntityId, string> names = supplierIds.Contains(supplierId)
                ? new Dictionary<EntityId, string> { [supplierId] = name }
                : new Dictionary<EntityId, string>();
            return Task.FromResult(names);
        }

        // Task E19/F04/US01/T01's own read-only port -- this suite never exercises a name-to-id
        // resolution, only the id-to-name direction GetNamesAsync above already covers.
        public Task<EntityId?> FindByNormalizedNameAsync(
            TenantId tenantId, string normalizedName, CancellationToken cancellationToken) =>
            Task.FromResult<EntityId?>(null);
    }

    private sealed class RecordingDocumentStorage : IDocumentStorage
    {
        public List<(string Path, byte[] Content)> Saved { get; } = [];

        public List<string> Deleted { get; } = [];

        private readonly Dictionary<string, byte[]> _objects = new(StringComparer.Ordinal);

        public async Task<string> SaveAsync(
            TenantId tenantId,
            EntityId documentId,
            int versionNumber,
            string fileName,
            Stream content,
            CancellationToken cancellationToken = default) =>
            await StoreAsync(
                DocumentStoragePath.Build(tenantId, documentId, versionNumber, fileName), content, cancellationToken);

        public async Task<string> SavePreviewAsync(
            TenantId tenantId, EntityId documentId, Stream content, CancellationToken cancellationToken = default) =>
            await StoreAsync(DocumentStoragePath.BuildPreview(tenantId, documentId), content, cancellationToken);

        public async Task<string> SavePreviewPageAsync(
            TenantId tenantId, EntityId documentId, int page, Stream content, CancellationToken cancellationToken = default) =>
            await StoreAsync(DocumentStoragePath.BuildPreviewPage(tenantId, documentId, page), content, cancellationToken);

        public Task<byte[]?> LoadAsync(
            TenantId tenantId, string storagePath, CancellationToken cancellationToken = default)
        {
            DocumentStoragePath.EnsureWithinTenant(tenantId, storagePath);
            return Task.FromResult(_objects.TryGetValue(storagePath, out var bytes) ? bytes : null);
        }

        public Task DeleteAsync(
            TenantId tenantId, string storagePath, CancellationToken cancellationToken = default)
        {
            DocumentStoragePath.EnsureWithinTenant(tenantId, storagePath);
            Deleted.Add(storagePath);
            _objects.Remove(storagePath);
            return Task.CompletedTask;
        }

        private async Task<string> StoreAsync(string path, Stream content, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();
            Saved.Add((path, bytes));
            _objects[path] = bytes;
            return path;
        }
    }
}
