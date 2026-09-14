using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Raffa.Documents.Contracts.Application.Extraction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Raffa.Messaging;

/// <summary>
/// Publishes the <see cref="ExtractionRequested"/> pointer to the <c>extraction-events</c> topic
/// (ADR-027 §D2). One <see cref="ServiceBusSender"/> for the process lifetime — the client is a
/// connection pool and is meant to be a singleton.
///
/// <para>
/// <b>Publish-before-commit is the caller's contract, not this class's.</b>
/// <see cref="Raffa.Documents.Contracts.Application.DocumentUploadService"/> sends the pointer
/// <em>before</em> its <c>SaveChangesAsync</c>, so a publish failure fails the upload (nothing
/// durable is left behind) and a commit failure leaves a message pointing at a job that does
/// not exist — which the Worker's claim answers with zero rows and a completed message. The
/// message id is the job id, so a broker with duplicate detection enabled collapses a retried
/// publish; a broker without it relies on the same claim.
/// </para>
/// </summary>
public sealed class ServiceBusExtractionQueuePublisher : IExtractionQueuePublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ServiceBusSender _sender;
    private readonly ILogger<ServiceBusExtractionQueuePublisher> _logger;

    public ServiceBusExtractionQueuePublisher(
        ServiceBusClient client,
        IOptions<ExtractionQueueOptions> options,
        ILogger<ServiceBusExtractionQueuePublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        _sender = client.CreateSender(options.Value.TopicName);
        _logger = logger;
    }

    public async Task PublishAsync(ExtractionRequested message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var body = JsonSerializer.SerializeToUtf8Bytes(message, Json);
        var envelope = new ServiceBusMessage(body)
        {
            // The job id, not a fresh GUID: a retried publish of the same job is the same message.
            MessageId = message.ExtractionJobId.ToString(),
            Subject = nameof(ExtractionRequested),
            ContentType = "application/json",
        };
        envelope.ApplicationProperties["schemaVersion"] = message.SchemaVersion;
        envelope.ApplicationProperties["tenantId"] = message.TenantId.ToString();

        await _sender.SendMessageAsync(envelope, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Published ExtractionRequested for document {DocumentId} (job {JobId}) to topic {Topic}",
            message.DocumentId, message.ExtractionJobId, _sender.EntityPath);
    }

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}
