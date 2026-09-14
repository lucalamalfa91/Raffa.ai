namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// The Service Bus topic and subscription names the publisher and the consumer must agree on
/// (ADR-027 §D2, corrected by its w15 round-2 footer §C3 and round-3 footer §C8; ADR-005 w15
/// footer §2). One name, or the Terraform (<c>infra/modules/servicebus</c>) and the Worker's
/// receiver disagree about which subscription holds the messages — a receiver opened on a
/// subscription Terraform never created fails exactly where a broken worker still yields a green
/// <c>backend.yml</c> run (the council's own finding, ADR-027 §D2's council-decisions section).
/// Both are also independently overridable via the host's own broker configuration section
/// (ADR-005 w15 footer §5) — these constants are only the default every environment ships with
/// today.
/// </summary>
public static class ExtractionQueueNames
{
    /// <summary>Matches <c>azurerm_servicebus_topic.extraction_events</c>
    /// (<c>infra/modules/servicebus/main.tf</c>).</summary>
    public const string TopicName = "extraction-events";

    /// <summary>Matches the one subscription <c>infra/modules/servicebus</c> creates on that topic
    /// (ADR-027 §C8: this ADR's own D2 sample once named a different, stale worker-role-shaped
    /// subscription value here — corrected, and this is the only name that occurs in this tree now).
    /// A topic with a second subscription would silently process every document twice (ADR-005 w15
    /// clause "exactly one subscription is a design constraint, not an accident").</summary>
    public const string SubscriptionName = "document-processing";
}
