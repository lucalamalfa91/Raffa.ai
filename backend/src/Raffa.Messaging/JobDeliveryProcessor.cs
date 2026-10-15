using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Raffa.SharedKernel.Jobs;

namespace Raffa.Messaging;

/// <summary>What the consumer must do with one delivery of the shared job kernel.</summary>
/// <param name="Action">Complete, abandon for redelivery, or dead-letter.</param>
/// <param name="Reason">Dead-letter reason, when <paramref name="Action"/> is dead-letter.</param>
/// <param name="Detail">Dead-letter description, when <paramref name="Action"/> is dead-letter.</param>
public sealed record JobDeliveryDecision(ExtractionSettlement.Action Action, string? Reason = null, string? Detail = null);

/// <summary>
/// Task F6-T05: everything the consumer does with a <see cref="JobRequested"/> delivery except talk to
/// the broker -- parse, validate, route to the <see cref="IJobHandler"/> that owns the kind, run it in
/// its own DI scope, and decide the settlement. Kept apart from the hosted service so the whole policy
/// is tested without a broker:
/// <list type="bullet">
/// <item>a body that does not parse, an empty kind or an unsupported schema version, or a kind no
/// handler owns -> <b>dead-letter</b>: it will never succeed and there is no row to record it on;</item>
/// <item>handled / claim lost -> <b>complete</b> (a duplicate delivery is completed, never abandoned
/// into a loop); no row yet -> abandon below two deliveries, dead-letter from two
/// (<see cref="ExtractionSettlement"/>, identical to the document pipeline);</item>
/// <item>any exception from the handler -> <b>abandon</b>. The handler releases its claim when it wants
/// the redelivery to win it; otherwise the redelivery loses the claim and is completed, and the row
/// stays visibly in its running state for the handler's own stale-claim takeover.</item>
/// </list>
/// </summary>
public sealed class JobDeliveryProcessor(IServiceScopeFactory scopeFactory, ILogger<JobDeliveryProcessor> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<JobDeliveryDecision> ProcessAsync(
        ReadOnlyMemory<byte> body, long deliveryCount, CancellationToken cancellationToken)
    {
        JobRequested? message;
        try
        {
            message = JsonSerializer.Deserialize<JobRequested>(body.Span, Json);
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "Message is not a JobRequested payload; dead-lettering");
            return Dead("malformed", exception.Message);
        }

        if (message is null || string.IsNullOrWhiteSpace(message.Kind))
        {
            return Dead("malformed", "empty body or kind");
        }

        if (message.SchemaVersion != JobRequested.CurrentSchemaVersion)
        {
            logger.LogError(
                "JobRequested {Kind} has schema version {Version}, this worker reads {Current}; dead-lettering",
                message.Kind, message.SchemaVersion, JobRequested.CurrentSchemaVersion);
            return Dead("unsupported-schema", $"schema version {message.SchemaVersion}");
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetServices<IJobHandler>()
                .FirstOrDefault(h => string.Equals(h.Kind, message.Kind, StringComparison.Ordinal));
            if (handler is null)
            {
                logger.LogError("No handler owns job kind {Kind}; dead-lettering job {JobId}", message.Kind, message.JobId);
                return Dead("unknown-job-kind", $"no handler for kind '{message.Kind}'");
            }

            var outcome = await handler.HandleAsync(message, cancellationToken).ConfigureAwait(false);
            var action = ExtractionSettlement.Decide(outcome, deliveryCount);
            switch (action)
            {
                case ExtractionSettlement.Action.Abandon:
                    logger.LogWarning(
                        "No {Kind} job row {JobId} yet (delivery {Delivery}); abandoning for redelivery",
                        message.Kind, message.JobId, deliveryCount);
                    return new JobDeliveryDecision(action);
                case ExtractionSettlement.Action.DeadLetter:
                    logger.LogError(
                        "No {Kind} job row {JobId} after {Delivery} deliveries; dead-lettering as {Reason}",
                        message.Kind, message.JobId, deliveryCount, ExtractionSettlement.JobNotFoundReason);
                    return Dead(
                        ExtractionSettlement.JobNotFoundReason,
                        $"{message.Kind} job {message.JobId}: no row after {deliveryCount} deliveries");
                default:
                    return new JobDeliveryDecision(action);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception, "Unhandled failure in {Kind} job {JobId} (delivery {Delivery}); abandoning",
                message.Kind, message.JobId, deliveryCount);
            return new JobDeliveryDecision(ExtractionSettlement.Action.Abandon);
        }
    }

    private static JobDeliveryDecision Dead(string reason, string detail) =>
        new(ExtractionSettlement.Action.DeadLetter, reason, detail);
}
