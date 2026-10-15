using System.Collections.ObjectModel;
using Microsoft.Extensions.Options;

namespace Raffa.AiGateway.Agents;

/// <summary>One agent as the registry holds it: the flow it belongs to and its immutable definition
/// (name, version, prompt, hash, schema, deadline, policy).</summary>
public sealed record AgentRegistration
{
    public AgentRegistration(string flow, AgentDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flow);
        ArgumentNullException.ThrowIfNull(definition);
        Flow = flow;
        Definition = definition;
    }

    public string Flow { get; }

    public AgentDefinition Definition { get; }
}

/// <summary>The on/off state of one agent or one flow. Default on.</summary>
public sealed class AgentSwitchOptions
{
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Bound from <c>Agents</c> (<see cref="SectionName"/>) and watched through
/// <see cref="IOptionsMonitor{TOptions}"/>, so a kill switch flips without a restart (plan A-02):
/// <c>Agents:Agents:contract-analyst:Enabled=false</c> turns one agent off,
/// <c>Agents:Flows:council:Enabled=false</c> a whole flow. An agent that is not mentioned is on, so
/// an empty section changes nothing.
/// </summary>
public sealed class AgentRegistryOptions
{
    public const string SectionName = "Agents";

    public Dictionary<string, AgentSwitchOptions> Agents { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, AgentSwitchOptions> Flows { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>A batch of registrations resolved from the container, for definitions whose parameters
/// (a deadline) come from the flow's own options; see <c>AddAgentDefinitions</c>.</summary>
public interface IAgentRegistrationSource
{
    IReadOnlyList<AgentRegistration> Registrations { get; }
}

/// <summary>
/// The catalog of every agent of the platform (plan 3.2, A-02, T-06): which agents exist, per flow,
/// at which version and with which prompt hash, and whether each is switched on. It is Singleton and
/// read-only after construction; only the kill switches move, through the options monitor.
///
/// <para>
/// <b>Kill switch.</b> <see cref="IsEnabled"/> is consulted by <see cref="StepRunner"/> before an
/// agent is called; a disabled agent ends as the step's own failure policy dictates (a skipped
/// analyst, a failed required step, the declared fallback) with reason
/// <see cref="StepFailureReason.Disabled"/>, audited like any other degrade. A flow's pre-existing
/// switch (<c>CouncilOptions.Enabled</c>) keeps working; the registry switch is an additional,
/// uniform one, default on.
/// </para>
///
/// <para>
/// <b>Drift.</b> <see cref="Validate"/> aggregates every definition's own
/// <see cref="AgentDefinition.Validate"/> (strict schema, prompt hash against the registered hash), so
/// one test per host or flow keeps every prompt file honest. Registering the same agent name twice is
/// a configuration error.
/// </para>
/// </summary>
public sealed class AgentRegistry
{
    private readonly IOptionsMonitor<AgentRegistryOptions> _options;
    private readonly Dictionary<string, AgentRegistration> _byName = new(StringComparer.Ordinal);

    public AgentRegistry(
        IEnumerable<AgentRegistration> registrations,
        IOptionsMonitor<AgentRegistryOptions> options,
        IEnumerable<IAgentRegistrationSource>? sources = null)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(options);
        _options = options;

        foreach (var registration in registrations.Concat(sources?.SelectMany(s => s.Registrations) ?? []))
        {
            if (!_byName.TryAdd(registration.Definition.Name, registration))
            {
                var existing = _byName[registration.Definition.Name];
                throw new InvalidOperationException(
                    $"Agent '{registration.Definition.Name}' is registered twice " +
                    $"(flow '{existing.Flow}' {existing.Definition.Version}, flow '{registration.Flow}' {registration.Definition.Version}).");
            }
        }

        All = new ReadOnlyCollection<AgentRegistration>(
            _byName.Values
                .OrderBy(r => r.Flow, StringComparer.Ordinal)
                .ThenBy(r => r.Definition.Name, StringComparer.Ordinal)
                .ToList());
    }

    /// <summary>Every registered agent, ordered by flow then name.</summary>
    public IReadOnlyList<AgentRegistration> All { get; }

    public IReadOnlyList<AgentRegistration> ForFlow(string flow) =>
        All.Where(r => string.Equals(r.Flow, flow, StringComparison.OrdinalIgnoreCase)).ToList();

    public bool TryGet(string agentName, out AgentRegistration registration)
    {
        if (agentName is not null && _byName.TryGetValue(agentName, out var found))
        {
            registration = found;
            return true;
        }

        registration = null!;
        return false;
    }

    /// <summary>The registration of <paramref name="agentName"/>.</summary>
    /// <exception cref="KeyNotFoundException">The agent is not registered.</exception>
    public AgentRegistration Get(string agentName) =>
        TryGet(agentName, out var registration)
            ? registration
            : throw new KeyNotFoundException($"Agent '{agentName}' is not registered.");

    /// <summary>Whether the agent may run now: its own switch and its flow's switch are on. An agent
    /// that is not registered is on (the registry never blocks what it does not know).</summary>
    public bool IsEnabled(string agentName) => DisabledReason(agentName) is null;

    /// <summary>Why the agent is off, or <see langword="null"/> when it is on; for the audit line.</summary>
    public string? DisabledReason(string agentName)
    {
        if (!TryGet(agentName, out var registration))
        {
            return null;
        }

        var current = _options.CurrentValue;
        if (!IsOn(current.Agents, agentName))
        {
            return $"agent '{agentName}' is switched off (Agents:Agents:{agentName}:Enabled).";
        }

        return IsOn(current.Flows, registration.Flow)
            ? null
            : $"flow '{registration.Flow}' is switched off (Agents:Flows:{registration.Flow}:Enabled).";
    }

    /// <summary>Every problem of every registered definition (empty when sound); the T-06 drift test.</summary>
    public IReadOnlyList<string> Validate() => All.SelectMany(r => r.Definition.Validate()).ToList();

    private static bool IsOn(Dictionary<string, AgentSwitchOptions>? switches, string key) =>
        switches is null || !switches.TryGetValue(key, out var entry) || entry is null || entry.Enabled;
}
