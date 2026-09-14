using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Api.Tests.TestSupport;

/// <summary>
/// The claim primitive for the EF InMemory host (task E16/F02/US03/T01). The real
/// <see cref="ExtractionJobClaimStore"/> is one conditional <c>UPDATE</c> — atomic by Postgres's
/// row lock — which the InMemory provider cannot execute at all (no relational SQL). This is the
/// same predicate expressed as a tracked read-modify-write: correct for the single-threaded
/// drain the tests perform, and deliberately <em>not</em> a claim the concurrency proof relies on
/// (<c>Raffa.Documents.Contracts.Tests</c>' claim-race tests keep that on real Postgres).
/// </summary>
internal sealed class InMemoryExtractionJobClaimStore(DocumentsContractsDbContext dbContext, IClock clock)
    : IExtractionJobClaimStore
{
    public async Task<int> TryClaimAsync(EntityId jobId, string claimedBy, CancellationToken cancellationToken)
    {
        var job = await dbContext.ExtractionJobs
            .SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken)
            .ConfigureAwait(false);

        if (job is null || job.Status != ExtractionJobStatus.Queued || job.ClaimedAt is not null)
        {
            return 0;
        }

        job.ClaimedAt = clock.UtcNow;
        job.ClaimedBy = claimedBy;
        job.AttemptCount += 1;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return 1;
    }
}
