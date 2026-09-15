using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Raffa.Documents.Contracts.Application.Extraction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Raffa.Messaging;

/// <summary>
/// The API half of a stranded-<c>Uploaded</c> recovery: one document's pointer may already sit
/// on the subscription dead-letter (ADR-027 §C6 <c>job-not-found</c>, or <c>max_delivery_count</c>),
/// in which case publishing a second copy is unnecessary and leaving the dead-lettered one behind
/// is clutter. This type receives from <c>$DeadLetterQueue</c>, resubmits the first match for
/// <em>this</em> tenant+document, completes every match, and abandons everything else so another
/// tenant's stranded job stays put (ADR-009 — ids-only messages, never a cross-tenant complete).
///
/// A receive or send failure returns <see langword="false"/> rather than throwing: the caller
/// then publishes a fresh pointer, which is the recovery that still works when the dead-letter
/// is empty, locked, or this identity cannot read it.
/// </summary>
public sealed class ServiceBusExtractionDeadLetterResubmitter(
    ServiceBusClient client,
    IOptions<ExtractionQueueOptions> options,
    ILogger<ServiceBusExtractionDeadLetterResubmitter> logger) : IExtractionDeadLetterResubmitter
{
    internal const int MaxScan = 100;
    internal static readonly TimeSpan ReceiveWait = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<bool> TryResubmitAsync(ExtractionRequested pointer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pointer);

        var settings = options.Value;
        try
        {
            await using var receiver = client.CreateReceiver(
                settings.TopicName,
                settings.SubscriptionName,
                new ServiceBusReceiverOptions
                {
                    SubQueue = SubQueue.DeadLetter,
                    ReceiveMode = ServiceBusReceiveMode.PeekLock,
                });
            await using var sender = client.CreateSender(settings.TopicName);

            var scanned = 0;
            var resubmitted = false;
            while (scanned < MaxScan)
            {
                var batch = await receiver
                    .ReceiveMessagesAsync(Math.Min(32, MaxScan - scanned), ReceiveWait, cancellationToken)
                    .ConfigureAwait(false);
                if (batch.Count == 0)
                {
                    break;
                }

                foreach (var message in batch)
                {
                    scanned++;
                    if (!MatchesThisDocument(message, pointer))
                    {
                        await receiver.AbandonMessageAsync(message, cancellationToken: cancellationToken)
                            .ConfigureAwait(false);
                        continue;
                    }

                    if (!resubmitted)
                    {
                        await sender.SendMessageAsync(ToResubmitEnvelope(pointer), cancellationToken)
                            .ConfigureAwait(false);
                        resubmitted = true;
                    }

                    await receiver.CompleteMessageAsync(message, cancellationToken).ConfigureAwait(false);
                }
            }

            if (resubmitted)
            {
                logger.LogInformation(
                    "Resubmitted dead-lettered ExtractionRequested for document {DocumentId} (job {JobId})",
                    pointer.DocumentId, pointer.ExtractionJobId);
            }

            return resubmitted;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Dead-letter resubmit for document {DocumentId} failed; a fresh pointer will be published instead",
                pointer.DocumentId);
            return false;
        }
    }

    internal static bool MatchesThisDocument(ServiceBusReceivedMessage message, ExtractionRequested pointer)
    {
        ExtractionRequested? body;
        try
        {
            body = JsonSerializer.Deserialize<ExtractionRequested>(message.Body.ToArray(), Json);
        }
        catch (JsonException)
        {
            return false;
        }

        return body is not null
            && body.TenantId == pointer.TenantId
            && body.DocumentId == pointer.DocumentId;
    }

    private static ServiceBusMessage ToResubmitEnvelope(ExtractionRequested pointer)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(pointer, Json);
        var envelope = new ServiceBusMessage(body)
        {
            MessageId = $"{pointer.ExtractionJobId}:dlq",
            Subject = nameof(ExtractionRequested),
            ContentType = "application/json",
        };
        envelope.ApplicationProperties["schemaVersion"] = pointer.SchemaVersion;
        envelope.ApplicationProperties["tenantId"] = pointer.TenantId.ToString();
        return envelope;
    }
}
