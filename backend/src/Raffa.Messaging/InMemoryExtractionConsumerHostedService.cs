using Raffa.Documents.Contracts.Application.Extraction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Raffa.Messaging;

/// <summary>
/// Drains <see cref="InMemoryExtractionQueue"/> in the same process that published to it — the
/// consumer half of the no-Service-Bus posture (tests, a single-process local run). No
/// redelivery: a handler exception is logged and the message is gone, which is the right shape
/// for a test double and an acceptable one for a laptop. See
/// <see cref="InMemoryExtractionQueue"/>'s own doc comment for why this is never the deployed
/// path.
/// </summary>
public sealed class InMemoryExtractionConsumerHostedService(
    InMemoryExtractionQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<InMemoryExtractionConsumerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogWarning(
            "Extraction consumer is the IN-PROCESS channel (no ServiceBus:FullyQualifiedNamespace configured). " +
            "Only messages published by this same process are processed; this is a local/test posture.");

        try
        {
            await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var handler = scope.ServiceProvider.GetRequiredService<ExtractionRequestedHandler>();
                    await handler.HandleAsync(message, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "In-process extraction failed for document {DocumentId}", message.DocumentId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
    }
}
