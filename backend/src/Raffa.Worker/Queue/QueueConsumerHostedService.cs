using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Raffa.Worker.Queue;

/// <summary>
/// The Worker host's queue-consumption loop (parent story us-04 AC-2: "Worker host ... consumes
/// the queue"). Internal: this is host wiring, not a public API surface — enforced by
/// Raffa.ArchitectureTests.DependencyDirectionTests.Host_must_not_contain_domain_types.
///
/// Drains <see cref="IQueueConsumer"/> to completion for each message. Dispatching a received
/// message to a concrete domain handler (for example advancing an
/// Raffa.Documents.Contracts.Domain.ExtractionJob through module-map.md's extraction pipeline)
/// is a later task once that handler exists; this proves the receive/complete/abandon mechanism
/// itself runs end-to-end inside the real hosted-service pipeline, which is this task's scope.
/// </summary>
internal sealed class QueueConsumerHostedService(
    IQueueConsumer queueConsumer,
    ILogger<QueueConsumerHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(200);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var message = await queueConsumer.ReceiveAsync(stoppingToken).ConfigureAwait(false);
            if (message is null)
            {
                await Task.Delay(PollDelay, stoppingToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                logger.LogInformation("Processing queue message {MessageId}", message.MessageId);
                await queueConsumer.CompleteAsync(message, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to process queue message {MessageId}; abandoning", message.MessageId);
                await queueConsumer.AbandonAsync(message, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
