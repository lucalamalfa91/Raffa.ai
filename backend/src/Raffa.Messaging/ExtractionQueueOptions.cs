using Raffa.Documents.Contracts.Application.Extraction;

namespace Raffa.Messaging;

/// <summary>
/// The <c>ServiceBus</c> configuration section, bound from the environment variables
/// <c>infra/modules/containerapps</c> injects into both containers (task E16/F01/US01/T01):
/// <c>ServiceBus__FullyQualifiedNamespace</c>, <c>ServiceBus__TopicName</c>,
/// <c>ServiceBus__SubscriptionName</c> (Worker only), <c>ServiceBus__MaxAutoLockRenewalMinutes</c>.
/// An absent namespace is the switch that selects the in-process channel instead — a deliberate
/// local/test posture, logged at startup, never a silent fallback in a deployed environment.
/// </summary>
public sealed class ExtractionQueueOptions
{
    public const string SectionName = "ServiceBus";

    /// <summary>e.g. <c>sb-raffa-dev.servicebus.windows.net</c>. Empty selects the in-process queue.</summary>
    public string? FullyQualifiedNamespace { get; set; }

    public string TopicName { get; set; } = ExtractionQueueNames.TopicName;

    public string SubscriptionName { get; set; } = ExtractionQueueNames.SubscriptionName;

    /// <summary>
    /// How long the consumer keeps renewing a message lock while the handler runs. A cold OCR on a
    /// forty-page scan is minutes, and a lock that expires mid-pipeline hands the same message to a
    /// second replica whose claim then loses (harmless) — but the first replica's Complete then
    /// fails with a lost lock and the message is redelivered once more for nothing. Terraform
    /// sets this to match the subscription's own lock duration ceiling.
    /// </summary>
    public int MaxAutoLockRenewalMinutes { get; set; } = 10;

    /// <summary>Concurrent handler invocations per Worker replica.</summary>
    public int MaxConcurrentCalls { get; set; } = 2;

    public bool UsesServiceBus => !string.IsNullOrWhiteSpace(FullyQualifiedNamespace);
}
