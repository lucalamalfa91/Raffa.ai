using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Raffa.Quotes.Application.Extraction;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Jobs;
using Raffa.SharedKernel.Storage;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiFlows.QuoteExtraction.Orchestration;

/// <summary>
/// Task F6-T01: the Worker's side of quote extraction. One delivery of a <see cref="JobRequested"/> of
/// kind <see cref="JobKinds.QuoteExtraction"/> becomes one run of <see cref="QuoteExtractionPipeline"/>,
/// with no HTTP request involved: claim the job (compare-and-swap, so a duplicate or racing delivery
/// does nothing), read the stored bytes back from object storage, run the pipeline, done.
///
/// <list type="bullet">
/// <item>The message carries ids only. Everything else -- file name, MIME type, storage path -- is read
/// from the quote row inside the tenant scope the message names (ADR-009 w15 footer), and a tenant id
/// that is not a valid, non-empty Guid is dropped before any scope is entered.</item>
/// <item>The pipeline always leaves the job terminal (task F6-T02), so this handler never throws for a
/// failed extraction: the failure is data on the row, and the message is completed.</item>
/// <item>The one exception is the host shutting down mid-run. That is not the quote's failure: the
/// job goes back to <c>Queued</c> and the cancellation is rethrown, so the broker redelivers the
/// message to another replica instead of the quote ending <c>Failed</c> on every deploy.</item>
/// </list>
/// </summary>
public sealed class QuoteExtractionJobHandler(
    IQuoteExtractionJobClaimStore claimStore,
    QuotesDbContext dbContext,
    IDocumentStorage storage,
    QuoteExtractionPipeline pipeline,
    ITenantContext tenantContext,
    ILogger<QuoteExtractionJobHandler>? logger = null) : IJobHandler
{
    public string Kind => JobKinds.QuoteExtraction;

    public async Task<JobHandleOutcome> HandleAsync(
        JobRequested message, long deliveryCount, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.TenantId == Guid.Empty || message.ResourceId == Guid.Empty || message.JobId == Guid.Empty)
        {
            // Nothing to scope to and no row to record anything on: complete it, never retry it.
            logger?.LogError("Quote extraction message {JobId} has an empty id; dropping it.", message.JobId);
            return JobHandleOutcome.Handled;
        }

        var tenantId = new TenantId(message.TenantId);
        var quoteId = new EntityId(message.ResourceId);
        var jobId = new EntityId(message.JobId);

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var claim = await claimStore
            .TryClaimAsync(tenantId, quoteId, jobId, deliveryCount, cancellationToken)
            .ConfigureAwait(false);
        switch (claim)
        {
            case QuoteJobClaimResult.NotFound:
                return JobHandleOutcome.JobNotFound;
            case QuoteJobClaimResult.Lost:
                return JobHandleOutcome.ClaimLost;
        }

        var quote = await dbContext.Quotes.AsNoTracking()
            .Where(q => q.TenantId == tenantId && q.Id == quoteId)
            .Select(q => new { q.FileName, q.MimeType, q.StoragePath })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (quote is null)
        {
            await pipeline
                .FailAsync(tenantId, quoteId, QuoteExtractionFailureKind.SourceMissing, $"Quote {quoteId} was not found for this tenant.")
                .ConfigureAwait(false);
            return JobHandleOutcome.Handled;
        }

        byte[]? bytes;
        try
        {
            bytes = await storage.LoadAsync(tenantId, quote.StoragePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A path outside the tenant prefix (the guard throws) or a storage outage: terminal for this
            // job either way, recorded on the row. Reprocess reads the same bytes again.
            logger?.LogError(exception, "Could not read the stored file of quote {QuoteId}.", quoteId);
            await pipeline
                .FailAsync(tenantId, quoteId, QuoteExtractionFailureKind.SourceMissing, $"{exception.GetType().Name}: {exception.Message}")
                .ConfigureAwait(false);
            return JobHandleOutcome.Handled;
        }

        if (bytes is null || bytes.Length == 0)
        {
            await pipeline
                .FailAsync(tenantId, quoteId, QuoteExtractionFailureKind.SourceMissing, "The stored file could not be read back from object storage.")
                .ConfigureAwait(false);
            return JobHandleOutcome.Handled;
        }

        await pipeline
            .ProcessAsync(tenantId, quoteId, quote.FileName, quote.MimeType, bytes, cancellationToken, jobId)
            .ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
        {
            // The pipeline recorded the cancellation as a failure (task F6-T02). A host shutdown is not
            // the quote's fault: give the job back and let the broker deliver it again.
            await claimStore.ReleaseAsync(tenantId, quoteId, jobId, CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return JobHandleOutcome.Handled;
    }
}
