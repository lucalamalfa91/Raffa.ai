using System.Collections.Concurrent;
using System.Threading.Channels;
using Raffa.Documents.Contracts.Application.Extraction;

namespace Raffa.Messaging;

/// <summary>
/// In-process <see cref="IEnrichQueuePublisher"/> — the consuming side of the two-queue split
/// (instant-identity-ingest, <see cref="EnrichRequested"/>). Mirrors <see cref="InMemoryExtractionQueue"/>'s
/// shape: used in tests and single-process local runs only; never the deployed path.
/// </summary>
public sealed class InMemoryEnrichQueue : IEnrichQueuePublisher
{
    private readonly Channel<EnrichRequested> _channel = Channel.CreateUnbounded<EnrichRequested>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    private readonly ConcurrentQueue<EnrichRequested> _published = new();

    /// <summary>Every message ever published through this instance, in order — for assertions.</summary>
    public IReadOnlyCollection<EnrichRequested> Published => _published.ToArray();

    /// <summary>The consumer's end: <see cref="ChannelReader{T}.ReadAllAsync"/> until the host stops.</summary>
    public ChannelReader<EnrichRequested> Reader => _channel.Reader;

    public Task PublishAsync(EnrichRequested message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _published.Enqueue(message);
        _channel.Writer.TryWrite(message);
        return Task.CompletedTask;
    }
}
