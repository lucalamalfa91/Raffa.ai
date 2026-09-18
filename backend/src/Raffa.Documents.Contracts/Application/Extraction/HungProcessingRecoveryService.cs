using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Aborts a hung processing run and either re-enqueues the document through
/// <see cref="DocumentReprocessService"/> (the same path as <c>POST /api/documents/{id}/reprocess</c>)
/// or marks it <see cref="DocumentProcessingStatus.Failed"/> once
/// <see cref="ExtractionRequestedHandler.MaxAttempts"/> full restarts have already been claimed.
/// Tenant-scoped: every query and write runs inside the caller's
/// <see cref="ITenantContext"/> scope (ADR-009).
/// </summary>
public sealed class HungProcessingRecoveryService(
    DocumentsContractsDbContext dbContext,
    DocumentReprocessService reprocessService,
    IExtractionRunAborter runAborter,
    ITenantContext tenantContext,
    IClock clock,
    ILogger<HungProcessingRecoveryService> logger)
{
    public const string GaveUpErrorPrefix = "Gave up after";

    /// <summary>
    /// Scans this tenant's <see cref="DocumentProcessingStatus.Processing"/> rows and recovers
    /// each one that has been silent for <see cref="HungProcessingDetector.InactivityWindow"/>.
    /// Called from <c>GET /api/documents</c> so a user looking at the list (or the rail poll)
    /// unsticks zombies whose Service Bus message was already completed as claim-lost.
    /// </summary>
    public async Task RecoverHungInTenantAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var processingIds = await dbContext.Documents
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.ProcessingStatus == DocumentProcessingStatus.Processing)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var documentId in processingIds)
        {
            await RecoverDocumentAsync(tenantId, documentId, force: false, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Recovers one document. <paramref name="force"/> skips the inactivity check — used when the
    /// in-process run already cancelled or crashed, so waiting out the window would only leave
    /// the row on "Uploading…" longer.
    /// </summary>
    public async Task<HungRecoveryAction> RecoverDocumentAsync(
        TenantId tenantId,
        EntityId documentId,
        bool force,
        CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var document = await dbContext.Documents
            .SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == documentId, cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return HungRecoveryAction.None;
        }

        var jobs = await dbContext.ExtractionJobs
            .Where(j => j.TenantId == tenantId && j.DocumentId == documentId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var classification = jobs
            .Where(j => j.Stage == ExtractionStage.Classification)
            .OrderByDescending(j => j.QueuedAt)
            .FirstOrDefault();

        var claimedInFlight = classification is not null
            && classification.ClaimedAt is not null
            && classification.CompletedAt is null
            && classification.Status is ExtractionJobStatus.Queued or ExtractionJobStatus.Running;

        if (document.ProcessingStatus != DocumentProcessingStatus.Processing && !claimedInFlight)
        {
            return HungRecoveryAction.None;
        }

        if (!force)
        {
            var progress = jobs.Select(j => new ExtractionJobProgress(j.ClaimedAt, j.StartedAt, j.CompletedAt));
            // A live Worker that is still heartbeating started_at will not look hung, so GET
            // recovery must not re-enqueue and start a second ProcessAsync while that owner holds
            // the claim. Silence for InactivityWindow is the only cheap signal we need.
            var hung = HungProcessingDetector.IsHung(document.ProcessingStatus, clock.UtcNow, progress);
            if (!hung)
            {
                return HungRecoveryAction.None;
            }
        }

        if (classification is not null)
        {
            runAborter.Abort(classification.Id.Value);
        }

        var attempts = classification?.AttemptCount ?? 0;
        if (attempts >= ExtractionRequestedHandler.MaxAttempts)
        {
            await FailTerminalAsync(document, classification, attempts, cancellationToken).ConfigureAwait(false);
            logger.LogWarning(
                "Document {DocumentId} failed after {Attempts} hung processing attempts",
                documentId.Value, attempts);
            return HungRecoveryAction.Failed;
        }

        dbContext.ChangeTracker.Clear();

        var queued = await reprocessService
            .ReprocessAsync(tenantId, documentId, ExtractionRequestedHandler.WorkerActor, cancellationToken)
            .ConfigureAwait(false);

        if (queued is null)
        {
            return HungRecoveryAction.None;
        }

        if (queued.IsFailure)
        {
            logger.LogWarning(
                "Hung processing recovery could not requeue document {DocumentId}: {Error}",
                documentId.Value, queued.Error);
            return HungRecoveryAction.None;
        }

        logger.LogWarning(
            "Aborted hung processing for document {DocumentId} (attempt {Attempt}/{Max}) and re-enqueued from scratch",
            documentId.Value, attempts, ExtractionRequestedHandler.MaxAttempts);
        return HungRecoveryAction.Requeued;
    }

    private async Task FailTerminalAsync(
        Document document,
        ExtractionJob? job,
        int attempts,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        document.ProcessingStatus = DocumentProcessingStatus.Failed;
        if (job is not null)
        {
            job.Status = ExtractionJobStatus.Failed;
            job.StartedAt ??= now;
            job.CompletedAt = now;
            job.ClaimedAt = null;
            job.ClaimedBy = null;
            job.ErrorDetail =
                $"{GaveUpErrorPrefix} {attempts} attempts. Processing made no progress for {HungProcessingDetector.InactivityWindow.TotalMinutes:0} minutes.";
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

public enum HungRecoveryAction
{
    None,
    Requeued,
    Failed,
}
