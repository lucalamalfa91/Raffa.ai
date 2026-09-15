using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// The in-process <see cref="IExtractionQueuePublisher"/> — a single unbounded channel that the
/// same process drains (<c>Raffa.Messaging.InMemoryExtractionConsumerHostedService</c>). Two
/// uses, both deliberate, and one non-use that matters more:
/// <list type="bullet">
/// <item>tests: every <c>WebApplicationFactory</c> host and every unit test that constructs
/// <see cref="DocumentUploadService"/> gets a publisher that records what was published
/// (<see cref="Published"/>) and never dials anything;</item>
/// <item>a single-process dev run with no <c>ServiceBus:FullyQualifiedNamespace</c> configured
/// — the API publishes, and nothing consumes unless the same process hosts the consumer.</item>
/// </list>
/// It is <b>not</b> a bridge between the API and the Worker: those are two processes, and a
/// channel does not cross a process boundary. Deployed environments always carry the Service
/// Bus namespace (task E16/F01/US01/T01 injects <c>ServiceBus__*</c> into both containers), so
/// there the Service Bus publisher is selected and this type is never constructed. Selecting
/// it silently in a deployed environment would be the "queue that goes nowhere" ADR-027 §D2
/// refuses — <c>MessagingServiceCollectionExtensions</c> logs which one it picked at startup.
/// </summary>
public sealed class InMemoryExtractionQueue : IExtractionQueuePublisher, IExtractionDeadLetterResubmitter
{
    private readonly Channel<ExtractionRequested> _channel = Channel.CreateUnbounded<ExtractionRequested>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    private readonly ConcurrentQueue<ExtractionRequested> _published = new();

    /// <summary>Every message ever published through this instance, in order — for assertions.</summary>
    public IReadOnlyCollection<ExtractionRequested> Published => _published.ToArray();

    /// <summary>The consumer's end: <see cref="ChannelReader{T}.ReadAllAsync"/> until the host stops.</summary>
    public ChannelReader<ExtractionRequested> Reader => _channel.Reader;

    public Task PublishAsync(ExtractionRequested message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _published.Enqueue(message);
        // TryWrite never fails on an unbounded channel that is not completed.
        _channel.Writer.TryWrite(message);
        return Task.CompletedTask;
    }

    /// <summary>No broker, no dead-letter — the caller publishes a fresh pointer instead.</summary>
    public Task<bool> TryResubmitAsync(ExtractionRequested pointer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        return Task.FromResult(false);
    }
}
