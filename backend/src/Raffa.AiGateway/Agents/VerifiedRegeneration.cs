using Raffa.AiGateway.Telemetry;

namespace Raffa.AiGateway.Agents;

/// <summary>What <see cref="VerifiedRegeneration.RunAsync{T}"/> ended with: the last value produced,
/// the verifier's verdict on it, and how it got there.</summary>
public sealed record VerifiedOutcome<T>(T Value, StepVerdict Verdict, int Attempts, bool Intervened)
{
    /// <summary>True when <see cref="Value"/> passed the verifier (on the first or the second attempt).</summary>
    public bool Accepted => Verdict.Ok;
}

/// <summary>
/// The regenerate-once policy (ADR-024, plan A-03) for callers that hold their own gateway call and
/// are not (yet) a <see cref="StepRunner"/> step: generate, verify; on a violation generate once more
/// with the violation named; then stop, whatever the verdict, and let the caller downgrade. It is the
/// same loop the runner applies to a step with a verifier, with the same "verifier intervened" metric.
/// It decides nothing about wording or downgrade: those stay with the caller (the Ask guards'
/// <c>RegenerateOnce</c> instruction text and abstain downgrade are unchanged).
/// </summary>
public static class VerifiedRegeneration
{
    /// <param name="generate">Produces a value; receives <see langword="null"/> on the first attempt and
    /// the retry instruction on the second.</param>
    /// <param name="verify">The deterministic check of a produced value.</param>
    /// <param name="retryInstruction">The addendum naming the violation, built from the first verdict.</param>
    /// <param name="agent">Agent name for the metric.</param>
    /// <param name="verifier">Verifier name for the metric.</param>
    public static async Task<VerifiedOutcome<T>> RunAsync<T>(
        Func<string?, CancellationToken, Task<T>> generate,
        Func<T, StepVerdict> verify,
        Func<StepVerdict, string> retryInstruction,
        string agent,
        string verifier,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(generate);
        ArgumentNullException.ThrowIfNull(verify);
        ArgumentNullException.ThrowIfNull(retryInstruction);
        ArgumentException.ThrowIfNullOrWhiteSpace(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(verifier);

        var first = await generate(null, cancellationToken).ConfigureAwait(false);
        var firstVerdict = verify(first);
        if (firstVerdict.Ok)
        {
            return new VerifiedOutcome<T>(first, firstVerdict, 1, false);
        }

        AgentMetrics.RecordVerifierIntervention(agent, verifier, AgentMetrics.OutcomeRetried);
        var second = await generate(retryInstruction(firstVerdict), cancellationToken).ConfigureAwait(false);
        var secondVerdict = verify(second);
        if (!secondVerdict.Ok)
        {
            AgentMetrics.RecordVerifierIntervention(agent, verifier, AgentMetrics.OutcomeRejected);
        }

        return new VerifiedOutcome<T>(second, secondVerdict, 2, true);
    }
}
