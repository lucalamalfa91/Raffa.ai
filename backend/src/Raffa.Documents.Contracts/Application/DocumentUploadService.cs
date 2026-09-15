using System.Security.Cryptography;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Storage;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// Implements task E01/F06/US01/T01 (us-01-document-upload, AC-1/AC-2): stores the uploaded
/// bytes in tenant-scoped object storage (no cross-tenant path) and persists the
/// <see cref="Document"/> + initial <see cref="DocumentVersion"/> + a queued classification
/// <see cref="ExtractionJob"/> as one unit of work (module-map "Worker responsibilities":
/// classification is the first extraction stage after upload).
///
/// Owns its own tenant scope (<see cref="ITenantContext.BeginScope"/>) for the duration of the
/// call instead of relying on the caller to have entered one — every caller (the API endpoint
/// today, a queue handler later) gets the ADR-009 RLS backstop automatically, and a caller that
/// also wraps this in its own scope is unaffected (nested scopes restore the previous value on
/// dispose).
///
/// Task E01/F09/US01/T01 (r0-integration, AC-1 "upload document -> audit event"): writes one
/// <see cref="IAuditWriter"/> entry per successful upload. <see cref="IAuditWriter"/> lives in
/// <c>Raffa.SharedKernel</c> (not <c>Raffa.Audit</c>), so taking this dependency does not
/// cross the ADR-002 module boundary
/// (<c>Raffa.ArchitectureTests.DependencyDirectionTests</c>'s allow-list for this module is
/// exactly <c>[SharedKernel, AiGateway]</c>) — the same gateway-abstraction shape already used for
/// <see cref="IDocumentStorage"/>. The write happens inside the same tenant scope the upload
/// itself opened, so the audit row's RLS `WITH CHECK` is satisfied by the identical ambient
/// tenant claim (see <see cref="Raffa.Audit.Infrastructure.AuditWriter"/>'s own doc comment).
///
/// Task E16/F02/US02/T01 (durable-queue-transport, ADR-027 §D2): also publishes an
/// <see cref="ExtractionRequested"/> pointer through <see cref="IExtractionQueuePublisher"/> —
/// <b>before</b> <see cref="Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(CancellationToken)"/>
/// commits the three rows below, never after. That ordering is not incidental: <c>extraction_job</c>
/// carries <c>FORCE ROW LEVEL SECURITY</c>, so a poller or a sweeper over it is a cross-tenant read
/// ADR-009 forbids, which makes "commit, then publish, with a recovery sweep for a lost publish" an
/// unavailable design here — the ordering below is the only one whose failure mode (a phantom
/// message when the commit that follows fails) needs no such sweep.
/// </summary>
public sealed class DocumentUploadService(
    DocumentsContractsDbContext dbContext,
    IDocumentStorage storage,
    IExtractionQueuePublisher extractionQueuePublisher,
    ITenantContext tenantContext,
    IClock clock,
    IAuditWriter auditWriter)
{
    private const int InitialVersionNumber = 1;

    /// <param name="actor">The caller's resolved token subject (ADR-011 w16 clause 15) — required,
    /// no default, so a placeholder can never return by omission. Recorded on
    /// <see cref="DocumentVersion.CreatedBy"/> and on the <c>document.uploaded</c> audit row.</param>
    public async Task<Result<DocumentUploadResult>> UploadAsync(
        TenantId tenantId,
        string fileName,
        string? mimeType,
        Stream content,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Result<DocumentUploadResult>.Failure("A file name is required.");
        }

        // Buffered once: the checksum needs the full byte range, and the storage write needs a
        // seekable, replayable stream regardless of what kind of stream the caller handed in (an
        // ASP.NET Core request body stream is not guaranteed seekable).
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        if (buffer.Length == 0)
        {
            return Result<DocumentUploadResult>.Failure("The uploaded file is empty.");
        }

        buffer.Position = 0;
        var checksum = Convert.ToHexString(SHA256.HashData(buffer.ToArray()));

        var documentId = EntityId.New();
        var now = clock.UtcNow;
        var effectiveMimeType = string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType;

        using var tenantScope = tenantContext.BeginScope(tenantId);

        buffer.Position = 0;
        var storagePath = await storage
            .SaveAsync(tenantId, documentId, InitialVersionNumber, fileName, buffer, cancellationToken)
            .ConfigureAwait(false);

        var document = new Document
        {
            Id = documentId,
            TenantId = tenantId,
            FileName = fileName,
            MimeType = effectiveMimeType,
            StoragePath = storagePath,
            Checksum = checksum,
            ProcessingStatus = DocumentProcessingStatus.Uploaded,
            CreatedAt = now,
        };

        var version = new DocumentVersion
        {
            TenantId = tenantId,
            DocumentId = documentId,
            VersionNumber = InitialVersionNumber,
            StoragePath = storagePath,
            Checksum = checksum,
            CreatedBy = actor,
            CreatedAt = now,
        };

        // First stage of the extraction pipeline (module-map "Worker responsibilities"): the
        // Worker host picks this up and classifies the document type before any further
        // extraction stage runs. Only the queued job row is created here — dequeue/consumption
        // is the Worker's concern and is not part of this task.
        var classificationJob = new ExtractionJob
        {
            TenantId = tenantId,
            DocumentId = documentId,
            Stage = ExtractionStage.Classification,
            Status = ExtractionJobStatus.Queued,
            QueuedAt = now,
        };

        dbContext.Documents.Add(document);
        dbContext.DocumentVersions.Add(version);
        dbContext.ExtractionJobs.Add(classificationJob);

        // Publish before commit (ADR-027 §D2, see the type doc comment): the ids are already known
        // because ExtractionJob.Id is ValueGeneratedNever (client-generated, TenantScopedEntity), so
        // the message can be built before SaveChangesAsync ever runs. If the commit below then
        // fails, the message is a harmless phantom — the Worker's claim finds no such job and
        // completes it (ADR-027 §D3/§C6).
        await extractionQueuePublisher.PublishAsync(
            new ExtractionRequested(
                tenantId.Value,
                documentId.Value,
                classificationJob.Id.Value,
                ExtractionRequested.CurrentSchemaVersion),
            cancellationToken).ConfigureAwait(false);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // AC-1 "upload document -> audit event": recorded only once the upload itself is
        // durable, still inside this call's own tenant scope (see the type doc comment). A
        // failure here throws and fails the whole upload rather than silently dropping the audit
        // record — ADR-011 treats audit as a compliance control, not a best-effort side-channel.
        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                actor,
                "document.uploaded",
                "document",
                documentId.Value.ToString(),
                now),
            cancellationToken).ConfigureAwait(false);

        return Result<DocumentUploadResult>.Success(new DocumentUploadResult(
            document.Id, document.FileName, document.MimeType, document.ProcessingStatus, document.CreatedAt));
    }
}
