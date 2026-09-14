using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Raffa.Documents.Contracts.Application.Extraction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Raffa.Messaging;

/// <summary>
/// The Worker's subscription consumer (ADR-027 §D2/§D3): one <see cref="ServiceBusProcessor"/> on
/// <c>extraction-events / document-processing</c>, manual completion, a DI scope per message,
/// <see cref="ExtractionRequestedHandler"/> doing the work.
///
/// <para>
/// <b>Settlement rules</b>, each chosen so the database keeps owning the terminal state:
/// <list type="bullet">
/// <item>handler returned → <b>Complete</b>. Includes "claim lost": a duplicate delivery is
/// completed, never abandoned into a loop;</item>
/// <item><see cref="ExtractionTransientException"/> → <b>Abandon</b>: the handler already released
/// its claim, so the redelivery can win it; <see cref="ExtractionRequestedHandler.MaxAttempts"/>
/// bounds the loop on the row, the subscription's max delivery count bounds it on the broker;</item>
/// <item>any other exception → <b>Abandon</b> too. The claim is still held, so the redelivery's
/// claim loses and the message is completed on its second pass — the row stays
/// <c>Processing</c> for an operator to see, which is the honest outcome of an unexpected
/// crash mid-pipeline, not a silent dead-letter;</item>
/// <item>a body that does not deserialise → <b>DeadLetter</b>: it will never succeed and there
/// is no row to record it on.</item>
/// </list>
/// </para>
/// </summary>
public sealed class ServiceBusExtractionConsumerHostedService(
    ServiceBusClient client,
    IOptions<ExtractionQueueOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<ServiceBusExtractionConsumerHostedService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        _processor = client.CreateProcessor(settings.TopicName, settings.SubscriptionName, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = Math.Max(1, settings.MaxConcurrentCalls),
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(Math.Max(1, settings.MaxAutoLockRenewalMinutes)),
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
        });
        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync += OnErrorAsync;

        logger.LogInformation(
            "Extraction consumer listening on {Namespace} {Topic}/{Subscription} (concurrency {Concurrency}, lock renewal {Minutes} min)",
            settings.FullyQualifiedNamespace, settings.TopicName, settings.SubscriptionName,
            settings.MaxConcurrentCalls, settings.MaxAutoLockRenewalMinutes);

        await _processor.StartProcessingAsync(stoppingToken).ConfigureAwait(false);
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
        finally
        {
            await _processor.StopProcessingAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        ExtractionRequested? message;
        try
        {
            message = JsonSerializer.Deserialize<ExtractionRequested>(args.Message.Body.ToArray(), Json);
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "Message {MessageId} is not an ExtractionRequested payload; dead-lettering", args.Message.MessageId);
            await args.DeadLetterMessageAsync(args.Message, "malformed", exception.Message, args.CancellationToken).ConfigureAwait(false);
            return;
        }

        if (message is null)
        {
            await args.DeadLetterMessageAsync(args.Message, "malformed", "empty body", args.CancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<ExtractionRequestedHandler>();
            await handler.HandleAsync(message, args.CancellationToken).ConfigureAwait(false);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken).ConfigureAwait(false);
        }
        catch (ExtractionTransientException exception)
        {
            logger.LogWarning(
                "Transient failure on document {DocumentId} (delivery {Delivery}); abandoning for redelivery: {Error}",
                message.DocumentId, args.Message.DeliveryCount, exception.Message);
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception, "Unhandled failure processing document {DocumentId} (delivery {Delivery}); abandoning",
                message.DocumentId, args.Message.DeliveryCount);
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken).ConfigureAwait(false);
        }
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(
            args.Exception, "Service Bus processor error ({Source}) on {EntityPath}", args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        if (_processor is not null)
        {
            await _processor.DisposeAsync().ConfigureAwait(false);
        }
    }
}
