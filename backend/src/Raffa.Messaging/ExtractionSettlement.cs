using Raffa.Documents.Contracts.Application.Extraction;

namespace Raffa.Messaging;

/// <summary>
/// ADR-027 §C6, task E16/F02/US02/T01 AC-6 (fix 2026-09-14 -- the branch the wave shipped without):
/// how a delivery is settled once the handler has said what it found. A pure function, so the
/// three branches are unit-tested without a broker.
/// <list type="bullet">
/// <item>row present (handled, or the claim lost to a duplicate) => <b>Complete</b>;</item>
/// <item>row absent and <c>DeliveryCount</c> below three => <b>Abandon</b>: with commit-then-publish
/// ordering the commit is always durable before the message arrives, but Service Bus can redeliver
/// very fast and the row is always findable by the time the first delivery fires. Three abandons
/// instead of two gives a wider window for any infra-level timing jitter.</item>
/// <item>row absent and <c>DeliveryCount</c> three or more => <b>DeadLetter</b> with reason
/// <see cref="JobNotFoundReason"/>: three abandons, capped, no handler state. Never a silent complete,
/// which would strand a document at <c>Uploaded</c> on a POST that returned 201.</item>
/// </list>
/// </summary>
public static class ExtractionSettlement
{
    public const string JobNotFoundReason = "job-not-found";

    public enum Action
    {
        Complete,
        Abandon,
        DeadLetter,
    }

    public static Action Decide(ExtractionHandleOutcome outcome, long deliveryCount) => outcome switch
    {
        ExtractionHandleOutcome.JobNotFound when deliveryCount < 3 => Action.Abandon,
        ExtractionHandleOutcome.JobNotFound => Action.DeadLetter,
        _ => Action.Complete,
    };
}
