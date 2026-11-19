using Raffa.AiGateway.Configuration;

namespace Raffa.AiGateway.Telemetry;

/// <summary>Per-role model knobs a playground variant replaces; a <see langword="null"/> field keeps the configured value.</summary>
public sealed record PlaygroundModelOverride(string? ModelId, string? ReasoningEffort, int? MaxCompletionTokens, double? Temperature);

/// <summary>
/// What a playground variant changes for the duration of one run: the text of an agent's system prompt
/// (by agent name), the model knobs of a gateway role (by role name: <c>agent</c>, <c>answer</c>,
/// <c>analyst</c>, <c>extract</c>, <c>classify</c>, <c>research</c>), the Ask tools taken away from the
/// orchestrator and its tool-call limit. Everything else is exactly what production runs.
/// </summary>
public sealed record PlaygroundOverrides(
    IReadOnlyDictionary<string, string>? Prompts = null,
    IReadOnlyDictionary<string, PlaygroundModelOverride>? Models = null,
    IReadOnlyCollection<string>? DisabledTools = null,
    int? MaxToolCalls = null)
{
    public bool IsEmpty =>
        (Prompts?.Count ?? 0) == 0 && (Models?.Count ?? 0) == 0 && (DisabledTools?.Count ?? 0) == 0 && MaxToolCalls is null;
}

/// <summary>
/// Ambient (<see cref="AsyncLocal{T}"/>) variant of the current playground run. Opened only by the
/// platform-admin playground endpoints after the platform-admin guard, so on every other request it is
/// <see langword="null"/> and each seam below returns the configured value untouched. The seams are the
/// only places a variant can act: the orchestrator's prompt and tool list, and the per-role model choice
/// of the Foundry clients. Nothing is written to disk or configuration: the override ends with the run.
/// </summary>
public static class PlaygroundScope
{
    private static readonly AsyncLocal<PlaygroundOverrides?> Ambient = new();

    public static PlaygroundOverrides? Current => Ambient.Value;

    public static IDisposable Begin(PlaygroundOverrides overrides)
    {
        var previous = Ambient.Value;
        Ambient.Value = overrides;
        return new Scope(previous);
    }

    /// <summary>The variant's text for <paramref name="agent"/>'s system prompt, or <see langword="null"/> for the registered one.</summary>
    public static string? PromptFor(string agent) =>
        Ambient.Value?.Prompts is { } prompts && prompts.TryGetValue(agent, out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    /// <summary>True when a variant replaces <paramref name="agent"/>'s prompt (the audit then names the run as modified).</summary>
    public static bool HasPrompt(string agent) => PromptFor(agent) is not null;

    public static bool IsToolDisabled(string tool) => Ambient.Value?.DisabledTools?.Contains(tool, StringComparer.Ordinal) == true;

    public static int MaxToolCallsOr(int configured) => Ambient.Value?.MaxToolCalls ?? configured;

    /// <summary>The configured selection of <paramref name="role"/> with the variant's knobs applied.</summary>
    public static AiModelSelection ModelFor(string role, AiModelSelection configured)
    {
        if (Ambient.Value?.Models is not { } models || !models.TryGetValue(role, out var change))
        {
            return configured;
        }

        return configured with
        {
            ModelId = string.IsNullOrWhiteSpace(change.ModelId) ? configured.ModelId : change.ModelId,
            ReasoningEffort = change.ReasoningEffort ?? configured.ReasoningEffort,
            MaxCompletionTokens = change.MaxCompletionTokens ?? configured.MaxCompletionTokens,
            Temperature = change.Temperature ?? configured.Temperature,
        };
    }

    private sealed class Scope(PlaygroundOverrides? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
