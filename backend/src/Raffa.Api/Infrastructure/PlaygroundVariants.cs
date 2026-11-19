using Raffa.AiFlows.Ask.Agentic;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Telemetry;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// What a playground variant may change, and the check that keeps it inside that: only the agents, gateway
/// roles, deployments and tools that exist in this environment, with values the gateway accepts. A variant
/// that names a deployment nobody configured, an unknown tool or an out-of-range knob is refused with the
/// reason, never silently dropped.
/// </summary>
internal sealed class PlaygroundCatalog(AiGatewayModelOptions models, IEnumerable<IAskTool> tools)
{
    public const int MaxPromptChars = 40_000;
    public const double MaxTemperature = 0.2;

    /// <summary>Agents whose system prompt a variant can replace (grows with each playground release).</summary>
    public static IReadOnlyList<string> PromptAgents { get; } = ["agent"];

    public static IReadOnlyList<string> Roles { get; } = ["agent", "answer", "analyst", "extract", "classify", "research"];

    public static IReadOnlyList<string> ReasoningEfforts { get; } = ["none", "minimal", "low", "medium", "high"];

    public IReadOnlyList<IAskTool> Tools { get; } = [.. tools];

    /// <summary>The configured selection of each chat role, with the role it falls back to when it has none of its own.</summary>
    public IReadOnlyList<(string Role, AiModelSelection? Selection, string? FallbackOf)> RoleSelections() =>
    [
        ("agent", models.Agent ?? models.Answer, models.Agent is null ? "answer" : null),
        ("answer", models.Answer, null),
        ("analyst", models.Analyst ?? models.Answer, models.Analyst is null ? "answer" : null),
        ("extract", models.Extract, null),
        ("classify", models.Classify, null),
        ("research", models.Research, null),
    ];

    /// <summary>Deployments a variant may pick: those already configured for a chat role of this environment.</summary>
    public IReadOnlyList<string> AllowedModelIds() =>
        [.. RoleSelections().Where(r => r.Selection is not null).Select(r => r.Selection!.ModelId).Distinct(StringComparer.Ordinal)];

    /// <summary>The first problem with <paramref name="name"/>/<paramref name="overrides"/>, or <see langword="null"/>; the cleaned overrides otherwise.</summary>
    public (string? Error, PlaygroundOverrides Clean) Validate(string? name, string? description, PlaygroundOverrides? overrides)
    {
        var empty = new PlaygroundOverrides();
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
        {
            return ("'name' is required and at most 120 characters.", empty);
        }

        if (description is { Length: > 500 })
        {
            return ("'description' is at most 500 characters.", empty);
        }

        overrides ??= empty;
        var prompts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (agent, text) in overrides.Prompts ?? new Dictionary<string, string>())
        {
            if (!PromptAgents.Contains(agent, StringComparer.Ordinal))
            {
                return ($"Prompt override for unknown agent '{agent}'. Known: {string.Join(", ", PromptAgents)}.", empty);
            }

            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxPromptChars)
            {
                return ($"The '{agent}' prompt must be non-empty and at most {MaxPromptChars} characters.", empty);
            }

            prompts[agent] = text;
        }

        var allowed = AllowedModelIds();
        var modelChanges = new Dictionary<string, PlaygroundModelOverride>(StringComparer.Ordinal);
        foreach (var (role, change) in overrides.Models ?? new Dictionary<string, PlaygroundModelOverride>())
        {
            var selection = RoleSelections().FirstOrDefault(r => r.Role == role);
            if (selection.Role is null)
            {
                return ($"Unknown role '{role}'. Known: {string.Join(", ", Roles)}.", empty);
            }

            if (selection.Selection is null)
            {
                return ($"The '{role}' role is not configured in this environment.", empty);
            }

            var modelId = string.IsNullOrWhiteSpace(change.ModelId) ? null : change.ModelId.Trim();
            if (modelId is not null && !allowed.Contains(modelId, StringComparer.Ordinal))
            {
                return ($"Deployment '{modelId}' is not configured in this environment. Available: {string.Join(", ", allowed)}.", empty);
            }

            var effort = string.IsNullOrWhiteSpace(change.ReasoningEffort) ? null : change.ReasoningEffort.Trim().ToLowerInvariant();
            if (effort is not null && !ReasoningEfforts.Contains(effort, StringComparer.Ordinal))
            {
                return ($"reasoningEffort '{effort}' must be one of {string.Join("/", ReasoningEfforts)}.", empty);
            }

            if (change.MaxCompletionTokens is { } tokens && (tokens < 256 || tokens > 65_536))
            {
                return ("maxCompletionTokens must be between 256 and 65536.", empty);
            }

            if (change.Temperature is { } temperature && (temperature < 0 || temperature > MaxTemperature))
            {
                return ($"temperature must be between 0 and {MaxTemperature} (ADR-024).", empty);
            }

            var clean = new PlaygroundModelOverride(modelId, effort, change.MaxCompletionTokens, change.Temperature);
            if (clean != new PlaygroundModelOverride(null, null, null, null))
            {
                modelChanges[role] = clean;
            }
        }

        var disabled = new List<string>();
        foreach (var tool in (overrides.DisabledTools ?? []).Distinct(StringComparer.Ordinal))
        {
            if (!Tools.Any(t => t.Name == tool))
            {
                return ($"Unknown tool '{tool}'.", empty);
            }

            disabled.Add(tool);
        }

        if (overrides.MaxToolCalls is { } max && (max < 1 || max > 16))
        {
            return ("maxToolCalls must be between 1 and 16.", empty);
        }

        return (null, new PlaygroundOverrides(prompts, modelChanges, disabled, overrides.MaxToolCalls));
    }
}
