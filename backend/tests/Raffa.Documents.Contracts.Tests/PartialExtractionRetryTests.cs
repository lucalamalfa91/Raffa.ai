using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Storage;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// F5-T02: a partial document (a stage failed) whose failure was transient is put back on the queue by
/// <see cref="HungProcessingRecoveryService.RetryPartialInTenantAsync"/>, bounded and paced; a
/// permanent failure, a failure inside the cool-down, one past the retry cap, and a legacy failure
/// without a kind are left for a human. Over EF InMemory, so without Docker.
/// </summary>
public sealed class PartialExtractionRetryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class NoopAudit : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopAborter : IExtractionRunAborter
    {
        public CancellationToken Register(Guid jobId, CancellationToken outer) => outer;

        public void Abort(Guid jobId)
        {
        }

        public void Unregister(Guid jobId)
        {
        }
    }

    private sealed class Storage : IDocumentStorage
    {
        public Task<string> SaveAsync(
            TenantId tenantId, EntityId documentId, int versionNumber, string fileName, Stream content,
            CancellationToken cancellationToken = default) => Task.FromResult($"{tenantId.Value}/{fileName}");

        public Task<byte[]?> LoadAsync(TenantId tenantId, string storagePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>([1, 2, 3]);

        public Task DeleteAsync(TenantId tenantId, string storagePath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<string> SavePreviewAsync(
            TenantId tenantId, EntityId documentId, Stream content, CancellationToken cancellationToken = default) =>
            Task.FromResult($"{tenantId.Value}/{documentId.Value}/preview.png");

        public Task<string> SavePreviewPageAsync(
            TenantId tenantId, EntityId documentId, int page, Stream content, CancellationToken cancellationToken = default) =>
            Task.FromResult($"{tenantId.Value}/{documentId.Value}/page-{page}.png");
    }

    private sealed class Harness
    {
        private readonly string _databaseName = Guid.NewGuid().ToString();

        public TenantId TenantId { get; } = TenantId.New();

        public TenantContext TenantContext { get; } = new();

        public InMemoryExtractionQueue Queue { get; } = new();

        public EntityId DocumentId { get; private set; }

        public DocumentsContractsDbContext CreateContext() => InMemoryDocumentsDb.Create(_databaseName);

        /// <summary>A NeedsReview document whose Risk stage failed <paramref name="failedAttempts"/> times, the last
        /// one <paramref name="failedAgo"/> ago, with the given kind.</summary>
        public async Task SeedPartialAsync(ExtractionStageFailureKind? kind, TimeSpan failedAgo, int failedAttempts = 1)
        {
            await using var db = CreateContext();
            var document = new Document
            {
                TenantId = TenantId,
                FileName = "partial.pdf",
                MimeType = "application/pdf",
                StoragePath = $"{TenantId.Value}/partial.pdf",
                Checksum = "checksum",
                CreatedAt = Now.AddDays(-1),
                ProcessingStatus = DocumentProcessingStatus.NeedsReview,
            };
            db.Documents.Add(document);
            db.ExtractionJobs.Add(new ExtractionJob
            {
                TenantId = TenantId, DocumentId = document.Id, Stage = ExtractionStage.Classification,
                Status = ExtractionJobStatus.Completed, QueuedAt = Now.AddDays(-1), CompletedAt = Now.AddDays(-1),
                AttemptCount = 1,
            });
            var runId = Guid.NewGuid();
            db.ExtractionJobs.Add(new ExtractionJob
            {
                TenantId = TenantId, DocumentId = document.Id, Stage = ExtractionStage.Metadata,
                Status = ExtractionJobStatus.Completed, QueuedAt = Now.AddHours(-30), StartedAt = Now.AddHours(-30),
                CompletedAt = Now.AddHours(-30), ExtractionRunId = runId,
            });
            for (var attempt = 0; attempt < failedAttempts; attempt++)
            {
                var queuedAt = Now - failedAgo - TimeSpan.FromHours(failedAttempts - 1 - attempt);
                db.ExtractionJobs.Add(new ExtractionJob
                {
                    TenantId = TenantId, DocumentId = document.Id, Stage = ExtractionStage.Risk,
                    Status = ExtractionJobStatus.Failed, QueuedAt = queuedAt, StartedAt = queuedAt,
                    CompletedAt = queuedAt, ExtractionRunId = runId, FailureKind = kind,
                    ErrorDetail = "AI provider unavailable: still failing after 3 retries.",
                });
            }

            await db.SaveChangesAsync();
            DocumentId = document.Id;
        }

        public async Task<HungProcessingRecoveryService> BuildRecoveryAsync()
        {
            var clock = new FixedClock();
            var db = CreateContext();
            var gateway = new FixtureAiGateway(new AiGatewayModelOptions(), clock);
            var reprocess = new DocumentReprocessService(
                db, new Storage(), Queue, Queue,
                new EmbeddingRetrievalService(db, gateway, TenantContext, clock),
                TenantContext, new NoopAudit(), clock);
            await Task.CompletedTask;
            return new HungProcessingRecoveryService(
                db, reprocess, new NoopAborter(), TenantContext, clock, NullLogger<HungProcessingRecoveryService>.Instance);
        }

        public async Task<DocumentProcessingStatus> StatusAsync()
        {
            await using var db = CreateContext();
            return (await db.Documents.SingleAsync()).ProcessingStatus;
        }
    }

    [Fact]
    public async Task A_transient_failure_past_the_cool_down_is_requeued_so_that_only_the_failed_stage_runs_again()
    {
        var harness = new Harness();
        await harness.SeedPartialAsync(ExtractionStageFailureKind.Transient, failedAgo: TimeSpan.FromMinutes(10));

        await (await harness.BuildRecoveryAsync()).RetryPartialInTenantAsync(harness.TenantId);

        var pointer = Assert.Single(harness.Queue.Published);
        Assert.Equal(harness.DocumentId.Value, pointer.DocumentId);
        Assert.Equal(DocumentProcessingStatus.Uploaded, await harness.StatusAsync());
    }

    [Fact]
    public async Task A_transient_failure_inside_the_cool_down_is_left_alone()
    {
        var harness = new Harness();
        await harness.SeedPartialAsync(ExtractionStageFailureKind.Transient, failedAgo: TimeSpan.FromMinutes(1));

        await (await harness.BuildRecoveryAsync()).RetryPartialInTenantAsync(harness.TenantId);

        Assert.Empty(harness.Queue.Published);
        Assert.Equal(DocumentProcessingStatus.NeedsReview, await harness.StatusAsync());
    }

    [Fact]
    public async Task A_permanent_failure_is_never_retried_automatically()
    {
        var harness = new Harness();
        await harness.SeedPartialAsync(ExtractionStageFailureKind.Permanent, failedAgo: TimeSpan.FromHours(2));

        await (await harness.BuildRecoveryAsync()).RetryPartialInTenantAsync(harness.TenantId);

        Assert.Empty(harness.Queue.Published);
    }

    [Fact]
    public async Task A_stage_that_already_failed_the_maximum_number_of_times_is_left_for_a_human()
    {
        var harness = new Harness();
        await harness.SeedPartialAsync(
            ExtractionStageFailureKind.Transient, failedAgo: TimeSpan.FromHours(2),
            failedAttempts: HungProcessingRecoveryService.MaxStageRetries);

        await (await harness.BuildRecoveryAsync()).RetryPartialInTenantAsync(harness.TenantId);

        Assert.Empty(harness.Queue.Published);
    }

    [Fact]
    public async Task A_legacy_failure_with_no_recorded_kind_is_not_retried()
    {
        var harness = new Harness();
        await harness.SeedPartialAsync(kind: null, failedAgo: TimeSpan.FromHours(2));

        await (await harness.BuildRecoveryAsync()).RetryPartialInTenantAsync(harness.TenantId);

        Assert.Empty(harness.Queue.Published);
    }

    [Fact]
    public async Task Another_tenants_partial_documents_are_not_touched()
    {
        var harness = new Harness();
        await harness.SeedPartialAsync(ExtractionStageFailureKind.Transient, failedAgo: TimeSpan.FromHours(2));

        await (await harness.BuildRecoveryAsync()).RetryPartialInTenantAsync(TenantId.New());

        Assert.Empty(harness.Queue.Published);
    }
}
