namespace Raffa.SharedKernel.Jobs;

/// <summary>
/// Outbound port of the shared job kernel (task F6-T05): "this job row is ready to be worked".
/// Callers publish <b>before</b> they commit the job row, as the document upload does (a publish
/// failure then fails the request with nothing durable left behind; a commit failure leaves a
/// harmless pointer to a row that does not exist, which the handler answers with
/// <see cref="JobHandleOutcome.JobNotFound"/>).
///
/// <para>
/// <see cref="IsDurable"/> tells the caller whether anything will pick the message up. With a broker
/// configured it is <see langword="true"/>; without one (tests, a single-process dev run) the
/// publisher is a no-op and the caller must run the work itself, inline, as it did before the queue
/// existed. That is the only fallback: it is chosen by configuration, never by a failure.
/// </para>
/// </summary>
public interface IJobQueuePublisher
{
    /// <summary>True when a published message will be delivered to a handler in another process.</summary>
    bool IsDurable { get; }

    Task PublishAsync(JobRequested message, CancellationToken cancellationToken = default);
}
