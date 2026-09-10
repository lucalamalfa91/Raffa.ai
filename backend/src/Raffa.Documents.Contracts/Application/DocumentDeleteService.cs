using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Storage;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// Task E13/F04/US01/T02 (documents-v2-api, <c>inputs/requirements.md</c> R-DOC-10): removes one
/// document completely — every stored object (each version's blob plus the rendered preview),
/// every retrieval chunk, the version rows, the extraction jobs, the document row itself — and
/// detaches the contract's link to it, so a contract built from several documents survives the
/// deletion of one of them.
///
/// <para>
/// <b>What is deliberately not deleted:</b> the <see cref="Contract"/> and the facts already
/// extracted from this document (clauses, obligations, risks, line items, evidence). A contract is
/// a business object a human has been correcting and negotiating against; erasing it because one
/// source file was removed would destroy far more than the operator asked for. Those rows are
/// <em>detached</em> instead: every <c>source_document_id</c> pointing at this document, and every
/// <c>extraction_job_id</c> pointing at one of its jobs, is set to null first. That is not an
/// optimisation — each of those columns is a real foreign key declared <c>ON DELETE RESTRICT</c>
/// (see <c>Migrations/Scripts/documents-contracts.sql</c>), so without the detachment Postgres
/// refuses the delete outright.
/// </para>
///
/// <para>
/// <b>Order:</b> storage first, then rows. A failure while deleting blobs leaves the rows in place
/// and the operation reports failure — the operator can retry, and deletion is idempotent. The
/// reverse order could leave orphaned bytes with nothing left in the database naming them.
/// </para>
///
/// <para>
/// <b>Admin-only, by the caller</b> (<c>DELETE /api/documents/{id}</c>). The
/// <c>document.deleted</c> audit row is written here rather than in the endpoint, inside this
/// call's own tenant scope: the audit table is itself RLS-protected, so a write with no ambient
/// tenant is rejected by Postgres (ADR-009/ADR-011). Same posture as
/// <c>DocumentUploadService.UploadAsync</c>'s own <c>document.uploaded</c> row.
/// </para>
/// </summary>
public sealed class DocumentDeleteService(
    DocumentsContractsDbContext dbContext,
    IDocumentStorage storage,
    EmbeddingRetrievalService embeddingRetrievalService,
    ITenantContext tenantContext,
    IAuditWriter auditWriter,
    IClock clock)
{
    private const string DocumentSourceType = "Document";

    /// <summary>Audit action for a completed deletion (R-DOC-10).</summary>
    public const string DeletedAuditAction = "document.deleted";

    /// <summary>
    /// Deletes the document, or returns <see langword="null"/> when no such document exists for
    /// this tenant (the endpoint turns that into a 404).
    /// </summary>
    /// <param name="actor">Who asked for the deletion, for the audit row (the caller's
    /// <c>X-User-Id</c>, or the unattributed placeholder while ADR-010 is not wired).</param>
    public async Task<Result<DocumentDeleteResult>?> DeleteAsync(
        TenantId tenantId, EntityId documentId, string actor, CancellationToken cancellationToken = default)
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

        var versions = await dbContext.DocumentVersions
            .Where(v => v.TenantId == tenantId && v.DocumentId == documentId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var storagePaths = versions
            .Select(v => v.StoragePath)
            .Append(document.StoragePath)
            .Concat(document.PreviewPath is { } preview ? [preview] : Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        try
        {
            foreach (var path in storagePaths)
            {
                await storage.DeleteAsync(tenantId, path, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result<DocumentDeleteResult>.Failure(
                $"The stored objects for document {documentId} could not be deleted: {exception.Message}");
        }

        var chunksRemoved = await embeddingRetrievalService
            .RemoveChunksAsync(tenantId, DocumentSourceType, documentId, cancellationToken)
            .ConfigureAwait(false);

        var jobs = await dbContext.ExtractionJobs
            .Where(j => j.TenantId == tenantId && j.DocumentId == documentId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var factsDetached = await DetachFactsAsync(tenantId, documentId, jobs, cancellationToken)
            .ConfigureAwait(false);

        // Detaching the contract link needs no write of its own: the link lives on this very row
        // (Document.ContractId; Contract has no Documents navigation), so removing the document
        // removes the link and leaves the contract standing (R-DOC-10 "detaches the contract's
        // document link" — never a cascade into the contract).
        var detachedContract = document.ContractId;

        dbContext.ExtractionJobs.RemoveRange(jobs);
        dbContext.DocumentVersions.RemoveRange(versions);
        dbContext.Documents.Remove(document);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                actor,
                DeletedAuditAction,
                "document",
                documentId.Value.ToString(),
                clock.UtcNow,
                $"objectsDeleted={storagePaths.Count}; chunksRemoved={chunksRemoved}; factsDetached={factsDetached}"),
            cancellationToken).ConfigureAwait(false);

        return Result<DocumentDeleteResult>.Success(new DocumentDeleteResult(
            documentId,
            document.ContractId,
            storagePaths.Count,
            chunksRemoved,
            detachedContract is null ? 0 : 1,
            factsDetached));
    }

    /// <summary>
    /// Clears every reference to this document (and to its extraction jobs) from the rows that
    /// survive it, and returns how many were re-pointed. All five columns are nullable by design —
    /// a fact whose source file is gone is still a fact, it just no longer names where it came
    /// from — but all five are also <c>ON DELETE RESTRICT</c> foreign keys, so this must run before
    /// the delete, not as a tidy-up after it.
    /// </summary>
    private async Task<int> DetachFactsAsync(
        TenantId tenantId,
        EntityId documentId,
        IReadOnlyList<ExtractionJob> jobs,
        CancellationToken cancellationToken)
    {
        // Nullable element type on purpose: extraction_evidence.extraction_job_id is a nullable
        // converted value object, and Npgsql cannot build an array-contains from a List<EntityId>
        // against a Nullable<EntityId> column ("Expression of type EntityId cannot be used for
        // parameter of type Nullable<EntityId>").
        var jobIds = jobs.Select(j => (EntityId?)j.Id).ToList();

        var clauses = await dbContext.Clauses
            .Where(c => c.TenantId == tenantId && c.SourceDocumentId == documentId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var obligations = await dbContext.Obligations
            .Where(o => o.TenantId == tenantId && o.SourceDocumentId == documentId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var risks = await dbContext.Risks
            .Where(r => r.TenantId == tenantId && r.SourceDocumentId == documentId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var lineItems = await dbContext.ContractLineItems
            .Where(l => l.TenantId == tenantId && l.SourceDocumentId == documentId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var evidence = await dbContext.ExtractionEvidences
            .Where(e => e.TenantId == tenantId
                && (e.SourceDocumentId == documentId
                    || (e.ExtractionJobId != null && jobIds.Contains(e.ExtractionJobId))))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var clause in clauses)
        {
            clause.SourceDocumentId = null;
        }

        foreach (var obligation in obligations)
        {
            obligation.SourceDocumentId = null;
        }

        foreach (var risk in risks)
        {
            risk.SourceDocumentId = null;
        }

        foreach (var lineItem in lineItems)
        {
            lineItem.SourceDocumentId = null;
        }

        foreach (var row in evidence)
        {
            row.SourceDocumentId = null;
            row.ExtractionJobId = null;
        }

        if (clauses.Count + obligations.Count + risks.Count + lineItems.Count + evidence.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return clauses.Count + obligations.Count + risks.Count + lineItems.Count + evidence.Count;
    }
}

/// <summary>What a deletion actually removed — returned for the audit detail and for tests.</summary>
/// <param name="FactsDetached">Rows (clauses, obligations, risks, line items, evidence) that
/// survived the deletion with their source-document reference cleared.</param>
public sealed record DocumentDeleteResult(
    EntityId DocumentId,
    EntityId? ContractId,
    int ObjectsDeleted,
    int ChunksRemoved,
    int ContractsDetached,
    int FactsDetached);
