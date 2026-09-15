using Raffa.Documents.Contracts.Application.Admission;
using Raffa.Documents.Contracts.Application.Preview;
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
/// The Worker's <b>intake</b> handler (instant-identity-ingest, ADR-027 §D1–§D3, §D6).
/// One <see cref="ExtractionRequested"/> pointer in; outcome:
/// <list type="number">
/// <item><b>Claim.</b> Compare-and-swap via <see cref="IExtractionJobClaimStore.TryClaimAsync"/>.</item>
/// <item><b>Content gate.</b> Parse/OCR + classify + threshold. Refusals are recorded as
/// <see cref="DocumentProcessingStatus.Rejected"/>; admitted documents continue below.</item>
/// <item><b>Headline pass.</b> ONE <see cref="IAiGateway.ExtractAsync"/> call for
/// supplier/type/status/dates — sets provisional identity on the contract shell within seconds.
/// <see cref="HeadlineExtractionService"/> always persists <c>ProvisionalSupplierName</c>
/// regardless of confidence.</item>
/// <item><b>Enqueue enrich.</b> Publishes <see cref="EnrichRequested"/> so the slow 7-stage
/// <see cref="StagedExtractionService"/> run happens under its own Service Bus lock, not this
/// message's lock (two-queue split).</item>
/// </list>
/// <para>
/// <b>Transient vs terminal.</b> A gateway that could not be reached is transient:
/// <see cref="ExtractionTransientException"/> tells the consumer to abandon.
/// <see cref="MaxAttempts"/> deliveries later the row goes
/// <see cref="DocumentProcessingStatus.Failed"/> (ADR-027 §D3).
/// </para>
/// </summary>
public sealed class ExtractionRequestedHandler(
    DocumentsContractsDbContext dbContext,
    IDocumentStorage storage,
    DocumentAdmissionGate admissionGate,
    HeadlineExtractionService headlineService,
    IEnrichQueuePublisher enrichQueuePublisher,
    IExtractionJobClaimStore claimStore,
    ITenantContext tenantContext,
    IClock clock,
    ILogger<ExtractionRequestedHandler> logger,
    DocumentPreviewService? previewService = null)
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
            logger.LogInformation(
                "Extraction job {JobId} for document {DocumentId} was not claimable (duplicate delivery, already claimed, or no longer queued); nothing to do",
                jobId.Value, documentId.Value);

            // ADR-027 §C6 (fix 2026-09-14): a lost claim is two different things. The row EXISTS and
            // someone else holds or finished it -- a duplicate delivery, complete it. The row does NOT
            // exist -- the upload's commit was slower than its publish, so this delivery arrived before
            // the row became visible; completing it would strand the document at Uploaded on a POST
            // that returned 201. The consumer settles that case by DeliveryCount.
            var rowExists = await dbContext.ExtractionJobs
                .AnyAsync(j => j.TenantId == tenantId && j.Id == jobId, cancellationToken)
                .ConfigureAwait(false);
            return rowExists ? ExtractionHandleOutcome.ClaimLost : ExtractionHandleOutcome.JobNotFound;
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
            return ExtractionHandleOutcome.JobNotFound;
        }

        document.ProcessingStatus = DocumentProcessingStatus.Processing;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var bytes = await storage.LoadAsync(tenantId, document.StoragePath, cancellationToken).ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0)
        {
            await FailTerminalAsync(document, job, "The stored bytes could not be read back from object storage.", cancellationToken)
                .ConfigureAwait(false);
            return ExtractionHandleOutcome.Handled;
        }

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

        // Admitted. Advance the classification job now (mirroring the former pipeline path).
        var classificationJob = await dbContext.ExtractionJobs
            .Where(j => j.TenantId == tenantId
                && j.DocumentId == document.Id
                && j.Stage == ExtractionStage.Classification
                && j.Status == ExtractionJobStatus.Running)
            .OrderByDescending(j => j.QueuedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = clock.UtcNow;
        document.DocumentType = decision.Classification!.DocumentType;
        document.PageCount = decision.Pages.Count;

        if (classificationJob is not null)
        {
            classificationJob.StartedAt ??= now;
            classificationJob.ModelId = decision.Classification.Metadata.ModelId;
            classificationJob.Status = decision.Classification.Confidence < 0.6
                ? ExtractionJobStatus.NeedsReview
                : ExtractionJobStatus.Completed;
            classificationJob.CompletedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Fast headline pass: one LLM call that sets provisional supplier/type/status/dates.
        // Non-fatal: if this fails, the contract stays provisional with the filename as identity.
        await headlineService
            .RunAsync(tenantId, document, decision.Pages, decision.Classification.DocumentType, cancellationToken)
            .ConfigureAwait(false);

        // R-DOC-08: render and store the first-page preview from the bytes we already hold.
        // Best-effort (non-fatal) — DocumentPreviewService returns null instead of throwing;
        // a document with no preview simply answers 404 on its preview endpoint.
        if (previewService is not null && bytes.Length > 0)
        {
            var previewPath = await previewService
                .RenderAndStoreAsync(tenantId, documentId, document.FileName, document.MimeType, bytes, cancellationToken)
                .ConfigureAwait(false);

            if (previewPath is not null)
            {
                document.PreviewPath = previewPath;
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        // Publish enrich so the 7-stage StagedExtractionService runs under its own lock.
        await enrichQueuePublisher.PublishAsync(
            new EnrichRequested(tenantId.Value, documentId.Value, EnrichRequested.CurrentSchemaVersion),
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Intake complete for document {DocumentId}; EnrichRequested published", documentId.Value);

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
