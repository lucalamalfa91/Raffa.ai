using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Raffa.AiFlows.CapabilityGaps.Drafting;
using Raffa.AiFlows.CapabilityGaps.Investigation;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Jev;

namespace Raffa.AiFlows.CapabilityGaps;

/// <summary>
/// Registrations of the capability-gaps flow (F4): the drafting workflow behind a `draft` reply
/// and the capability investigator. Called by
/// <see cref="AiFlowsServiceCollectionExtensions.AddAiFlows"/>.
/// </summary>
internal static class CapabilityGapsFlowServiceCollectionExtensions
{
    /// <summary>
    /// Registers the drafting workflow (ADR-030 D3): the offer planner and the negotiation writer
    /// behind a `draft` reply, with the same TryAdd-options / Scoped-service shape as the council
    /// (the host binds <c>Chat:Drafting</c> before calling <c>AddAiFlows</c>); and the capability
    /// investigator (ADR-031), which decides whether a fresh turn asks for a feature Raffa does not
    /// have.
    /// </summary>
    internal static IServiceCollection AddCapabilityGapsFlow(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(new DraftingOptions());
        services.AddScoped<NegotiationDraftingWorkflow>();

        // The verdict is decided by the Jev classify-role pilot when it is on (never the LLM --
        // CapabilityInvestigator's own doc comment); the analyst-role Foundry call only ever writes
        // a gap's free-text description. The host binds Chat:GapInvestigation (as IOptionsMonitor,
        // so the mode and the kill switch change without a restart) before calling this; absent
        // that, the defaults: Triggered, enabled.
        services.AddOptions<GapInvestigationOptions>();
        // AiGatewayJevOptions/JevHttpJsonClient are normally registered (config-bound) by
        // Raffa.AiGateway.ServiceCollectionExtensions.AddAiGatewayModule, called before this one by
        // every real host (same ordering IAiGateway itself already relies on). TryAdd here is the
        // same defensive fallback every other option in this method already gets -- a host or test
        // that calls only AddAiFlows still gets a safe, Jev-off default rather than a missing
        // registration (so this cannot be a config-binding factory here).
        services.TryAddSingleton(new AiGatewayJevOptions());
        services.TryAddSingleton(sp => new JevHttpJsonClient(
            new HttpClient { Timeout = TimeSpan.FromSeconds(180) },
            sp.GetRequiredService<AiGatewayJevOptions>()));
        services.TryAddSingleton<JevVerdictClient>();
        // A factory, not constructor selection: the investigator has a second, options-instance
        // constructor for tests and tools, and the container must not weigh the two.
        services.AddScoped(sp => new CapabilityInvestigator(
            sp.GetRequiredService<Raffa.AiGateway.IAiGateway>(),
            sp.GetRequiredService<IOptionsMonitor<GapInvestigationOptions>>(),
            sp.GetRequiredService<AiGatewayJevOptions>(),
            sp.GetRequiredService<JevVerdictClient>()));

        // Runs the investigator beside the answer (ADR-031); its follow-up is appended by Raffa.Api.
        services.AddSingleton<CapabilityCheckRunner>();

        return services;
    }
}
