using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Raffa.SharedKernel.Jobs;

namespace Raffa.Messaging;

/// <summary>
/// Publishes the shared <see cref="JobRequested"/> pointer (task F6-T05) to the same
/// <c>extraction-events</c> topic the document pipeline uses -- no second topic, no second
/// subscription, so no infrastructure change. The consumer routes on the message subject
/// (<see cref="JobRequested.MessageSubject"/>). One <see cref="ServiceBusSender"/> for the process
/// lifetime. Publish-before-commit is the caller's contract (see <see cref="IJobQueuePublisher"/>).
/// The message id is <c>{kind}:{jobId}</c>, so a retried publish of the same job is the same
/// message on a broker with duplicate detection, and ids of different kinds can never collide.
/// </summary>
public sealed class ServiceBusJobQueuePublisher : IJobQueuePublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ServiceBusSender _sender;
    private readonly ILogger<ServiceBusJobQueuePublisher> _logger;

    public ServiceBusJobQueuePublisher(
        ServiceBusClient client,
        IOptions<ExtractionQueueOptions> options,
        ILogger<ServiceBusJobQueuePublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        _sender = client.CreateSender(options.Value.TopicName);
        _logger = logger;
    }

    public bool IsDurable => true;

    public async Task PublishAsync(JobRequested message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var envelope = new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(message, Json))
        {
            MessageId = $"{message.Kind}:{message.JobId}",
            Subject = JobRequested.MessageSubject,
            ContentType = "application/json",
        };
        envelope.ApplicationProperties["schemaVersion"] = message.SchemaVersion;
        envelope.ApplicationProperties["tenantId"] = message.TenantId.ToString();
        envelope.ApplicationProperties["kind"] = message.Kind;

        await _sender.SendMessageAsync(envelope, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Published JobRequested {Kind} for resource {ResourceId} (job {JobId}) to topic {Topic}",
            message.Kind, message.ResourceId, message.JobId, _sender.EntityPath);
    }

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}
