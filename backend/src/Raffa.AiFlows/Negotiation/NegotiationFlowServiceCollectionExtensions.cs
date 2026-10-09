using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raffa.AiFlows.Negotiation.Agents;
using Raffa.AiFlows.Negotiation.Options;
using Raffa.AiFlows.Negotiation.Orchestration;

namespace Raffa.AiFlows.Negotiation;

/// <summary>
/// Registrations of the negotiation council flow (F2): the three analyst calls over the pack for
/// the savings/negotiation intents and Ask's agentic flow around them. Called by
/// <see cref="AiFlowsServiceCollectionExtensions.AddAiFlows"/>.
/// </summary>
internal static class NegotiationFlowServiceCollectionExtensions
{
    /// <summary>
    /// Registers the council, the market researcher and Ask's agentic flow, all Scoped like the
    /// rest of the per-request services. The options use <c>TryAdd</c> so a host that bound
    /// <c>Chat:Council</c> before calling <c>AddAiFlows</c> keeps its own values; the default is
    /// "on" with the documented bounds. The researcher's market RAG
    /// (<see cref="Tools.IMarketRagSearch"/>) is registered by the MarketKnowledge flow; without
    /// it the researcher step is skipped.
    /// </summary>
    internal static IServiceCollection AddNegotiationFlow(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(new CouncilOptions());
        services.AddScoped<NegotiationCouncil>();
        services.AddScoped<MarketResearcher>();
        services.AddScoped<AskAgentFlow>();

        return services;
    }
}
