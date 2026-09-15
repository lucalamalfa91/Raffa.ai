using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Raffa.Documents.Contracts.Application.Extraction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Raffa.Messaging;

/// <summary>
/// Publishes <see cref="EnrichRequested"/> messages to the same <c>extraction-events</c> topic
/// as <see cref="ServiceBusExtractionQueuePublisher"/>, but with <c>Subject = "EnrichRequested"</c>
/// so the consumer can route intake vs enrich messages by Subject (two-queue split,
/// instant-identity-ingest).
///
/// <para>
/// Using the same topic for both message types avoids Terraform changes (only one topic and one
/// subscription exist today). The consumer reads Subject and dispatches to the correct handler.
/// </para>
/// </summary>
public sealed class ServiceBusEnrichQueuePublisher : IEnrichQueuePublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ServiceBusSender _sender;
    private readonly ILogger<ServiceBusEnrichQueuePublisher> _logger;

    public ServiceBusEnrichQueuePublisher(
        ServiceBusClient client,
        IOptions<ExtractionQueueOptions> options,
        ILogger<ServiceBusEnrichQueuePublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        _sender = client.CreateSender(options.Value.TopicName);
        _logger = logger;
    }

    public async Task PublishAsync(EnrichRequested message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var body = JsonSerializer.SerializeToUtf8Bytes(message, Json);
        var envelope = new ServiceBusMessage(body)
        {
            // MessageId is the documentId — an enrich message is idempotent per document.
            MessageId = message.DocumentId.ToString(),
            Subject = nameof(EnrichRequested),
            ContentType = "application/json",
        };
        envelope.ApplicationProperties["schemaVersion"] = message.SchemaVersion;
        envelope.ApplicationProperties["tenantId"] = message.TenantId.ToString();

        await _sender.SendMessageAsync(envelope, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Published EnrichRequested for document {DocumentId} to topic {Topic}",
            message.DocumentId, _sender.EntityPath);
    }

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}
