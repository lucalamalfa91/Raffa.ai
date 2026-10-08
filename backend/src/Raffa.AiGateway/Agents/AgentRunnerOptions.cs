namespace Raffa.AiGateway.Agents;

/// <summary>Settings of <see cref="StepRunner"/>. Immutable defaults; a host overrides by registering
/// its own instance before <c>AddAiGatewayModule</c>.</summary>
public sealed record AgentRunnerOptions
{
    /// <summary>
    /// How many steps of one runner (one request or turn) may be in flight at once. The council needs
    /// two; the staged extraction seven. The cap exists so a wide fan-out cannot starve the model
    /// deployment's tokens-per-minute budget, not because the gateway is unsafe under concurrency:
    /// its audit writes are already serialized.
    /// </summary>
    public int MaxParallelism { get; init; } = 8;
}
