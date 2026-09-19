using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Storage;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// Task E13/F04/US01/T02 (documents-v2-api, <c>inputs/requirements.md</c> R-DOC-07 "Load, re-OCR,
/// re-embed"): re-runs the whole read side of the pipeline for one already-stored document —
/// load the bytes back through <see cref="IDocumentStorage.LoadAsync"/>, hybrid-parse them again
/// (native text or the <c>ocr</c> role, ADR-017), throw away this document's existing retrieval
/// chunks and re-index page-aware ones, then re-run staged extraction so facts added by later
/// tasks (the supplier fact of F03/T02, for instance) back-fill onto documents uploaded before
/// they existed.
///
/// <para>
/// <b>Why the old chunks go first.</b> Documents indexed before this task stored whatever the V1
/// parse produced — for a born-digital PDF that could be the raw <c>%PDF-…</c> byte soup, and for
/// a scanned page the fixture OCR placeholder. Leaving them in place would mean Ask keeps citing
/// unreadable text next to the new, clean chunks; R-DOC-07 AC-1 is explicit that after a reprocess
/// no embedding row for the tenant starts with <c>%PDF</c>. So the replacement is a delete-then-
/// index, not an upsert.
/// </para>
///
/// <para>
/// <b>Admin-only, by the caller.</b> This service does not know about roles: the endpoint
/// (<c>POST /api/documents/{id}/reprocess</c>) enforces Admin before calling, the same way every
/// other privileged surface in this host does. The <c>document.reprocessed</c> audit row is
/// written here, inside this call's own tenant scope — the audit table is RLS-protected, so a
/// write with no ambient tenant is rejected by Postgres (ADR-009/ADR-011).
/// </para>
/// </summary>
public sealed class DocumentReprocessService(
    DocumentsContractsDbContext dbContext,
    IDocumentStorage storage,
    IExtractionQueuePublisher extractionQueuePublisher,
    IExtractionDeadLetterResubmitter deadLetterResubmitter,
    EmbeddingRetrievalService embeddingRetrievalService,
    ITenantContext tenantContext,
    IAuditWriter auditWriter,
    IClock clock)
{
    /// <summary>Same discriminator <see cref="DocumentProcessingPipeline"/> indexes under.</summary>
    private const string DocumentSourceType = "Document";

    /// <summary>Audit action for a re-run that was queued (R-DOC-07; queued, not completed, since
    /// task E16/F02/US03/T01 — completion is the Worker's, and the pipeline's own rows record it).</summary>
    public const string ReprocessedAuditAction = "document.reprocessed";

    /// <summary>
    /// Queues the re-run and returns at once (task E16/F02/US03/T01, ADR-027 §D1): the parse,
    /// classify and re-embed that used to run inline here (<c>:58-116</c> before this wave) are now
    /// the Worker's, reached through the same <see cref="ExtractionRequested"/> pointer a first
    /// upload publishes. Returns the queued job's id, a failure when the stored bytes are no longer
    /// readable (the Worker would only discover it later; the operator learns it now), or
    /// <see langword="null"/> when no such document exists for this tenant (the endpoint's 404).
    /// </summary>
    /// <param name="actor">Who asked for the re-run, for the audit row.</param>
    /// <param name="resetAttemptCount">
    /// When true, the classification job's <c>attempt_count</c> is zeroed so the next claim starts
    /// a fresh budget. Used to resurrect hang-cap Failures and the classify-outage / EF-transient
    /// wrap; ordinary reprocess keeps the lifetime bound so a bad file cannot loop.
    /// </param>
    public async Task<Result<DocumentReprocessQueued>?> ReprocessAsync(
        TenantId tenantId,
        EntityId documentId,
        string actor,
        CancellationToken cancellationToken = default,
        bool resetAttemptCount = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var document = await dbContext.Documents
            .SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == documentId, cancellationToken)
            .ConfigureAwait(false);

        if (document is null)
        {
            return null;
        }

        // A Rejected row has no blob any more (the Worker deleted it, ADR-027 §D6) and nothing a
        // re-run could change; R-DOC-07's "Retry upload" for it is a new upload, which the screen
        // offers. Failing here rather than queueing a job that would fail on LoadAsync keeps the
        // answer in the request the operator is looking at.
        if (document.ProcessingStatus == DocumentProcessingStatus.Rejected)
        {
            return Result<DocumentReprocessQueued>.Failure(
                "This document was refused by the content gate; upload it again rather than reprocessing it.");
        }

        var bytes = await storage.LoadAsync(tenantId, document.StoragePath, cancellationToken).ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0)
        {
            return Result<DocumentReprocessQueued>.Failure(
                $"The stored bytes for document {documentId} could not be read back from object storage.");
        }

        // Replace, never merge (R-DOC-07 AC-1): the stale chunks go now, in this request, so Ask
        // never cites the old unreadable text while the Worker is re-indexing. The window between
        // this delete and the Worker's re-index is a document with no chunks — honest ("still
        // processing", which the status below says) rather than wrong.
        await embeddingRetrievalService
            .RemoveChunksAsync(tenantId, DocumentSourceType, documentId, cancellationToken)
            .ConfigureAwait(false);

        var job = await RequeueClassificationJobAsync(
                tenantId, documentId, document.CreatedAt, resetAttemptCount, cancellationToken)
            .ConfigureAwait(false);
        document.ProcessingStatus = DocumentProcessingStatus.Uploaded;

        var pointer = new ExtractionRequested(
            tenantId.Value, documentId.Value, job.Id.Value, ExtractionRequested.CurrentSchemaVersion);

        // Dead-letter first: a stranded Uploaded row is usually a pointer that the Worker dead-lettered
        // (job-not-found, max delivery) while the row stayed Queued. Resubmitting that message puts
        // work back on the topic without a second copy. No match (or no broker) → publish a fresh
        // pointer, the same path a first upload uses. Publish-before-commit in both branches, as
        // DocumentUploadService does: a send failure fails the request with nothing durable; a
        // commit failure leaves a pointer the Worker's claim answers with zero rows.
        var resubmittedFromDeadLetter = await deadLetterResubmitter
            .TryResubmitAsync(pointer, cancellationToken)
            .ConfigureAwait(false);
        if (!resubmittedFromDeadLetter)
        {
            await extractionQueuePublisher.PublishAsync(pointer, cancellationToken).ConfigureAwait(false);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var origin = resubmittedFromDeadLetter ? "deadLetter" : "queued";
        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                actor,
                ReprocessedAuditAction,
                "document",
                documentId.Value.ToString(),
                clock.UtcNow,
                $"{origin}; extractionJobId={job.Id.Value}; attempt={job.AttemptCount + 1}"),
            cancellationToken).ConfigureAwait(false);

        return Result<DocumentReprocessQueued>.Success(new DocumentReprocessQueued(documentId, job.Id));
    }

    /// <summary>
    /// Puts the classification job back into the exact state a fresh claim requires: Queued,
    /// unclaimed, no timestamps. <c>ClaimedAt</c>/<c>ClaimedBy</c> must be cleared — the claim's
    /// compare-and-swap is <c>claimed_at IS NULL</c>, so a job left claimed from its first run would
    /// refuse the re-run's delivery for ever. <c>AttemptCount</c> is kept unless
    /// <paramref name="resetAttemptCount"/> is set: it is the bound on redeliveries across the
    /// document's whole life, not per request.
    /// </summary>
    private async Task<ExtractionJob> RequeueClassificationJobAsync(
        TenantId tenantId,
        EntityId documentId,
        DateTimeOffset fallbackQueuedAt,
        bool resetAttemptCount,
        CancellationToken cancellationToken)
    {
        var classificationJob = await dbContext.ExtractionJobs
            .Where(j => j.TenantId == tenantId
                && j.DocumentId == documentId
                && j.Stage == ExtractionStage.Classification)
            .OrderByDescending(j => j.QueuedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (classificationJob is null)
        {
            classificationJob = new ExtractionJob
            {
                TenantId = tenantId,
                DocumentId = documentId,
                Stage = ExtractionStage.Classification,
                Status = ExtractionJobStatus.Queued,
                QueuedAt = fallbackQueuedAt,
            };
            dbContext.ExtractionJobs.Add(classificationJob);
        }
        else
        {
            classificationJob.Status = ExtractionJobStatus.Queued;
            classificationJob.QueuedAt = clock.UtcNow;
            classificationJob.StartedAt = null;
            classificationJob.CompletedAt = null;
            classificationJob.ErrorDetail = null;
            classificationJob.ClaimedAt = null;
            classificationJob.ClaimedBy = null;
            // ADR-027 w15 footer C12: a re-run is a fresh job nobody has opened yet -- it must not
            // inherit the queue-jump the previous run was given.
            classificationJob.PrioritisedAt = null;
            if (resetAttemptCount)
            {
                classificationJob.AttemptCount = 0;
            }
        }

        return classificationJob;
    }
}

/// <summary>The reprocess endpoint's 202 body: which document, and which job the Worker will claim.</summary>
public sealed record DocumentReprocessQueued(EntityId DocumentId, EntityId ExtractionJobId);
