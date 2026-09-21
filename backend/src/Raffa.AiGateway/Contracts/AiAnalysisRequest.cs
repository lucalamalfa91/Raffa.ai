namespace Raffa.AiGateway.Contracts;

/// <summary>
/// One bounded, schema-constrained reasoning step over caller-supplied evidence — the call shape
/// behind Ask Raffa's negotiation council (<c>Raffa.Chat.Application.Council</c>): a specialist
/// agent (contract analyst, market analyst, lever strategist) reads a JSON input assembled from
/// the context pack and returns strict JSON matching <paramref name="JsonSchema"/>. Like every
/// other role: no tools, no grounding, no browsing — the agent sees only <paramref name="InputJson"/>.
/// </summary>
/// <param name="AgentName">Stable agent identity, logged as the prompt version's suffix and used
/// as the strict-schema name (letters, digits, '-' and '_' only).</param>
/// <param name="SystemPrompt">The agent's own versioned persona and rules.</param>
/// <param name="InputJson">The evidence (pack items, earlier agents' findings, the goal) as JSON.</param>
/// <param name="JsonSchema">The strict JSON Schema the response must match.</param>
/// <param name="PromptVersion">The caller's own prompt version tag, echoed onto the result's
/// metadata the way <c>AiAnswerRequest.SystemPrompt</c>'s version is.</param>
public sealed record AiAnalysisRequest(
    string AgentName,
    string SystemPrompt,
    string InputJson,
    string JsonSchema,
    string PromptVersion);

/// <summary>The raw JSON payload the agent returned (schema-constrained, but not parsed or
/// validated by the gateway — the caller owns the shape) plus the call's metadata.</summary>
public sealed record AiAnalysisResult(string PayloadJson, AiCallMetadata Metadata);
