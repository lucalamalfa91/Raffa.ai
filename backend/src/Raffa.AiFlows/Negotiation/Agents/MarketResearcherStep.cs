using Raffa.AiGateway.Agents;

namespace Raffa.AiFlows.Negotiation.Agents;

// The market researcher as a runner step (plan A-05, F1 Ask split PR-7; decision D8: always on,
// prompt and behaviour unchanged). Same shape as the council steps: the prompt, version, agent name
// and hand-written strict schema are the CouncilAgents constants, so the request that reaches
// IAiGateway is byte-identical to the one MarketResearcher built by hand; the wire records below
// mirror the old private DTOs property for property (the runner serializes with the same options).

internal sealed record ResearcherGoal(
    decimal? TargetAmount, decimal? TargetPercent, string? Currency, int? WindowDays, string? WindowLabel);

internal sealed record ResearcherValue(string Key, string Value, string Kind, string? Currency);

internal sealed record ResearcherItem(
    string CitationKey, string Corpus, string Title, string? Subtitle, string Snippet, IReadOnlyList<ResearcherValue> Values);

internal sealed record ResearcherStepInput(
    string Question, ResearcherGoal? Goal, IReadOnlyList<string> MissingFields, IReadOnlyList<ResearcherItem> Items);

internal sealed record ResearchQuery(string Query, string? Scope);

internal sealed record ResearcherQueriesPayload(IReadOnlyList<ResearchQuery>? Queries);

/// <summary>The researcher's definition. Optional (<see cref="AgentFailurePolicy.Skip"/>): a failed
/// call degrades the flow, never the turn. The deadline is the council's per-agent backstop
/// (<see cref="Options.CouncilOptions.AgentDeadline"/>), well above the gateway's own timeout and retries.</summary>
internal static class MarketResearcherStepDefinition
{
    // SHA-256 of the prompt (AgentDefinition.HashPrompt), registered next to the version: a prompt
    // edited without a version bump fails the definition's Validate() in a test.
    private const string PromptHash = "5f26a8fb9660d07bdd644b7930e7fbb98988ab241036f1aec9be3ae70bb06e6c";

    public static AgentDefinition Create(TimeSpan deadline) => new(
        CouncilAgents.MarketResearcherName, CouncilAgents.Version, CouncilAgents.MarketResearcherPrompt,
        CouncilAgents.QueriesSchema, deadline, AgentFailurePolicy.Skip,
        registeredPromptHash: PromptHash);
}

/// <summary>The market researcher: writes the market RAG queries; running them is the deterministic tool.</summary>
internal sealed class MarketResearcherStep(TimeSpan deadline) : IAgentStep<ResearcherStepInput, ResearcherQueriesPayload>
{
    public AgentDefinition Definition { get; } = MarketResearcherStepDefinition.Create(deadline);
}
