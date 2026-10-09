using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raffa.AiFlows.CapabilityGaps.Drafting;

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
    /// behind a `draft` reply. Same TryAdd-options / Scoped-service shape as the council; the
    /// host binds <c>Chat:Drafting</c> before calling <c>AddAiFlows</c>.
    /// </summary>
    internal static IServiceCollection AddCapabilityGapsFlow(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(new DraftingOptions());
        services.AddScoped<NegotiationDraftingWorkflow>();

        return services;
    }
}
