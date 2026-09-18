using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Infrastructure;

/// <summary>
/// The conditional-<c>UPDATE</c> claim that turns Service Bus's at-least-once delivery into
/// exactly-once work (ADR-027 §D3, task E16/F02/US01/T01). A claim is one atomic statement: it
/// succeeds only while the job is still <see cref="ExtractionJobStatus.Queued"/> and unclaimed, so
/// a duplicate delivery, a redelivery racing the original, or a second worker instance all see
/// zero rows affected and do nothing — the database, not the caller, decides who won.
///
/// us-02's message handler (task E16/F02/US02/T01) is the first real caller. A stale claim is
/// not reopened here: <see cref="HungProcessingRecoveryService"/> aborts the zombie and
/// re-enqueues through <see cref="DocumentReprocessService"/> instead, so a hang cannot leave
/// the row on Processing forever.
/// </summary>
public interface IExtractionJobClaimStore
{
    /// <summary>
    /// Attempts to claim <paramref name="jobId"/>: sets <c>claimed_at</c>/<c>claimed_by</c> and
    /// increments <c>attempt_count</c> iff the row is still <see cref="ExtractionJobStatus.Queued"/>
    /// and has never been claimed. Returns the number of rows affected by the <c>UPDATE</c> — 1 on
    /// a win, 0 on a loss (already claimed, already terminal, or no such row).
    /// </summary>
    Task<int> TryClaimAsync(EntityId jobId, string claimedBy, CancellationToken cancellationToken);
}

public sealed class ExtractionJobClaimStore(DocumentsContractsDbContext dbContext, IClock clock)
    : IExtractionJobClaimStore
{
    public Task<int> TryClaimAsync(EntityId jobId, string claimedBy, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var queuedStatus = ExtractionJobStatus.Queued.ToString();

        // Raw, parameterised SQL — not a tracked-entity SaveChanges — because atomicity here comes
        // from Postgres's own row lock on the UPDATE, not from EF's optimistic concurrency. The
        // "claimed_at IS NULL" guard is what makes this a compare-and-swap: the first concurrent
        // caller to commit flips it away from NULL, so a second caller's WHERE re-evaluates false
        // and it affects zero rows, even though this statement never touches `status` itself.
        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE extraction_job
               SET claimed_at = {now}, claimed_by = {claimedBy}, attempt_count = attempt_count + 1
             WHERE id = {jobId.Value} AND status = {queuedStatus} AND claimed_at IS NULL
            """,
            cancellationToken);
    }
}
