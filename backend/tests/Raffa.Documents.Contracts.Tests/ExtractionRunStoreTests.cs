using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Documents.Contracts.Infrastructure.Extraction;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// F5 PR-1 guards of <see cref="IExtractionRunStore"/>, driven directly over the EF InMemory provider:
/// the tenant scope is mandatory on every call (ADR-009), a run handle is bound to the store instance
/// (so the scoped DbContext) that opened it, and the store only accepts the facts of the stage it is
/// told to apply.
/// </summary>
public sealed class ExtractionRunStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Written { get; } = [];

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Written.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class Harness
    {
        private readonly string _databaseName = Guid.NewGuid().ToString();

        public TenantId TenantId { get; } = TenantId.New();

        public TenantContext TenantContext { get; } = new();

        public EntityId DocumentId { get; private set; }

        public DocumentsContractsDbContext CreateContext() => InMemoryDocumentsDb.Create(_databaseName);

        public ExtractionRunStore CreateStore(DocumentsContractsDbContext db) =>
            new(db, TenantContext, new FixedClock(Now), new RecordingAuditWriter());

        public ExtractionRunRequest Request(TenantId? tenantId = null) =>
            new(tenantId ?? TenantId, DocumentId, "hash-1", PageCount: 4, ClassificationConfidence: null);

        public async Task SeedDocumentAsync()
        {
            await using var db = CreateContext();
            var document = new Document
            {
                TenantId = TenantId,
                FileName = "contract.pdf",
                MimeType = "application/pdf",
                StoragePath = $"{TenantId.Value:D}/documents/contract.pdf",
                Checksum = "checksum-1",
                ProcessingStatus = DocumentProcessingStatus.Uploaded,
                CreatedAt = Now,
            };
            db.Documents.Add(document);
            await db.SaveChangesAsync();
            DocumentId = document.Id;
        }
    }

    [Fact]
    public async Task Open_without_a_tenant_scope_throws_and_writes_nothing()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();

        // A fresh TenantContext: no BeginScope was ever entered, Current is null.
        Assert.Null(harness.TenantContext.Current);

        await using (var db = harness.CreateContext())
        {
            var store = harness.CreateStore(db);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.OpenAsync(harness.Request(), CancellationToken.None));
        }

        await using var check = harness.CreateContext();
        Assert.Equal(0, await check.ExtractionJobs.CountAsync());
        Assert.Equal(0, await check.Contracts.CountAsync());
        Assert.Equal(DocumentProcessingStatus.Uploaded, (await check.Documents.SingleAsync()).ProcessingStatus);
    }

    [Fact]
    public async Task Open_inside_another_tenants_scope_throws()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();

        using (harness.TenantContext.BeginScope(TenantId.New()))
        {
            await using var db = harness.CreateContext();
            var store = harness.CreateStore(db);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.OpenAsync(harness.Request(), CancellationToken.None));
        }

        await using var check = harness.CreateContext();
        Assert.Equal(0, await check.ExtractionJobs.CountAsync());
        Assert.Equal(0, await check.Contracts.CountAsync());
    }

    [Fact]
    public async Task Every_call_after_open_refuses_to_run_outside_the_runs_tenant_scope()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();

        await using var db = harness.CreateContext();
        var store = harness.CreateStore(db);

        ExtractionRun run;
        using (harness.TenantContext.BeginScope(harness.TenantId))
        {
            var opened = await store.OpenAsync(harness.Request(), CancellationToken.None);
            Assert.True(opened.IsSuccess, opened.IsFailure ? opened.Error : null);
            run = opened.Value;
        }

        // The scope ended: the handle must not be usable without one.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.StartStagesAsync(run, run.StagesToRun, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CloseAsync(run, [], CancellationToken.None));
    }

    [Fact]
    public async Task A_run_handle_is_rejected_by_another_store_instance()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();

        await using var dbA = harness.CreateContext();
        await using var dbB = harness.CreateContext();
        var storeA = harness.CreateStore(dbA);
        var storeB = harness.CreateStore(dbB);

        using var scope = harness.TenantContext.BeginScope(harness.TenantId);

        var opened = await storeA.OpenAsync(harness.Request(), CancellationToken.None);
        Assert.True(opened.IsSuccess, opened.IsFailure ? opened.Error : null);
        var run = opened.Value;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => storeB.StartStagesAsync(run, run.StagesToRun, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => storeB.ApplyAsync(run, ExtractionStage.Metadata, new StageFacts.Scalars(ExtractionStage.Metadata, []), "model", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => storeB.FailAsync(run, ExtractionStage.Metadata, "boom", ExtractionStageFailureKind.Permanent, CancellationToken.None));

        await using var check = harness.CreateContext();
        Assert.Equal(0, await check.ExtractionJobs.CountAsync());
    }

    [Fact]
    public async Task Apply_rejects_facts_of_another_stage()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();

        await using var db = harness.CreateContext();
        var store = harness.CreateStore(db);
        using var scope = harness.TenantContext.BeginScope(harness.TenantId);

        var run = (await store.OpenAsync(harness.Request(), CancellationToken.None)).Value;
        await store.StartStagesAsync(run, run.StagesToRun, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.ApplyAsync(run, ExtractionStage.Metadata, new StageFacts.LineItems([]), "model", CancellationToken.None));

        // Nothing was saved for the stage: its job is still the Running one StartStagesAsync wrote.
        await using var check = harness.CreateContext();
        var job = await check.ExtractionJobs.SingleAsync(j => j.Stage == ExtractionStage.Metadata);
        Assert.Equal(ExtractionJobStatus.Running, job.Status);
        Assert.Null(job.CompletedAt);
        Assert.Null(job.ExtractedCount);
    }

    [Fact]
    public async Task Apply_with_an_empty_fact_list_completes_the_stage_with_zero_counts()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();

        await using var db = harness.CreateContext();
        var store = harness.CreateStore(db);
        using var scope = harness.TenantContext.BeginScope(harness.TenantId);

        var run = (await store.OpenAsync(harness.Request(), CancellationToken.None)).Value;
        await store.StartStagesAsync(run, run.StagesToRun, CancellationToken.None);

        var scalar = await store.ApplyAsync(
            run, ExtractionStage.Metadata, new StageFacts.Scalars(ExtractionStage.Metadata, []), "model-1", CancellationToken.None);
        var list = await store.ApplyAsync(
            run, ExtractionStage.LineItems, new StageFacts.LineItems([]), "model-1", CancellationToken.None);

        Assert.Equal(ExtractionJobStatus.Completed, scalar.Status);
        Assert.Equal(0, scalar.ExtractedCount);
        Assert.Equal(0, scalar.SkippedCount);
        Assert.Equal(ExtractionJobStatus.Completed, list.Status);
        Assert.Equal(0, list.ExtractedCount);
        Assert.Equal(0, list.SkippedCount);

        await using var check = harness.CreateContext();
        var job = await check.ExtractionJobs.SingleAsync(j => j.Stage == ExtractionStage.Metadata);
        Assert.Equal(ExtractionJobStatus.Completed, job.Status);
        Assert.Equal("model-1", job.ModelId);
        Assert.Equal(0, job.ExtractedCount);
        Assert.Equal(0, job.SkippedCount);
    }

    [Fact]
    public void Scalar_facts_refuse_a_list_stage()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StageFacts.Scalars(ExtractionStage.Risk, []));
    }
}
