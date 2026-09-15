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
/// us-02's message handler (task E16/F02/US02/T01) is the first real caller and will wrap this
/// with the fuller lease/reclaim and status-transition behaviour ADR-027 §D3's own SQL sample
/// describes; this task proves the atomic primitive itself, scoped exactly to what AC-4 asks for.
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
        var runningStatus = ExtractionJobStatus.Running.ToString();

        // Raw, parameterised SQL — not a tracked-entity SaveChanges — because atomicity here comes
        // from Postgres's own row lock on the UPDATE, not from EF's optimistic concurrency. The
        // "claimed_at IS NULL" guard is what makes this a compare-and-swap: the first concurrent
        // caller to commit flips it away from NULL, so a second caller's WHERE re-evaluates false
        // and it affects zero rows. Setting status = Running immediately makes the document show
        // "Processing" from the first second; ReleaseOrFailAsync resets it back to Queued on a
        // transient failure so a redelivery can reclaim (ADR-027 §D3 / instant-identity-ingest).
        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE extraction_job
               SET claimed_at = {now}, claimed_by = {claimedBy}, attempt_count = attempt_count + 1,
                   status = {runningStatus}
             WHERE id = {jobId.Value} AND status = {queuedStatus} AND claimed_at IS NULL
            """,
            cancellationToken);
    }
}
