using Microsoft.EntityFrameworkCore;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Jobs;
using Raffa.SharedKernel.Storage;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Quotes.Application;

/// <summary>
/// Task F6-T03 (reprocess): re-queues the extraction of one already-stored quote -- same bytes, a
/// fresh <see cref="QuoteExtractionJob"/> -- so a <c>Failed</c> or <c>NeedsReview</c> quote can be
/// run again without a second upload. Mirrors what <c>DocumentReprocessService</c> does for
/// documents.
///
/// <para>
/// <b>Replace, never add.</b> The re-run does not append to the quote's lines: the pipeline swaps
/// the previous lines for the new ones in one transaction
/// (<c>QuoteExtractionPipeline</c>), so a reprocess twice in a row leaves exactly one set of lines
/// and a failed re-run leaves the previous lines untouched. This service itself only prepares the
/// run; it does not extract.
/// </para>
///
/// <para>
/// <b>One run at a time.</b> A quote with a job that is <c>Queued</c> or <c>Running</c> is refused,
/// unless that job is older than <see cref="StaleJobAge"/>: a run that long can only be an orphan
/// of a crashed process, and it is closed as <c>Failed</c> ("superseded") rather than blocking the
/// quote for ever.
/// </para>
///
/// <para>
/// With a durable queue configured (task F6-T01) the job is published and the Worker runs it; the
/// request returns at once. Admin-only is the caller's rule (the endpoint), not this service's, as for documents.
/// </para>
/// </summary>
public sealed class QuoteReprocessService(
    QuotesDbContext dbContext,
    IDocumentStorage storage,
    ITenantContext tenantContext,
    IAuditWriter auditWriter,
    IClock clock,
    IJobQueuePublisher? jobQueuePublisher = null)
{
    /// <summary>Audit action for a re-run that was queued.</summary>
    public const string ReprocessedAuditAction = "quote.reprocess.queued";

    /// <summary>An open job older than this is treated as orphaned by a crashed process. Longer
    /// than any extraction run (parse + OCR + one model call) can legitimately take.</summary>
    public static readonly TimeSpan StaleJobAge = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Returns <see langword="null"/> when no such quote exists for this tenant (the endpoint's
    /// 404), a failure when the quote cannot be re-run now, otherwise the queued job and the
    /// stored bytes.
    /// </summary>
    public async Task<Result<QuoteReprocessQueued>?> ReprocessAsync(
        TenantId tenantId,
        EntityId quoteId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var quote = await dbContext.Quotes
            .SingleOrDefaultAsync(q => q.TenantId == tenantId && q.Id == quoteId, cancellationToken)
            .ConfigureAwait(false);
        if (quote is null)
        {
            return null;
        }

        var now = clock.UtcNow;
        var openJobs = await dbContext.QuoteExtractionJobs
            .Where(j => j.TenantId == tenantId
                && j.QuoteId == quoteId
                && (j.Status == QuoteExtractionJobStatus.Queued || j.Status == QuoteExtractionJobStatus.Running))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (openJobs.Any(j => now - (j.StartedAt ?? j.QueuedAt) < StaleJobAge))
        {
            return Result<QuoteReprocessQueued>.Failure("This quote is still being processed; try again when it has finished.");
        }

        var bytes = await storage.LoadAsync(tenantId, quote.StoragePath, cancellationToken).ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0)
        {
            return Result<QuoteReprocessQueued>.Failure(
                $"The stored bytes for quote {quoteId} could not be read back from object storage.");
        }

        foreach (var orphan in openJobs)
        {
            orphan.Status = QuoteExtractionJobStatus.Failed;
            orphan.StartedAt ??= now;
            orphan.CompletedAt = now;
            orphan.ErrorDetail = "[Unexpected] Superseded by a reprocess; the previous run never finished.";
        }

        var job = new QuoteExtractionJob
        {
            TenantId = tenantId,
            QuoteId = quoteId,
            Status = QuoteExtractionJobStatus.Queued,
            QueuedAt = now,
        };
        dbContext.QuoteExtractionJobs.Add(job);
        quote.ProcessingStatus = QuoteProcessingStatus.Uploaded;

        // Task F6-T01: with a durable queue the Worker runs the job; publish before the commit, as the
        // upload does. Without one the caller runs it inline and gets the bytes for that.
        var queued = jobQueuePublisher is { IsDurable: true };
        if (queued)
        {
            await jobQueuePublisher!.PublishAsync(
                new JobRequested(
                    JobKinds.QuoteExtraction,
                    tenantId.Value,
                    quoteId.Value,
                    job.Id.Value,
                    JobRequested.CurrentSchemaVersion),
                cancellationToken).ConfigureAwait(false);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                actor,
                ReprocessedAuditAction,
                "quote",
                quoteId.Value.ToString(),
                now,
                $"extractionJobId={job.Id.Value}"),
            cancellationToken).ConfigureAwait(false);

        return Result<QuoteReprocessQueued>.Success(new QuoteReprocessQueued(
            quoteId, job.Id, quote.FileName, quote.MimeType, queued ? [] : bytes, queued));
    }
}

/// <summary>The queued re-run: which quote, which job the pipeline will advance, whether the Worker
/// will run it (<paramref name="ExtractionQueued"/>), and otherwise the stored bytes the caller's
/// inline run needs (empty when queued: the Worker reads them itself).</summary>
public sealed record QuoteReprocessQueued(
    EntityId QuoteId,
    EntityId ExtractionJobId,
    string FileName,
    string MimeType,
    byte[] Bytes,
    bool ExtractionQueued = false);
