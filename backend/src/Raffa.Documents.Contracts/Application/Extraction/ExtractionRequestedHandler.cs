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
/// redelivery racing the original, a second replica, or a job that is no longer queued. A live
/// duplicate is completed. A stale in-flight claim (the original worker died holding it) is
/// aborted and re-enqueued from scratch by <see cref="HungProcessingRecoveryService"/> so the
/// document cannot sit on Processing forever.</item>
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
    ILogger<ExtractionRequestedHandler> logger,
    HungProcessingRecoveryService? hungRecovery = null,
    IExtractionRunAborter? runAborter = null,
    IExtractionHangWatch? hangWatch = null,
    ExtractionProgressHeartbeat? progressHeartbeat = null)
{
    /// <summary>Deliveries a job may consume before its row is marked terminal (ADR-027 §D3).</summary>
    public const int MaxAttempts = 3;

    /// <summary>The actor recorded on the gate's rejection audit row — the Worker, not a person.</summary>
    public const string WorkerActor = "system:worker";

    private static readonly string ClaimedBy = Environment.MachineName;

    /// <summary>ADR-027 w15 footer C12: how many prioritised jobs one delivery may take ahead of its
    /// own. One, deliberately: with three replicas x four calls in flight, one per delivery already
    /// lets a dozen opened documents start at once, and every extra job a delivery carries widens the
    /// message-lock and eviction exposure (a delivery holds its lock for the whole run).</summary>
    public const int MaxPrioritisedPerDelivery = 1;

    public async Task<ExtractionHandleOutcome> HandleAsync(ExtractionRequested message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var tenantId = new TenantId(message.TenantId);
        var documentId = new EntityId(message.DocumentId);
        var jobId = new EntityId(message.ExtractionJobId);

        using var tenantScope = tenantContext.BeginScope(tenantId);

        // ADR-027 w15 footer C12 (task E16/F03/US02/T01, 2026-09-14): the queue-jump. Service Bus is
        // FIFO and this wave keeps exactly one subscription (OQ-w15-012), so a document the user has
        // opened while it was still queued cannot move in the broker -- it moves here. Before this
        // delivery claims its own job it looks, inside its own tenant scope only (ADR-009), for a
        // classification job somebody is waiting for, claims it with the same compare-and-swap every
        // delivery uses, and runs it first. The prioritised job's own message arrives later, loses the
        // claim to a row that exists, and is completed (C6). Work is conserved: nothing is done
        // twice, the FIFO resumes right after, the reordering is the whole effect.
        //
        // ADR-027 w15 footer C12, fix 2026-09-14 (post-deploy): this whole block is best-effort, the
        // same posture the web side already takes toward the priority call it fires ("an optimisation,
        // never a promise the screen has to keep") -- extended here to the Worker side, which the
        // original footer left unguarded. A message's own job must NEVER fail because the queue-jump
        // lookup itself failed (a transient query error, a timeout, anything not yet named): every
        // delivery, prioritised or not, runs `dbContext.ExtractionJobs...` once here, so a single bad
        // query would have stalled the *entire tenant's* queue at `Uploaded` forever, exactly the
        // silent-stranding failure ADR-027 §C6 exists to bound -- caused this time by the optimisation
        // meant to help, not by the pipeline itself.
        try
        {
            var prioritised = await dbContext.ExtractionJobs
                .AsNoTracking()
                .Where(j => j.TenantId == tenantId
                    && j.Stage == ExtractionStage.Classification
                    && j.Status == ExtractionJobStatus.Queued
                    && j.ClaimedAt == null
                    && j.PrioritisedAt != null
                    && j.Id != jobId)
                .OrderBy(j => j.PrioritisedAt)
                .ThenBy(j => j.QueuedAt)
                .Take(MaxPrioritisedPerDelivery)
                .Select(j => new { j.Id, j.DocumentId })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var prioritisedJob in prioritised)
            {
                try
                {
                    var outcome = await ProcessOneAsync(tenantId, prioritisedJob.DocumentId, prioritisedJob.Id, cancellationToken)
                        .ConfigureAwait(false);
                    logger.LogInformation(
                        "Prioritised job {PrioritisedJobId} handled ahead of job {JobId}: {Outcome}",
                        prioritisedJob.Id.Value, jobId.Value, outcome);
                }
                catch (ExtractionTransientException exception)
                {
                    // ReleaseOrFailAsync already put the row back to queued/unclaimed (still prioritised),
                    // so the next delivery in this tenant, or its own message, retries it. This delivery's
                    // own message must not pay for it: no abandon, no delivery count burnt.
                    logger.LogWarning(
                        "Prioritised job {PrioritisedJobId} released after a transient failure; continuing with job {JobId}: {Error}",
                        prioritisedJob.Id.Value, jobId.Value, exception.Message);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // The claim stays held and the row stays Processing for an operator to see -- the same
                    // honest posture the consumer documents for an unexpected crash on a delivery's own job.
                    logger.LogError(
                        exception, "Prioritised job {PrioritisedJobId} failed unexpectedly; continuing with job {JobId}",
                        prioritisedJob.Id.Value, jobId.Value);
                }
                finally
                {
                    // Every step below re-queries by id, and the gate, the pipeline and the claim store share
                    // this one scoped DbContext: nothing a half-written prioritised job left tracked may leak
                    // into the own job's SaveChanges.
                    dbContext.ChangeTracker.Clear();
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The lookup itself failed -- nothing was claimed, nothing was processed, nothing to roll
            // back. Log it and fall straight through to the delivery's own job below: a broken
            // optimisation must degrade to "no queue-jump this delivery", never to "no processing at
            // all". `ChangeTracker.Clear()` is safe even though nothing here tracked anything.
            logger.LogError(exception, "Prioritised-job lookup failed for job {JobId}; continuing without a queue-jump", jobId.Value);
            dbContext.ChangeTracker.Clear();
        }

        // The delivery's own job, with the semantics it always had: an exception here reaches the
        // transport and follows the settlement rules (ExtractionSettlement).
        return await ProcessOneAsync(tenantId, documentId, jobId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One job, start to finish: claim, load, gate, pipeline, terminal state. The body every
    /// delivery ran inline until ADR-027 w15 footer C12 made a delivery able to run a prioritised job
    /// before its own. Assumes the tenant scope is already open.</summary>
    private async Task<ExtractionHandleOutcome> ProcessOneAsync(
        TenantId tenantId, EntityId documentId, EntityId jobId, CancellationToken cancellationToken)
    {
        var claimed = await claimStore.TryClaimAsync(jobId, ClaimedBy, cancellationToken).ConfigureAwait(false);
        if (claimed == 0)
        {
            return await OnClaimLostAsync(tenantId, documentId, jobId, cancellationToken).ConfigureAwait(false);
        }

        var workToken = hangWatch?.Start(cancellationToken) ?? cancellationToken;
        var runToken = runAborter?.Register(jobId.Value, workToken) ?? workToken;
        try
        {
            return await RunClaimedAsync(tenantId, documentId, jobId, runToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Inactivity watch or hang recovery cancelled this run. Abort the zombie and
            // re-enqueue from scratch (or fail after MaxAttempts) — never complete a ClaimLost
            // no-op that would leave the row on Processing forever.
            logger.LogWarning(
                "Extraction job {JobId} for document {DocumentId} was aborted after making no progress; recovering from scratch",
                jobId.Value, documentId.Value);
            await RecoverHungAsync(tenantId, documentId).ConfigureAwait(false);
            return ExtractionHandleOutcome.Handled;
        }
        catch (ExtractionTransientException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The consumer used to Abandon with the claim still held; the redelivery then lost
            // the claim and completed the message, stranding the document at Processing. Recover
            // here instead: abort, then POST-reprocess equivalent, so a crash cannot hang forever.
            logger.LogError(
                exception,
                "Unhandled failure processing document {DocumentId}; aborting the run and re-enqueueing from scratch",
                documentId.Value);
            await RecoverHungAsync(tenantId, documentId).ConfigureAwait(false);
            return ExtractionHandleOutcome.Handled;
        }
        finally
        {
            runAborter?.Unregister(jobId.Value);
        }
    }

    private async Task<ExtractionHandleOutcome> OnClaimLostAsync(
        TenantId tenantId, EntityId documentId, EntityId jobId, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Extraction job {JobId} for document {DocumentId} was not claimable (duplicate delivery, already claimed, or no longer queued)",
            jobId.Value, documentId.Value);

        var rowExists = await dbContext.ExtractionJobs
            .AnyAsync(j => j.TenantId == tenantId && j.Id == jobId, cancellationToken)
            .ConfigureAwait(false);
        if (!rowExists)
        {
            return ExtractionHandleOutcome.JobNotFound;
        }

        // A still-held claim whose last heartbeat is stale means the original worker is gone
        // (process crash after the peek-lock expired) and this redelivery would otherwise be
        // completed as a no-op — the hang that left rows on "Uploading…" forever. Recover from
        // scratch. A fresh in-flight claim (duplicate delivery of a live run) is still a no-op.
        if (hungRecovery is not null)
        {
            var action = await hungRecovery
                .RecoverDocumentAsync(tenantId, documentId, force: false, cancellationToken)
                .ConfigureAwait(false);
            if (action is not HungRecoveryAction.None)
            {
                return ExtractionHandleOutcome.Handled;
            }
        }

        return ExtractionHandleOutcome.ClaimLost;
    }

    private Task RecoverHungAsync(TenantId tenantId, EntityId documentId) =>
        hungRecovery is null
            ? Task.CompletedTask
            : hungRecovery.RecoverDocumentAsync(tenantId, documentId, force: true, CancellationToken.None);

    /// <summary>Claim won: load, mark Processing, gate, pipeline. <paramref name="cancellationToken"/>
    /// is the hang-watch token so a silent stage cancels this run.</summary>
    private async Task<ExtractionHandleOutcome> RunClaimedAsync(
        TenantId tenantId, EntityId documentId, EntityId jobId, CancellationToken cancellationToken)
    {
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
            return ExtractionHandleOutcome.JobNotFound;
        }

        document.ProcessingStatus = DocumentProcessingStatus.Processing;
        // Durable heartbeat so the list reports "Classifying" (not "Uploading") and hang
        // detection has a timestamp while the admission gate runs.
        job.StartedAt ??= clock.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        hangWatch?.Heartbeat();

        using var bound = progressHeartbeat?.Bind(job);
        using var foundryAttempts = progressHeartbeat?.BeginFoundryAttempts();
        using var memoryPulses = progressHeartbeat?.BeginMemoryPulses();

        var bytes = await storage.LoadAsync(tenantId, document.StoragePath, cancellationToken).ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0)
        {
            await FailTerminalAsync(document, job, "The stored bytes could not be read back from object storage.", cancellationToken)
                .ConfigureAwait(false);
            return ExtractionHandleOutcome.Handled;
        }

        hangWatch?.Heartbeat();
        var decision = await admissionGate
            .EvaluateAsync(tenantId, WorkerActor, document.FileName, document.MimeType, bytes, cancellationToken)
            .ConfigureAwait(false);

        switch (decision.Outcome)
        {
            case AdmissionOutcome.Failed when decision.Error?.StartsWith(
                DocumentAdmissionGate.GatewayUnavailablePrefix, StringComparison.Ordinal) == true:
                await ReleaseOrFailAsync(document, job, decision.Error, cancellationToken).ConfigureAwait(false);
                return ExtractionHandleOutcome.Handled;

            case AdmissionOutcome.Failed:
                await FailTerminalAsync(document, job, decision.Error ?? "The document could not be read.", cancellationToken)
                    .ConfigureAwait(false);
                return ExtractionHandleOutcome.Handled;

            case AdmissionOutcome.Rejected:
                await RejectAsync(document, job, decision, cancellationToken).ConfigureAwait(false);
                return ExtractionHandleOutcome.Handled;
        }

        hangWatch?.Heartbeat();
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

        return ExtractionHandleOutcome.Handled;
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

/// <summary>ADR-027 §C6 (fix 2026-09-14): what a delivery meant, so the transport can settle it.
/// <see cref="Handled"/> and <see cref="ClaimLost"/> are both "complete the message"; only
/// <see cref="JobNotFound"/> -- no row at all inside the message's own tenant scope -- is settled by
/// delivery count (abandon below two, dead-letter <c>job-not-found</c> from two).</summary>
public enum ExtractionHandleOutcome
{
    Handled,
    ClaimLost,
    JobNotFound,
}
