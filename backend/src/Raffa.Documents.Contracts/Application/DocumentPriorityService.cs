using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// Task E16/F03/US02/T01 (priority by claim; ADR-027 w15 footer C12), built by hand on 2026-09-14
/// after the first real twenty-file batch on <c>dev</c>. Backs <c>POST /api/documents/{id}/prioritise</c>:
/// the web calls it once when a user opens a document that is still queued.
///
/// <para>
/// Service Bus is FIFO and this wave keeps exactly one subscription (OQ-w15-012), so a document
/// cannot move in the broker. It moves on its row instead: the document's queued, unclaimed
/// classification job is stamped <see cref="ExtractionJob.PrioritisedAt"/>, and the next delivery
/// any Worker replica handles in this tenant runs that job before its own
/// (<see cref="Extraction.ExtractionRequestedHandler"/>). The stamp is the order between several
/// opened documents ("asked first"); it grants nothing else.
/// </para>
///
/// <para>
/// <b>Idempotent by design</b>, because the client calls it blindly on open: a job already claimed
/// (the Worker is on it), already prioritised, or already terminal is a no-op that still answers
/// "done" — only "no such document for this tenant" is a <see langword="null"/>, the endpoint's 404.
/// One <c>document.prioritised</c> audit row when something changed, none otherwise.
/// </para>
/// </summary>
public sealed class DocumentPriorityService(
    DocumentsContractsDbContext dbContext,
    ITenantContext tenantContext,
    IAuditWriter auditWriter,
    IClock clock)
{
    public const string PrioritisedAuditAction = "document.prioritised";

    /// <summary>
    /// Stamps the document's queued, unclaimed classification job(s). Returns <see langword="null"/>
    /// when no such document exists for this tenant, <see cref="DocumentPriorityOutcome.Prioritised"/>
    /// when a job was stamped now, and <see cref="DocumentPriorityOutcome.NothingToDo"/> when there
    /// was nothing left to stamp (already claimed, already prioritised, or terminal).
    /// </summary>
    /// <param name="actor">Who opened the document, for the audit row.</param>
    public async Task<DocumentPriorityOutcome?> PrioritiseAsync(
        TenantId tenantId, EntityId documentId, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var exists = await dbContext.Documents
            .AnyAsync(d => d.TenantId == tenantId && d.Id == documentId, cancellationToken)
            .ConfigureAwait(false);
        if (!exists)
        {
            return null;
        }

        // Tracked read-modify-write rather than ExecuteUpdate: the in-memory test host has no
        // relational provider, and the row count here is one.
        var waiting = await dbContext.ExtractionJobs
            .Where(j => j.TenantId == tenantId
                && j.DocumentId == documentId
                && j.Stage == ExtractionStage.Classification
                && j.Status == ExtractionJobStatus.Queued
                && j.ClaimedAt == null
                && j.PrioritisedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (waiting.Count == 0)
        {
            return DocumentPriorityOutcome.NothingToDo;
        }

        var now = clock.UtcNow;
        foreach (var job in waiting)
        {
            job.PrioritisedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                actor,
                PrioritisedAuditAction,
                "document",
                documentId.Value.ToString(),
                now,
                $"jobs={string.Join(",", waiting.Select(j => j.Id.Value))}"),
            cancellationToken).ConfigureAwait(false);

        return DocumentPriorityOutcome.Prioritised;
    }
}

/// <summary>What <see cref="DocumentPriorityService.PrioritiseAsync"/> did; both answer 204.</summary>
public enum DocumentPriorityOutcome
{
    Prioritised,
    NothingToDo,
}
