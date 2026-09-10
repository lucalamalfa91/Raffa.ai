namespace Raffa.Worker.Queue;

/// <summary>
/// Durable-queue consumption port that <see cref="QueueConsumerHostedService"/> drives (parent
/// story us-04 AC-2: "Worker host ... consumes the queue"; ADR-002: "queue message handlers
/// belong to the worker host, not to domain projects"; ADR-005 names Azure Service Bus Standard
/// as the durable-queue product; module-map.md "Worker responsibilities" describes a single
/// Worker host fed by multiple queue types). Internal: this is host-composition wiring, not a
/// public API surface — enforced by
/// Raffa.ArchitectureTests.DependencyDirectionTests.Host_must_not_contain_domain_types, and by
/// design no domain module receives from the queue directly.
///
/// R0 placeholder for the concrete adapter, same shape as the
/// Raffa.AiGateway.IAiGateway / Raffa.Benchmark.IBenchmarkService "R0 placeholder" pattern:
/// the peek-lock-style contract below (receive, then complete or abandon) is real and driven
/// end-to-end today by <see cref="InMemoryQueueConsumer"/>; swapping in an
/// Azure.Messaging.ServiceBus-backed implementation once a producer exists is a later task, not
/// a redesign of this interface.
/// </summary>
internal interface IQueueConsumer
{
    /// <summary>Receives the next available message, or <see langword="null"/> if none is
    /// currently waiting.</summary>
    Task<QueueMessage?> ReceiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Acknowledges successful processing; the message is not redelivered.</summary>
    Task CompleteAsync(QueueMessage message, CancellationToken cancellationToken = default);

    /// <summary>Releases the message back to the queue for redelivery (processing failed).</summary>
    Task AbandonAsync(QueueMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// One message received off the queue: an opaque body plus the id
/// <see cref="IQueueConsumer.CompleteAsync"/>/<see cref="IQueueConsumer.AbandonAsync"/> reference
/// back to the in-flight delivery.
/// </summary>
internal sealed record QueueMessage(string MessageId, string Body);
