using Microsoft.EntityFrameworkCore;
using Raffa.Quotes.Application.Extraction;
using Raffa.Quotes.Domain;
using Raffa.SharedKernel;

namespace Raffa.Quotes.Infrastructure;

/// <summary>
/// EF Core implementation of <see cref="IQuoteExtractionJobClaimStore"/>: set-based
/// <c>ExecuteUpdate</c> statements, so atomicity comes from Postgres's row lock on the <c>UPDATE</c>
/// and not from EF's change tracking. Every statement also filters on the tenant, beside RLS
/// (ADR-009). Needs a relational provider; test hosts on the in-memory provider register their own
/// double.
/// </summary>
public sealed class QuoteExtractionJobClaimStore(QuotesDbContext dbContext, IClock clock)
    : IQuoteExtractionJobClaimStore
{
    public async Task<QuoteJobClaimResult> TryClaimAsync(
        TenantId tenantId,
        EntityId quoteId,
        EntityId jobId,
        long deliveryCount,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var takeoverBefore = now - IQuoteExtractionJobClaimStore.TakeoverMinimumAge;
        var isRedelivery = deliveryCount > 1;

        var claimed = await dbContext.QuoteExtractionJobs
            .Where(j => j.TenantId == tenantId
                && j.Id == jobId
                && j.QuoteId == quoteId
                && (j.Status == QuoteExtractionJobStatus.Queued
                    || (isRedelivery
                        && j.Status == QuoteExtractionJobStatus.Running
                        && j.StartedAt != null
                        && j.StartedAt < takeoverBefore)))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(j => j.Status, QuoteExtractionJobStatus.Running)
                    .SetProperty(j => j.StartedAt, (DateTimeOffset?)now)
                    .SetProperty(j => j.CompletedAt, (DateTimeOffset?)null)
                    .SetProperty(j => j.ErrorDetail, (string?)null),
                cancellationToken)
            .ConfigureAwait(false);

        if (claimed == 1)
        {
            // Visible progress for the list: the quote is being worked on. Never touches a quote that
            // already reached a terminal state.
            await dbContext.Quotes
                .Where(q => q.TenantId == tenantId
                    && q.Id == quoteId
                    && (q.ProcessingStatus == QuoteProcessingStatus.Uploaded
                        || q.ProcessingStatus == QuoteProcessingStatus.Processing))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(q => q.ProcessingStatus, QuoteProcessingStatus.Processing),
                    cancellationToken)
                .ConfigureAwait(false);
            return QuoteJobClaimResult.Claimed;
        }

        var exists = await dbContext.QuoteExtractionJobs.AsNoTracking()
            .AnyAsync(j => j.TenantId == tenantId && j.Id == jobId && j.QuoteId == quoteId, cancellationToken)
            .ConfigureAwait(false);
        return exists ? QuoteJobClaimResult.Lost : QuoteJobClaimResult.NotFound;
    }

    public async Task ReleaseAsync(
        TenantId tenantId, EntityId quoteId, EntityId jobId, CancellationToken cancellationToken)
    {
        await dbContext.QuoteExtractionJobs
            .Where(j => j.TenantId == tenantId
                && j.Id == jobId
                && j.QuoteId == quoteId
                && (j.Status == QuoteExtractionJobStatus.Running || j.Status == QuoteExtractionJobStatus.Failed))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(j => j.Status, QuoteExtractionJobStatus.Queued)
                    .SetProperty(j => j.StartedAt, (DateTimeOffset?)null)
                    .SetProperty(j => j.CompletedAt, (DateTimeOffset?)null)
                    .SetProperty(j => j.ErrorDetail, (string?)null),
                cancellationToken)
            .ConfigureAwait(false);

        await dbContext.Quotes
            .Where(q => q.TenantId == tenantId
                && q.Id == quoteId
                && (q.ProcessingStatus == QuoteProcessingStatus.Processing
                    || q.ProcessingStatus == QuoteProcessingStatus.Failed))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(q => q.ProcessingStatus, QuoteProcessingStatus.Uploaded),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
