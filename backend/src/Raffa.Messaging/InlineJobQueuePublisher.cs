using System.Collections.Concurrent;
using Raffa.SharedKernel.Jobs;

namespace Raffa.Messaging;

/// <summary>
/// The <see cref="IJobQueuePublisher"/> of a host with no broker configured (no
/// <c>ServiceBus:FullyQualifiedNamespace</c>): tests and a single-process dev run. It delivers
/// nothing -- <see cref="IsDurable"/> is <see langword="false"/> and the caller runs the work itself,
/// inline, exactly as before the queue existed. Unlike the document pipeline's in-process channel it
/// has no consumer to start, because the work never leaves the request that asked for it. It records
/// what it was asked to publish so a test can prove the call was made.
/// </summary>
public sealed class InlineJobQueuePublisher : IJobQueuePublisher
{
    private readonly ConcurrentQueue<JobRequested> _published = new();

    public bool IsDurable => false;

    /// <summary>Every message handed to this publisher, in order.</summary>
    public IReadOnlyCollection<JobRequested> Published => _published.ToArray();

    public Task PublishAsync(JobRequested message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _published.Enqueue(message);
        return Task.CompletedTask;
    }
}
