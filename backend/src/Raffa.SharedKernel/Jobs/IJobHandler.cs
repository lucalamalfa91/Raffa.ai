namespace Raffa.SharedKernel.Jobs;

/// <summary>
/// The consuming side of the shared job kernel (task F6-T05): one handler per <see cref="Kind"/>,
/// resolved by the queue consumer for each delivery inside a fresh DI scope. The handler owns the
/// claim of its own job row and everything the job does; the consumer owns only transport
/// (receive, route, settle).
/// </summary>
public interface IJobHandler
{
    /// <summary>The <see cref="JobKinds"/> value this handler owns.</summary>
    string Kind { get; }

    /// <summary>
    /// Works one delivery. Returns normally for "done" and for "someone else has it" (the message is
    /// completed). Throws for a failure the broker should redeliver; a handler that wants a redelivery
    /// must have released its claim first, so the next delivery can win it.
    /// </summary>
    Task<JobHandleOutcome> HandleAsync(JobRequested message, CancellationToken cancellationToken = default);
}

/// <summary>What a handler found. <see cref="Handled"/> and <see cref="ClaimLost"/> both complete the
/// message; only <see cref="JobNotFound"/> -- no row at all inside the message's own tenant scope -- is
/// settled by delivery count (abandon below two, dead-letter from two), because the publisher's commit
/// may simply still be landing.</summary>
public enum JobHandleOutcome
{
    Handled,
    ClaimLost,
    JobNotFound,
}
