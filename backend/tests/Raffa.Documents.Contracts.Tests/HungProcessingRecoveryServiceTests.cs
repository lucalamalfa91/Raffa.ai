using Raffa.AiGateway;
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
using Testcontainers.PostgreSql;

namespace Raffa.Documents.Contracts.Tests;

public sealed class HungProcessingRecoveryServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task A_fresh_processing_document_is_left_alone()
    {
        var harness = await SeedHungAsync(claimedAt: Now.AddMinutes(-1), attemptCount: 1);

        var action = await harness.Recovery.RecoverDocumentAsync(harness.TenantId, harness.DocumentId, force: false);

        Assert.Equal(HungRecoveryAction.None, action);
        Assert.Equal(DocumentProcessingStatus.Processing, await harness.ReloadStatusAsync());
        Assert.Empty(harness.Queue.Published);
        Assert.Empty(harness.Aborter.Aborted);
    }

    [Fact]
    public async Task A_processing_document_heartbeating_past_three_minutes_is_left_alone()
    {
        var harness = await SeedHungAsync(claimedAt: Now.AddMinutes(-4), attemptCount: 1);

        var action = await harness.Recovery.RecoverDocumentAsync(harness.TenantId, harness.DocumentId, force: false);

        Assert.Equal(HungRecoveryAction.None, action);
        Assert.Equal(DocumentProcessingStatus.Processing, await harness.ReloadStatusAsync());
        Assert.Empty(harness.Queue.Published);
        Assert.Empty(harness.Aborter.Aborted);
    }

    [Fact]
    public async Task A_hung_processing_document_is_aborted_and_requeued_from_scratch()
    {
        var harness = await SeedHungAsync(claimedAt: Now.AddMinutes(-15), attemptCount: 1);

        var action = await harness.Recovery.RecoverDocumentAsync(harness.TenantId, harness.DocumentId, force: false);

        Assert.Equal(HungRecoveryAction.Requeued, action);
        Assert.Equal(harness.JobId.Value, Assert.Single(harness.Aborter.Aborted));
        Assert.Equal(DocumentProcessingStatus.Uploaded, await harness.ReloadStatusAsync());
        var pointer = Assert.Single(harness.Queue.Published);
        Assert.Equal(harness.TenantId.Value, pointer.TenantId);
        Assert.Equal(harness.DocumentId.Value, pointer.DocumentId);

        await using var db = CreateContext();
        var job = await db.ExtractionJobs.SingleAsync(j => j.Id == harness.JobId);
        Assert.Equal(ExtractionJobStatus.Queued, job.Status);
        Assert.Null(job.ClaimedAt);
        Assert.Null(job.ClaimedBy);
        Assert.Equal(1, job.AttemptCount);
    }

    [Fact]
    public async Task A_hung_document_that_has_already_used_max_attempts_fails_terminally()
    {
        var harness = await SeedHungAsync(
            claimedAt: Now.AddMinutes(-16),
            attemptCount: ExtractionRequestedHandler.MaxAttempts);

        var action = await harness.Recovery.RecoverDocumentAsync(harness.TenantId, harness.DocumentId, force: false);

        Assert.Equal(HungRecoveryAction.Failed, action);
        Assert.Equal(DocumentProcessingStatus.Failed, await harness.ReloadStatusAsync());
        Assert.Empty(harness.Queue.Published);

        await using var db = CreateContext();
        var job = await db.ExtractionJobs.SingleAsync(j => j.Id == harness.JobId);
        Assert.Equal(ExtractionJobStatus.Failed, job.Status);
        Assert.Null(job.ClaimedAt);
        Assert.Contains(HungProcessingRecoveryService.GaveUpErrorPrefix, job.ErrorDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecoverHungInTenantAsync_does_not_touch_another_tenant()
    {
        var harness = await SeedHungAsync(claimedAt: Now.AddMinutes(-20), attemptCount: 1);
        var otherTenant = TenantId.New();

        await harness.Recovery.RecoverHungInTenantAsync(otherTenant);

        Assert.Equal(DocumentProcessingStatus.Processing, await harness.ReloadStatusAsync());
        Assert.Empty(harness.Queue.Published);
    }

    [Fact]
    public async Task Force_recovers_even_before_the_inactivity_window()
    {
        var harness = await SeedHungAsync(claimedAt: Now.AddSeconds(-5), attemptCount: 1);

        var action = await harness.Recovery.RecoverDocumentAsync(harness.TenantId, harness.DocumentId, force: true);

        Assert.Equal(HungRecoveryAction.Requeued, action);
        Assert.Equal(DocumentProcessingStatus.Uploaded, await harness.ReloadStatusAsync());
        Assert.Single(harness.Queue.Published);
    }

    [Theory]
    [InlineData("Gave up after 3 attempts. Processing made no progress for 3 minutes.", true)]
    [InlineData("Gave up after 3 attempts. Processing made no progress for 15 minutes.", false)]
    [InlineData("Gave up after 3 attempts. Last error: Malformed extraction payload.", false)]
    [InlineData("Schema validation failed", false)]
    [InlineData(null, false)]
    public void IsResurrectableHangCapFailure_is_only_the_old_shorter_hang_cap(
        string? errorDetail, bool expected)
    {
        Assert.Equal(expected, HungProcessingRecoveryService.IsResurrectableHangCapFailure(errorDetail));
    }

    public const string LiveClassifyOutageError =
        "Gave up after 3 attempts. Last error: The document could not be assessed: the 'classify' role could not be reached (InvalidOperationException: An exception has been raised that is likely due to a transient failure.) Nothing was stored.";

    [Theory]
    [InlineData(LiveClassifyOutageError, true)]
    [InlineData(
        "Gave up after 3 attempts. Classify stayed unreachable after in-call retries. Last error: The document could not be assessed: the 'classify' role could not be reached (InvalidOperationException: An exception has been raised that is likely due to a transient failure.) Nothing was stored.",
        false)]
    [InlineData("Gave up after 3 attempts. Last error: Malformed extraction payload.", false)]
    [InlineData(
        "The document could not be assessed: the 'classify' role could not be reached (InvalidOperationException: ManagedIdentityCredential authentication failed: no token endpoint.). Nothing was stored.",
        false)]
    [InlineData(null, false)]
    public void IsResurrectableClassifyOutageFailure_is_the_ef_transient_wrap_only(
        string? errorDetail, bool expected)
    {
        Assert.Equal(expected, HungProcessingRecoveryService.IsResurrectableClassifyOutageFailure(errorDetail));
    }

    [Fact]
    public async Task A_failed_document_from_the_old_three_minute_hang_cap_is_requeued_with_a_fresh_attempt_budget()
    {
        var harness = await SeedFailedAsync(
            "Gave up after 3 attempts. Processing made no progress for 3 minutes.",
            attemptCount: ExtractionRequestedHandler.MaxAttempts);

        var action = await harness.Recovery.RecoverDocumentAsync(harness.TenantId, harness.DocumentId, force: false);

        Assert.Equal(HungRecoveryAction.Requeued, action);
        Assert.Equal(DocumentProcessingStatus.Uploaded, await harness.ReloadStatusAsync());
        Assert.Empty(harness.Aborter.Aborted);
        var pointer = Assert.Single(harness.Queue.Published);
        Assert.Equal(harness.DocumentId.Value, pointer.DocumentId);

        await using var db = CreateContext();
        var job = await db.ExtractionJobs.SingleAsync(j => j.Id == harness.JobId);
        Assert.Equal(ExtractionJobStatus.Queued, job.Status);
        Assert.Equal(0, job.AttemptCount);
        Assert.Null(job.ErrorDetail);
        Assert.Null(job.ClaimedAt);
    }

    [Fact]
    public async Task A_failed_document_from_the_current_hang_cap_is_left_failed()
    {
        var harness = await SeedFailedAsync(
            "Gave up after 3 attempts. Processing made no progress for 15 minutes.",
            attemptCount: ExtractionRequestedHandler.MaxAttempts);

        var action = await harness.Recovery.RecoverDocumentAsync(harness.TenantId, harness.DocumentId, force: false);

        Assert.Equal(HungRecoveryAction.None, action);
        Assert.Equal(DocumentProcessingStatus.Failed, await harness.ReloadStatusAsync());
        Assert.Empty(harness.Queue.Published);

        await using var db = CreateContext();
        var job = await db.ExtractionJobs.SingleAsync(j => j.Id == harness.JobId);
        Assert.Equal(ExtractionJobStatus.Failed, job.Status);
        Assert.Equal(ExtractionRequestedHandler.MaxAttempts, job.AttemptCount);
    }

    [Fact]
    public async Task A_failed_document_from_the_classify_ef_transient_outage_is_requeued_with_a_fresh_attempt_budget()
    {
        var harness = await SeedFailedAsync(
            LiveClassifyOutageError,
            attemptCount: ExtractionRequestedHandler.MaxAttempts);

        var action = await harness.Recovery.RecoverDocumentAsync(harness.TenantId, harness.DocumentId, force: false);

        Assert.Equal(HungRecoveryAction.Requeued, action);
        Assert.Equal(DocumentProcessingStatus.Uploaded, await harness.ReloadStatusAsync());
        Assert.Single(harness.Queue.Published);

        await using var db = CreateContext();
        var job = await db.ExtractionJobs.SingleAsync(j => j.Id == harness.JobId);
        Assert.Equal(ExtractionJobStatus.Queued, job.Status);
        Assert.Equal(0, job.AttemptCount);
        Assert.Null(job.ErrorDetail);
    }

    [Fact]
    public async Task RecoverHungInTenantAsync_resurrects_stranded_classify_outage_failures()
    {
        var harness = await SeedFailedAsync(
            LiveClassifyOutageError,
            attemptCount: ExtractionRequestedHandler.MaxAttempts);

        await harness.Recovery.RecoverHungInTenantAsync(harness.TenantId);

        Assert.Equal(DocumentProcessingStatus.Uploaded, await harness.ReloadStatusAsync());
        Assert.Single(harness.Queue.Published);

        await using var db = CreateContext();
        var job = await db.ExtractionJobs.SingleAsync(j => j.Id == harness.JobId);
        Assert.Equal(0, job.AttemptCount);

        await harness.Recovery.RecoverHungInTenantAsync(harness.TenantId);
        Assert.Single(harness.Queue.Published);
        Assert.Equal(DocumentProcessingStatus.Uploaded, await harness.ReloadStatusAsync());
    }

    [Fact]
    public async Task A_failed_document_with_classify_retries_exhausted_marker_is_left_failed()
    {
        var harness = await SeedFailedAsync(
            "Gave up after 3 attempts. Classify stayed unreachable after in-call retries. Last error: The document could not be assessed: the 'classify' role could not be reached (InvalidOperationException: An exception has been raised that is likely due to a transient failure.) Nothing was stored.",
            attemptCount: ExtractionRequestedHandler.MaxAttempts);

        var action = await harness.Recovery.RecoverDocumentAsync(harness.TenantId, harness.DocumentId, force: false);

        Assert.Equal(HungRecoveryAction.None, action);
        Assert.Equal(DocumentProcessingStatus.Failed, await harness.ReloadStatusAsync());
        Assert.Empty(harness.Queue.Published);
    }

    [Fact]
    public async Task A_failed_document_with_a_parse_error_is_left_failed()
    {
        var harness = await SeedFailedAsync(
            "Gave up after 3 attempts. Last error: Malformed extraction payload.",
            attemptCount: ExtractionRequestedHandler.MaxAttempts);

        var action = await harness.Recovery.RecoverDocumentAsync(harness.TenantId, harness.DocumentId, force: false);

        Assert.Equal(HungRecoveryAction.None, action);
        Assert.Equal(DocumentProcessingStatus.Failed, await harness.ReloadStatusAsync());
        Assert.Empty(harness.Queue.Published);
    }

    [Fact]
    public async Task RecoverHungInTenantAsync_resurrects_stranded_three_minute_failures()
    {
        var harness = await SeedFailedAsync(
            "Gave up after 3 attempts. Processing made no progress for 3 minutes.",
            attemptCount: ExtractionRequestedHandler.MaxAttempts);

        await harness.Recovery.RecoverHungInTenantAsync(harness.TenantId);

        Assert.Equal(DocumentProcessingStatus.Uploaded, await harness.ReloadStatusAsync());
        Assert.Single(harness.Queue.Published);

        await using var db = CreateContext();
        var job = await db.ExtractionJobs.SingleAsync(j => j.Id == harness.JobId);
        Assert.Equal(0, job.AttemptCount);

        await harness.Recovery.RecoverHungInTenantAsync(harness.TenantId);
        Assert.Single(harness.Queue.Published);
        Assert.Equal(DocumentProcessingStatus.Uploaded, await harness.ReloadStatusAsync());
    }

    [Fact]
    public async Task RecoverHungInTenantAsync_does_not_resurrect_another_tenants_stranded_failure()
    {
        var harness = await SeedFailedAsync(
            "Gave up after 3 attempts. Processing made no progress for 3 minutes.",
            attemptCount: ExtractionRequestedHandler.MaxAttempts);
        var otherTenant = TenantId.New();

        await harness.Recovery.RecoverHungInTenantAsync(otherTenant);

        Assert.Equal(DocumentProcessingStatus.Failed, await harness.ReloadStatusAsync());
        Assert.Empty(harness.Queue.Published);
    }

    private DocumentsContractsDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString());
        return new DocumentsContractsDbContext(optionsBuilder.Options);
    }

    private Task<Harness> SeedHungAsync(DateTimeOffset claimedAt, int attemptCount) =>
        SeedAsync(
            processingStatus: DocumentProcessingStatus.Processing,
            jobStatus: ExtractionJobStatus.Queued,
            claimedAt: claimedAt,
            claimedBy: "worker-dead",
            startedAt: claimedAt,
            completedAt: null,
            attemptCount: attemptCount,
            errorDetail: null);

    private Task<Harness> SeedFailedAsync(string errorDetail, int attemptCount)
    {
        var failedAt = Now.AddMinutes(-10);
        return SeedAsync(
            processingStatus: DocumentProcessingStatus.Failed,
            jobStatus: ExtractionJobStatus.Failed,
            claimedAt: null,
            claimedBy: null,
            startedAt: failedAt,
            completedAt: failedAt,
            attemptCount: attemptCount,
            errorDetail: errorDetail);
    }

    private async Task<Harness> SeedAsync(
        DocumentProcessingStatus processingStatus,
        ExtractionJobStatus jobStatus,
        DateTimeOffset? claimedAt,
        string? claimedBy,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        int attemptCount,
        string? errorDetail)
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var queuedAt = claimedAt ?? startedAt ?? Now.AddHours(-1);
        var document = new Document
        {
            TenantId = tenantId,
            FileName = "stuck.pdf",
            MimeType = "application/pdf",
            StoragePath = $"{tenantId.Value}/stuck.pdf",
            Checksum = "checksum",
            CreatedAt = Now.AddHours(-1),
            ProcessingStatus = processingStatus,
        };
        var job = new ExtractionJob
        {
            TenantId = tenantId,
            DocumentId = document.Id,
            Stage = ExtractionStage.Classification,
            Status = jobStatus,
            QueuedAt = queuedAt,
            ClaimedAt = claimedAt,
            ClaimedBy = claimedBy,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            AttemptCount = attemptCount,
            ErrorDetail = errorDetail,
        };

        await using (var db = CreateContext())
        {
            db.Documents.Add(document);
            db.ExtractionJobs.Add(job);
            await db.SaveChangesAsync();
        }

        var storage = new RecordingStorage();
        storage.Saved[$"{tenantId.Value}/stuck.pdf"] = [1, 2, 3];
        var queue = new InMemoryExtractionQueue();
        var aborter = new RecordingAborter();
        var clock = new FixedClock(Now);
        var audit = new RecordingAuditWriter();
        var dbForServices = CreateContext();
        var gateway = new FixtureAiGateway(new AiGatewayModelOptions(), clock);
        var reprocess = new DocumentReprocessService(
            dbForServices,
            storage,
            queue,
            queue,
            new EmbeddingRetrievalService(dbForServices, gateway, tenantContext, clock),
            tenantContext,
            audit,
            clock);
        var recovery = new HungProcessingRecoveryService(
            dbForServices,
            reprocess,
            aborter,
            tenantContext,
            clock,
            NullLogger<HungProcessingRecoveryService>.Instance);

        return new Harness(this, tenantContext, tenantId, document.Id, job.Id, queue, aborter, recovery);
    }

    private sealed class Harness(
        HungProcessingRecoveryServiceTests fixture,
        ITenantContext tenantContext,
        TenantId tenantId,
        EntityId documentId,
        EntityId jobId,
        InMemoryExtractionQueue queue,
        RecordingAborter aborter,
        HungProcessingRecoveryService recovery)
    {
        public ITenantContext TenantContext { get; } = tenantContext;
        public TenantId TenantId { get; } = tenantId;
        public EntityId DocumentId { get; } = documentId;
        public EntityId JobId { get; } = jobId;
        public InMemoryExtractionQueue Queue { get; } = queue;
        public RecordingAborter Aborter { get; } = aborter;
        public HungProcessingRecoveryService Recovery { get; } = recovery;

        public async Task<DocumentProcessingStatus> ReloadStatusAsync()
        {
            await using var db = fixture.CreateContext();
            var document = await db.Documents.SingleAsync(d => d.Id == DocumentId);
            return document.ProcessingStatus;
        }
    }

    private sealed class RecordingAborter : IExtractionRunAborter
    {
        public List<Guid> Aborted { get; } = [];

        public CancellationToken Register(Guid jobId, CancellationToken outer) => outer;

        public void Abort(Guid jobId) => Aborted.Add(jobId);

        public void Unregister(Guid jobId)
        {
        }
    }

    private sealed class RecordingStorage : IDocumentStorage
    {
        public Dictionary<string, byte[]> Saved { get; } = new(StringComparer.Ordinal);

        public Task<string> SaveAsync(
            TenantId tenantId, EntityId documentId, int versionNumber, string fileName, Stream content,
            CancellationToken cancellationToken = default) =>
            Task.FromResult($"{tenantId.Value}/{fileName}");

        public Task<byte[]?> LoadAsync(TenantId tenantId, string storagePath, CancellationToken cancellationToken = default)
        {
            Saved.TryGetValue(storagePath, out var bytes);
            return Task.FromResult(bytes);
        }

        public Task DeleteAsync(TenantId tenantId, string storagePath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<string> SavePreviewAsync(
            TenantId tenantId, EntityId documentId, Stream content, CancellationToken cancellationToken = default) =>
            Task.FromResult($"{tenantId.Value}/{documentId.Value}/preview.png");

        public Task<string> SavePreviewPageAsync(
            TenantId tenantId, EntityId documentId, int page, Stream content,
            CancellationToken cancellationToken = default) =>
            Task.FromResult($"{tenantId.Value}/{documentId.Value}/page-{page}.png");
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
}
