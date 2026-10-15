using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Raffa.AiGateway.Agents;

/// <summary>Registration helpers of the <see cref="AgentRegistry"/> (plan A-02).</summary>
public static class AgentRegistryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Singleton <see cref="AgentRegistry"/> and its options. Idempotent
    /// (<c>TryAdd</c>), so a flow may call it defensively. With <paramref name="configuration"/>
    /// the kill switches are bound from its <c>Agents</c> section and follow its reloads; without it
    /// every agent is on.
    /// </summary>
    public static IServiceCollection AddAgentRegistry(this IServiceCollection services, IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions();
        if (!services.Any(d => d.ServiceType == typeof(IOptionsChangeTokenSource<AgentRegistryOptions>)))
        {
            // Bound lazily from the explicit configuration, else the container's IConfiguration, so the
            // gateway module can register the registry without knowing the host's configuration.
            services.AddSingleton<IConfigureOptions<AgentRegistryOptions>>(sp =>
                new ConfigureNamedOptions<AgentRegistryOptions>(
                    Options.DefaultName,
                    o => (configuration ?? sp.GetService<IConfiguration>())
                        ?.GetSection(AgentRegistryOptions.SectionName).Bind(o)));
            services.AddSingleton<IOptionsChangeTokenSource<AgentRegistryOptions>>(sp =>
                new SectionChangeTokenSource((configuration ?? sp.GetService<IConfiguration>())
                    ?.GetSection(AgentRegistryOptions.SectionName)));
        }

        services.TryAddSingleton<AgentRegistry>();
        return services;
    }

    /// <summary>How a flow registers its agents: one call with the flow name and its definitions.
    /// Definitions are Singleton data (see <see cref="AgentDefinition"/>), built once.</summary>
    public static IServiceCollection AddAgentDefinitions(
        this IServiceCollection services, string flow, params AgentDefinition[] definitions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definitions);
        foreach (var definition in definitions)
        {
            services.AddSingleton(new AgentRegistration(flow, definition));
        }

        return services;
    }

    /// <summary>Like the array overload but built from the container, for definitions whose deadline
    /// (or other parameter) comes from the flow's own options.</summary>
    public static IServiceCollection AddAgentDefinitions(
        this IServiceCollection services, string flow, Func<IServiceProvider, IEnumerable<AgentDefinition>> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(flow);
        ArgumentNullException.ThrowIfNull(factory);
        services.AddSingleton<IAgentRegistrationSource>(sp => new RegistrationSource(
            factory(sp).Select(d => new AgentRegistration(flow, d)).ToList()));
        return services;
    }

    private sealed class RegistrationSource(IReadOnlyList<AgentRegistration> registrations) : IAgentRegistrationSource
    {
        public IReadOnlyList<AgentRegistration> Registrations { get; } = registrations;
    }

    private sealed class SectionChangeTokenSource(IConfigurationSection? section) : IOptionsChangeTokenSource<AgentRegistryOptions>
    {
        public string Name => Options.DefaultName;

        public IChangeToken GetChangeToken() => section?.GetReloadToken() ?? new CancellationChangeToken(CancellationToken.None);
    }
}
