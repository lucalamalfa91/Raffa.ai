using Raffa.AiGateway.Agents;

namespace Raffa.AiFlows.Negotiation.Agents;

// The council's three agents as runner steps (plan A-05a, decision D8: always on, behaviour unchanged).
// The prompts, the version, the agent names and the hand-written strict schemas are the CouncilAgents
// constants exactly as before, so the requests that reach IAiGateway are byte-identical to the ones
// NegotiationCouncil built by hand; the wire records below mirror the old private DTOs property for
// property (the runner serializes them with the same options, so InputJson is identical).

internal sealed record CouncilValue(string Key, string Value, string Kind, string? Currency);

internal sealed record CouncilItem(
    string CitationKey, string Corpus, string Title, string? Subtitle, string Snippet, IReadOnlyList<CouncilValue> Values);

internal sealed record CouncilGoal(
    decimal? TargetAmount, decimal? TargetPercent, string? Currency, int? WindowDays, string? WindowLabel);

internal sealed record CouncilAnalystInput(string Question, CouncilGoal? Goal, IReadOnlyList<CouncilItem> Items);

internal sealed record CouncilFinding(string Title, string Insight, string? LeverType, IReadOnlyList<string>? CitationKeys);

internal sealed record CouncilFindingsPayload(IReadOnlyList<CouncilFinding>? Findings);

internal sealed record CouncilFindingsByAgent(
    IReadOnlyList<CouncilFinding> ContractAnalyst, IReadOnlyList<CouncilFinding> MarketAnalyst);

internal sealed record CouncilStrategistInput(
    string Question, CouncilGoal? Goal, IReadOnlyList<CouncilItem> Items, CouncilFindingsByAgent Findings);

internal sealed record CouncilPlay(
    int Rank, string Lever, string Ask, IReadOnlyList<string>? ExpectedValueKeys, string? Fallback, string? Timing,
    IReadOnlyList<string>? CitationKeys);

internal sealed record CouncilPlaysPayload(IReadOnlyList<CouncilPlay>? Plays);

/// <summary>The three council definitions. The analysts are optional (<see cref="AgentFailurePolicy.Skip"/>:
/// a failed analyst degrades the council, never the turn); the strategist is required
/// (<see cref="AgentFailurePolicy.Fail"/>: no plays means no council for the turn). The council had no
/// deadline of its own (only the gateway's per-call timeout and retries bound it), so the deadline is
/// a backstop well above that, <see cref="Options.CouncilOptions.AgentDeadline"/>.</summary>
internal static class CouncilStepDefinitions
{
    // SHA-256 of each prompt (AgentDefinition.HashPrompt), registered next to the version: a prompt
    // edited without a version bump fails the definition's Validate() in a test.
    private const string ContractAnalystHash = "7a7ebbff304bbec28aa680ea3a21f7d6e8693d52881b57ec3817e9b8951b9c6f";
    private const string MarketAnalystHash = "6588ffe9477b2b7d063a17efc0f769020c9ecf5658a051c052e7fbcc8bccebd3";
    private const string LeverStrategistHash = "ca07e9bbdb4da29da985a5fcf30b2cc259d6199955a0048e87710c78816d5bb9";

    public static AgentDefinition ContractAnalyst(TimeSpan deadline) => new(
        CouncilAgents.ContractAnalystName, CouncilAgents.Version, CouncilAgents.ContractAnalystPrompt,
        CouncilAgents.FindingsSchema, deadline, AgentFailurePolicy.Skip,
        registeredPromptHash: ContractAnalystHash);

    public static AgentDefinition MarketAnalyst(TimeSpan deadline) => new(
        CouncilAgents.MarketAnalystName, CouncilAgents.Version, CouncilAgents.MarketAnalystPrompt,
        CouncilAgents.FindingsSchema, deadline, AgentFailurePolicy.Skip,
        registeredPromptHash: MarketAnalystHash);

    public static AgentDefinition LeverStrategist(TimeSpan deadline) => new(
        CouncilAgents.LeverStrategistName, CouncilAgents.Version, CouncilAgents.LeverStrategistPrompt,
        CouncilAgents.PlaysSchema, deadline, AgentFailurePolicy.Fail,
        registeredPromptHash: LeverStrategistHash);
}

/// <summary>Round one, tenant slice: the contract analyst.</summary>
internal sealed class ContractAnalystStep(TimeSpan deadline) : IAgentStep<CouncilAnalystInput, CouncilFindingsPayload>
{
    public AgentDefinition Definition { get; } = CouncilStepDefinitions.ContractAnalyst(deadline);
}

/// <summary>Round one, market slice: the market analyst.</summary>
internal sealed class MarketAnalystStep(TimeSpan deadline) : IAgentStep<CouncilAnalystInput, CouncilFindingsPayload>
{
    public AgentDefinition Definition { get; } = CouncilStepDefinitions.MarketAnalyst(deadline);
}

/// <summary>Round two: the lever strategist, over both analysts' findings.</summary>
internal sealed class LeverStrategistStep(TimeSpan deadline) : IAgentStep<CouncilStrategistInput, CouncilPlaysPayload>
{
    public AgentDefinition Definition { get; } = CouncilStepDefinitions.LeverStrategist(deadline);
}
