using System.Collections.Concurrent;

namespace Raffa.Worker.Queue;

/// <summary>
/// R0 default <see cref="IQueueConsumer"/>: an in-process, peek-lock-style queue with no external
/// broker dependency. Honest about R0 scope (ADR-002 assumption: "no durable outbox/messaging
/// middleware is assumed beyond the queue at R0") — nothing in the codebase enqueues a durable
/// Azure Service Bus message yet (ADR-005 names the product; wiring that SDK is a later task), so
/// a network-backed consumer would have nothing to receive from and nothing to prove. Registered
/// as the Worker host's default <see cref="IQueueConsumer"/> by
/// <see cref="WorkerServiceCollectionExtensions.AddWorkerHost"/>.
///
/// Thread-safe: <see cref="Enqueue"/> may be called from a producer/test while
/// <see cref="QueueConsumerHostedService"/> is concurrently receiving on a background thread.
/// </summary>
internal sealed class InMemoryQueueConsumer : IQueueConsumer
{
    private readonly ConcurrentQueue<QueueMessage> _pending = new();
    private readonly ConcurrentDictionary<string, QueueMessage> _inFlight = new();
    private readonly ConcurrentQueue<string> _completedMessageIds = new();

    /// <summary>Producer/test hook: makes a message available to the next
    /// <see cref="ReceiveAsync"/>.</summary>
    public void Enqueue(QueueMessage message) => _pending.Enqueue(message);

    /// <summary>Ids the hosted service has successfully completed, oldest first. Exposed so
    /// Raffa.Worker.Tests can prove the receive loop truly ran end-to-end, not just that a
    /// message was dequeued.</summary>
    public IReadOnlyCollection<string> CompletedMessageIds => _completedMessageIds.ToArray();

    public Task<QueueMessage?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        if (_pending.TryDequeue(out var message))
        {
            _inFlight[message.MessageId] = message;
            return Task.FromResult<QueueMessage?>(message);
        }

        return Task.FromResult<QueueMessage?>(null);
    }

    public Task CompleteAsync(QueueMessage message, CancellationToken cancellationToken = default)
    {
        _inFlight.TryRemove(message.MessageId, out _);
        _completedMessageIds.Enqueue(message.MessageId);
        return Task.CompletedTask;
    }

    public Task AbandonAsync(QueueMessage message, CancellationToken cancellationToken = default)
    {
        if (_inFlight.TryRemove(message.MessageId, out var inFlightMessage))
        {
            _pending.Enqueue(inFlightMessage);
        }

        return Task.CompletedTask;
    }
}
