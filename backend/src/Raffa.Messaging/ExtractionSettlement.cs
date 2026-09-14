using Raffa.Documents.Contracts.Application.Extraction;

namespace Raffa.Messaging;

/// <summary>
/// ADR-027 §C6, task E16/F02/US02/T01 AC-6 (fix 2026-09-14 -- the branch the wave shipped without):
/// how a delivery is settled once the handler has said what it found. A pure function, so the
/// three branches are unit-tested without a broker.
/// <list type="bullet">
/// <item>row present (handled, or the claim lost to a duplicate) => <b>Complete</b>;</item>
/// <item>row absent and <c>DeliveryCount</c> below two => <b>Abandon</b>: the upload's commit may
/// simply be slower than its publish, and the redelivery will see the row;</item>
/// <item>row absent and <c>DeliveryCount</c> two or more => <b>DeadLetter</b> with reason
/// <see cref="JobNotFoundReason"/>: two abandons, capped, no handler state. Never a silent complete,
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
        ExtractionHandleOutcome.JobNotFound when deliveryCount < 2 => Action.Abandon,
        ExtractionHandleOutcome.JobNotFound => Action.DeadLetter,
        _ => Action.Complete,
    };
}
