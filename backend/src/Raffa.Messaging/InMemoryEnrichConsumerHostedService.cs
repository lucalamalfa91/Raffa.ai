using Raffa.Documents.Contracts.Application.Extraction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Raffa.Messaging;

/// <summary>
/// Drains <see cref="InMemoryEnrichQueue"/> for the two-queue split (instant-identity-ingest).
/// In-process only; never the deployed path. No redelivery — same posture as
/// <see cref="InMemoryExtractionConsumerHostedService"/>.
/// </summary>
public sealed class InMemoryEnrichConsumerHostedService(
    InMemoryEnrichQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<InMemoryEnrichConsumerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogWarning(
            "Enrich consumer is the IN-PROCESS channel (no ServiceBus:FullyQualifiedNamespace configured). " +
            "Only enrich messages published by this same process are processed; this is a local/test posture.");

        try
        {
            await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var handler = scope.ServiceProvider.GetRequiredService<EnrichRequestedHandler>();
                    await handler.HandleAsync(message, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "In-process enrich failed for document {DocumentId}", message.DocumentId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
    }
}
