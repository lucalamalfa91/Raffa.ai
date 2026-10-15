using Microsoft.EntityFrameworkCore;
using Raffa.Quotes.Application.Extraction;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;

namespace Raffa.Api.Tests.TestSupport;

/// <summary>
/// Test double of <see cref="IQuoteExtractionJobClaimStore"/> for hosts on the EF in-memory provider,
/// which has no <c>ExecuteUpdate</c>. Same rules as <see cref="QuoteExtractionJobClaimStore"/>
/// (queued, or running and taken over by a redelivery), written as read-modify-write: atomicity is
/// proved against Postgres in <c>Raffa.Quotes.Tests</c>, not here. The same pattern as
/// <see cref="InMemoryExtractionJobClaimStore"/> for documents.
/// </summary>
internal sealed class InMemoryQuoteExtractionJobClaimStore(QuotesDbContext dbContext, IClock clock)
    : IQuoteExtractionJobClaimStore
{
    public async Task<QuoteJobClaimResult> TryClaimAsync(
        TenantId tenantId, EntityId quoteId, EntityId jobId, long deliveryCount, CancellationToken cancellationToken)
    {
        var job = await dbContext.QuoteExtractionJobs
            .SingleOrDefaultAsync(j => j.TenantId == tenantId && j.Id == jobId && j.QuoteId == quoteId, cancellationToken);
        if (job is null)
        {
            return QuoteJobClaimResult.NotFound;
        }

        var now = clock.UtcNow;
        var takeover = deliveryCount > 1
            && job.Status == QuoteExtractionJobStatus.Running
            && job.StartedAt is { } started
            && started < now - IQuoteExtractionJobClaimStore.TakeoverMinimumAge;
        if (job.Status != QuoteExtractionJobStatus.Queued && !takeover)
        {
            return QuoteJobClaimResult.Lost;
        }

        job.Status = QuoteExtractionJobStatus.Running;
        job.StartedAt = now;
        job.CompletedAt = null;
        job.ErrorDetail = null;
        var quote = await dbContext.Quotes
            .SingleOrDefaultAsync(q => q.TenantId == tenantId && q.Id == quoteId, cancellationToken);
        if (quote is { ProcessingStatus: QuoteProcessingStatus.Uploaded or QuoteProcessingStatus.Processing })
        {
            quote.ProcessingStatus = QuoteProcessingStatus.Processing;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return QuoteJobClaimResult.Claimed;
    }

    public async Task ReleaseAsync(
        TenantId tenantId, EntityId quoteId, EntityId jobId, CancellationToken cancellationToken)
    {
        var job = await dbContext.QuoteExtractionJobs
            .SingleOrDefaultAsync(j => j.TenantId == tenantId && j.Id == jobId && j.QuoteId == quoteId, cancellationToken);
        if (job is { Status: QuoteExtractionJobStatus.Running or QuoteExtractionJobStatus.Failed })
        {
            job.Status = QuoteExtractionJobStatus.Queued;
            job.StartedAt = null;
            job.CompletedAt = null;
            job.ErrorDetail = null;
        }

        var quote = await dbContext.Quotes
            .SingleOrDefaultAsync(q => q.TenantId == tenantId && q.Id == quoteId, cancellationToken);
        if (quote is { ProcessingStatus: QuoteProcessingStatus.Processing or QuoteProcessingStatus.Failed })
        {
            quote.ProcessingStatus = QuoteProcessingStatus.Uploaded;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
    }
}
