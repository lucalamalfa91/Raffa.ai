using Raffa.AiGateway.Contracts;

namespace Raffa.AiGateway.Agents;

/// <summary>How a step ended. Never an exception for an expected failure: a flow reads this.</summary>
public enum StepStatus
{
    /// <summary>The output is in <see cref="StepResult{TOut}.Value"/>, and the verifier (if any) accepted it.</summary>
    Succeeded = 0,

    /// <summary>The step could not deliver; <see cref="AgentFailurePolicy.Fallback"/> supplied the value.</summary>
    FellBack = 1,

    /// <summary>The step could not deliver and is optional (<see cref="AgentFailurePolicy.Skip"/>): the
    /// flow goes on without it. <see cref="StepResult{TOut}.Reason"/> says why, explicitly.</summary>
    Skipped = 2,

    /// <summary>The step could not deliver and is required (<see cref="AgentFailurePolicy.Fail"/>).</summary>
    Failed = 3,
}

/// <summary>Why a step did not deliver.</summary>
public enum StepFailureReason
{
    None = 0,

    /// <summary>The gateway returned a failure (after its own retries) or the role is unavailable.</summary>
    GatewayError = 1,

    /// <summary>The payload was not valid JSON for the output type, or parsed to nothing.</summary>
    InvalidPayload = 2,

    /// <summary>The verifier still rejected the output after the one retry (or retry is off).</summary>
    VerifierRejected = 3,

    /// <summary>The step's deadline passed; the call was cancelled. Never a silently truncated output.</summary>
    DeadlineExceeded = 4,
}

/// <summary>
/// What one step produced, explicit about how it got there. <see cref="Value"/> is set for
/// <see cref="StepStatus.Succeeded"/> and <see cref="StepStatus.FellBack"/>. A fan-out returns one of
/// these per step and the flow tolerates the partial failures it chooses to
/// (<see cref="Describe"/> is the line a flow puts in its failure list).
/// </summary>
public sealed record StepResult<TOut>(
    string StepName,
    StepStatus Status,
    TOut? Value,
    StepFailureReason Reason,
    string? Error,
    int Attempts,
    bool VerifierIntervened,
    AiCallMetadata? Metadata,
    TimeSpan Elapsed)
    where TOut : class
{
    public bool IsSuccess => Status == StepStatus.Succeeded;

    /// <summary>True when <see cref="Value"/> can be used (a real output or the declared fallback).</summary>
    public bool HasValue => Value is not null;

    /// <summary>Whether the step is required and did not deliver.</summary>
    public bool IsFailure => Status == StepStatus.Failed;

    /// <summary><c>"contract-analyst: &lt;error&gt;"</c>, or <see langword="null"/> when the step succeeded.</summary>
    public string? Describe() => IsSuccess ? null : $"{StepName}: {Error ?? Reason.ToString()}";
}
