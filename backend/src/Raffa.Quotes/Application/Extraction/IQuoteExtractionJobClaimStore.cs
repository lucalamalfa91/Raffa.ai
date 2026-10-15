using Raffa.SharedKernel;

namespace Raffa.Quotes.Application.Extraction;

/// <summary>Outcome of a claim attempt on a <c>QuoteExtractionJob</c>.</summary>
public enum QuoteJobClaimResult
{
    /// <summary>This caller now owns the run: the job is <c>Running</c> and the quote <c>Processing</c>.</summary>
    Claimed,

    /// <summary>No such job for this tenant and quote: the publisher's commit may still be landing, or the pointer is stale.</summary>
    NotFound,

    /// <summary>The job exists but someone else owns it, or it is already finished.</summary>
    Lost,
}

/// <summary>
/// The compare-and-swap that turns the queue's at-least-once delivery into one run per job (task
/// F6-T01), the quote counterpart of the document pipeline's claim store. One atomic
/// <c>UPDATE</c>: the database, not the caller, decides who won.
///
/// <para>
/// <b>Hang recovery without a heartbeat column.</b> A claim succeeds on a <c>Queued</c> job, and also
/// on a <c>Running</c> one when this is a <em>redelivery</em> (delivery count above one) and the run
/// started at least <see cref="TakeoverMinimumAge"/> ago. A broker only redelivers a message whose
/// previous consumer is gone (crashed, or lost its lock), so a redelivery that finds the job still
/// <c>Running</c> is exactly a hung run; taking it over is the recovery. The pipeline replaces, never
/// adds (task F6-T03), so even a spurious takeover cannot duplicate lines.
/// </para>
/// </summary>
public interface IQuoteExtractionJobClaimStore
{
    /// <summary>Minimum age of a <c>Running</c> job before a redelivery may take it over; stops a burst
    /// of duplicate deliveries from racing a run that has only just started.</summary>
    static readonly TimeSpan TakeoverMinimumAge = TimeSpan.FromMinutes(2);

    Task<QuoteJobClaimResult> TryClaimAsync(
        TenantId tenantId,
        EntityId quoteId,
        EntityId jobId,
        long deliveryCount,
        CancellationToken cancellationToken);

    /// <summary>
    /// Puts a job back to <c>Queued</c> (and its quote to <c>Uploaded</c>) so a later delivery can claim
    /// it: used when a run was cut short by the host shutting down, which is not the quote's failure.
    /// </summary>
    Task ReleaseAsync(
        TenantId tenantId,
        EntityId quoteId,
        EntityId jobId,
        CancellationToken cancellationToken);
}
