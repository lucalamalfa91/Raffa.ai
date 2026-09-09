using Contigo.Documents.Contracts.Application.Extraction;
using Contigo.Documents.Contracts.Domain;
using Contigo.Documents.Contracts.Infrastructure;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Storage;
using Contigo.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Contigo.Documents.Contracts.Application;

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
    DocumentProcessingPipeline processingPipeline,
    EmbeddingRetrievalService embeddingRetrievalService,
    ITenantContext tenantContext,
    IAuditWriter auditWriter,
    IClock clock)
{
    /// <summary>Same discriminator <see cref="DocumentProcessingPipeline"/> indexes under.</summary>
    private const string DocumentSourceType = "Document";

    /// <summary>Audit action for a completed re-run (R-DOC-07).</summary>
    public const string ReprocessedAuditAction = "document.reprocessed";

    /// <summary>
    /// Returns the pipeline's own summary for the re-run, a failure when the document has no
    /// readable bytes any more, or <see langword="null"/> when no such document exists for this
    /// tenant (the endpoint turns that into a 404).
    /// </summary>
    /// <param name="actor">Who asked for the re-run, for the audit row.</param>
    public async Task<Result<DocumentProcessingSummary>?> ReprocessAsync(
        TenantId tenantId, EntityId documentId, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        Document? document;
        using (tenantContext.BeginScope(tenantId))
        {
            document = await dbContext.Documents
                .AsNoTracking()
                .SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == documentId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (document is null)
        {
            return null;
        }

        var bytes = await storage.LoadAsync(tenantId, document.StoragePath, cancellationToken).ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0)
        {
            return Result<DocumentProcessingSummary>.Failure(
                $"The stored bytes for document {documentId} could not be read back from object storage.");
        }

        // Replace, never merge: see the type doc comment (R-DOC-07 AC-1).
        await embeddingRetrievalService
            .RemoveChunksAsync(tenantId, DocumentSourceType, documentId, cancellationToken)
            .ConfigureAwait(false);

        // Requeue the classification job so the pipeline's own classify step has a row to advance,
        // exactly as it does on a first upload — a reprocess is a re-run of the same pipeline, not
        // a second, differently-shaped one.
        await RequeueClassificationJobAsync(tenantId, documentId, document.CreatedAt, cancellationToken)
            .ConfigureAwait(false);

        var result = await processingPipeline
            .ProcessAsync(tenantId, documentId, document.FileName, document.MimeType, bytes, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            using var tenantScope = tenantContext.BeginScope(tenantId);
            await auditWriter.WriteAsync(
                new AuditEntry(
                    tenantId,
                    actor,
                    ReprocessedAuditAction,
                    "document",
                    documentId.Value.ToString(),
                    clock.UtcNow,
                    $"pagesParsed={result.Value.PagesParsed}; chunksIndexed={result.Value.ChunksIndexed}; " +
                    $"documentType={result.Value.DocumentType}"),
                cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private async Task RequeueClassificationJobAsync(
        TenantId tenantId, EntityId documentId, DateTimeOffset fallbackQueuedAt, CancellationToken cancellationToken)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var classificationJob = await dbContext.ExtractionJobs
            .Where(j => j.TenantId == tenantId
                && j.DocumentId == documentId
                && j.Stage == ExtractionStage.Classification)
            .OrderByDescending(j => j.QueuedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (classificationJob is null)
        {
            dbContext.ExtractionJobs.Add(new ExtractionJob
            {
                TenantId = tenantId,
                DocumentId = documentId,
                Stage = ExtractionStage.Classification,
                Status = ExtractionJobStatus.Queued,
                QueuedAt = fallbackQueuedAt,
            });
        }
        else
        {
            classificationJob.Status = ExtractionJobStatus.Queued;
            classificationJob.StartedAt = null;
            classificationJob.CompletedAt = null;
            classificationJob.ErrorDetail = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
