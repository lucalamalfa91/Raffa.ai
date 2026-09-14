using Raffa.Documents.Contracts.Application.Admission;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Storage;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Raised for a failure the next delivery may well not see again — the AI gateway could not be
/// reached, object storage timed out. The consumer abandons the message so the broker redelivers
/// it; the handler has already released its claim so the redelivery can win it. Anything else
/// that goes wrong is terminal for the document and is written to the row, never thrown.
/// </summary>
public sealed class ExtractionTransientException(string message) : Exception(message);

/// <summary>
/// The Worker's half of an upload (task E16/F02/US03/T01 with the transport it depends on; ADR-027
/// §D1–§D3, §D6). One <see cref="ExtractionRequested"/> pointer in, one durable outcome out:
/// <list type="number">
/// <item><b>Claim.</b> <see cref="IExtractionJobClaimStore.TryClaimAsync"/> is the compare-and-swap
/// that turns at-least-once delivery into exactly-once work. Zero rows means a duplicate, a
/// redelivery racing the original, a second replica, or a job that is no longer queued — and
/// the correct response to all four is the same: do nothing, complete the message.</item>
/// <item><b>Content gate.</b> <see cref="DocumentAdmissionGate.EvaluateAsync"/> — parse/OCR, the
/// readable-text floor, the Foundry <c>classify</c> call and the threshold — is exactly the work
/// the request used to wait minutes for. A refusal is now a <b>row</b>:
/// <see cref="DocumentProcessingStatus.Rejected"/> with its reason <em>code</em>, the detected type
/// and the confidence, the blob deleted, the classification job completed. The gate's own
/// <c>document.rejected</c> audit row is written where it always was.</item>
/// <item><b>Pipeline.</b> An admitted document goes through
/// <see cref="DocumentProcessingPipeline"/>'s pages-and-classification overload — the model is
/// still called once per upload, the parse and the verdict are reused — which advances the
/// classification job and the document's status exactly as it did in-request.</item>
/// </list>
/// <para>
/// <b>Transient vs terminal.</b> A gateway that could not be reached is transient: the claim is
/// released (so the redelivery can claim), and <see cref="ExtractionTransientException"/> tells the
/// consumer to abandon. <see cref="MaxAttempts"/> deliveries later the row itself goes
/// <see cref="DocumentProcessingStatus.Failed"/> — the database, not the dead-letter queue, owns the
/// terminal state (ADR-027 §D3). A document the pipeline genuinely cannot read is terminal on the
/// first attempt.
/// </para>
/// </summary>
public sealed class ExtractionRequestedHandler(
    DocumentsContractsDbContext dbContext,
    IDocumentStorage storage,
    DocumentAdmissionGate admissionGate,
    DocumentProcessingPipeline processingPipeline,
    IExtractionJobClaimStore claimStore,
    ITenantContext tenantContext,
    IClock clock,
    ILogger<ExtractionRequestedHandler> logger)
{
    /// <summary>Deliveries a job may consume before its row is marked terminal (ADR-027 §D3).</summary>
    public const int MaxAttempts = 3;

    /// <summary>The actor recorded on the gate's rejection audit row — the Worker, not a person.</summary>
    public const string WorkerActor = "system:worker";

    private static readonly string ClaimedBy = Environment.MachineName;

    public async Task HandleAsync(ExtractionRequested message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var tenantId = new TenantId(message.TenantId);
        var documentId = new EntityId(message.DocumentId);
        var jobId = new EntityId(message.ExtractionJobId);

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var claimed = await claimStore.TryClaimAsync(jobId, ClaimedBy, cancellationToken).ConfigureAwait(false);
        if (claimed == 0)
        {
            logger.LogInformation(
                "Extraction job {JobId} for document {DocumentId} was not claimable (duplicate delivery, already claimed, or no longer queued); nothing to do",
                jobId.Value, documentId.Value);
            return;
        }

        var job = await dbContext.ExtractionJobs
            .SingleOrDefaultAsync(j => j.TenantId == tenantId && j.Id == jobId, cancellationToken)
            .ConfigureAwait(false);
        var document = await dbContext.Documents
            .SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == documentId, cancellationToken)
            .ConfigureAwait(false);

        if (job is null || document is null)
        {
            // The document was deleted between the upload and this delivery (R-DOC-10 removes the
            // job rows with it). Nothing to process and nothing to record.
            logger.LogInformation("Document {DocumentId} or job {JobId} no longer exists; skipping", documentId.Value, jobId.Value);
            return;
        }

        document.ProcessingStatus = DocumentProcessingStatus.Processing;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var bytes = await storage.LoadAsync(tenantId, document.StoragePath, cancellationToken).ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0)
        {
            await FailTerminalAsync(document, job, "The stored bytes could not be read back from object storage.", cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var decision = await admissionGate
            .EvaluateAsync(tenantId, WorkerActor, document.FileName, document.MimeType, bytes, cancellationToken)
            .ConfigureAwait(false);

        switch (decision.Outcome)
        {
            case AdmissionOutcome.Failed when decision.Error?.StartsWith(
                DocumentAdmissionGate.GatewayUnavailablePrefix, StringComparison.Ordinal) == true:
                await ReleaseOrFailAsync(document, job, decision.Error, cancellationToken).ConfigureAwait(false);
                return;

            case AdmissionOutcome.Failed:
                await FailTerminalAsync(document, job, decision.Error ?? "The document could not be read.", cancellationToken)
                    .ConfigureAwait(false);
                return;

            case AdmissionOutcome.Rejected:
                await RejectAsync(document, job, decision, cancellationToken).ConfigureAwait(false);
                return;
        }

        var result = await processingPipeline
            .ProcessAsync(
                tenantId,
                documentId,
                decision.Pages,
                decision.Classification!,
                bytes,
                document.FileName,
                document.MimeType,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            // The pipeline records its own stage failure on the job and the document; this log line
            // is the operator's pointer to it, not a second write.
            logger.LogWarning(
                "Pipeline reported a failure for document {DocumentId}: {Error}", documentId.Value, result.Error);
        }
    }

    private async Task RejectAsync(
        Document document, ExtractionJob job, AdmissionDecision decision, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        document.ProcessingStatus = DocumentProcessingStatus.Rejected;
        document.RejectionReason = decision.Reason;
        document.RejectionDetectedType = decision.DetectedType;
        document.RejectionConfidence = decision.Confidence;

        // Classification did run and did reach a verdict — "not a contract" is a completed
        // classification, not a failed one. The job is terminal either way.
        job.Status = ExtractionJobStatus.Completed;
        job.StartedAt ??= now;
        job.CompletedAt = now;
        job.ErrorDetail = null;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The row is the record; the bytes have no further use (ADR-027 §D6: "the blob is
        // deleted"). Deleting after the commit means a crash between the two leaves a stray
        // blob, never a row that points at nothing — the safer of the two orderings.
        await storage.DeleteAsync(document.TenantId, document.StoragePath, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Document {DocumentId} rejected by the content gate: {Reason} ({DetectedType}, {Confidence:P0})",
            document.Id.Value, decision.Reason, decision.DetectedType, decision.Confidence);
    }

    private async Task FailTerminalAsync(
        Document document, ExtractionJob job, string error, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        document.ProcessingStatus = DocumentProcessingStatus.Failed;
        job.Status = ExtractionJobStatus.Failed;
        job.StartedAt ??= now;
        job.CompletedAt = now;
        job.ErrorDetail = error;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogWarning("Document {DocumentId} failed terminally: {Error}", document.Id.Value, error);
    }

    /// <summary>
    /// A transient failure: hand the job back to the queue for the broker's redelivery — unless
    /// this was already the last permitted attempt, in which case the row goes terminal here and
    /// the message is completed, never dead-lettered (ADR-027 §D3).
    /// </summary>
    private async Task ReleaseOrFailAsync(
        Document document, ExtractionJob job, string error, CancellationToken cancellationToken)
    {
        if (job.AttemptCount >= MaxAttempts)
        {
            await FailTerminalAsync(
                document, job, $"Gave up after {job.AttemptCount} attempts. Last error: {error}", cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // Back to the exact state a fresh claim requires: Queued, unclaimed. AttemptCount keeps
        // counting — that is the bound. The status returns to Uploaded so the list stops saying
        // "Processing" for a document nothing is currently processing.
        job.Status = ExtractionJobStatus.Queued;
        job.ClaimedAt = null;
        job.ClaimedBy = null;
        job.ErrorDetail = error;
        document.ProcessingStatus = DocumentProcessingStatus.Uploaded;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogWarning(
            "Transient failure on document {DocumentId} (attempt {Attempt}/{Max}); released for redelivery: {Error}",
            document.Id.Value, job.AttemptCount, MaxAttempts, error);
        throw new ExtractionTransientException(error);
    }
}
