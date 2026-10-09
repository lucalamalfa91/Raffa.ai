using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raffa.AiFlows.WebResearch.Configuration;
using Raffa.AiFlows.WebResearch.Orchestration;
using Raffa.Chat.Application.WebResearch;

namespace Raffa.AiFlows.WebResearch;

/// <summary>
/// Registrations of the web-research flow (F3, ADR-030): the options (kill switch, daily budget),
/// the composer behind the research role, and the budget port that links the options to the
/// persistence in <c>Raffa.Chat</c>. Called by
/// <see cref="AiFlowsServiceCollectionExtensions.AddAiFlows"/>.
/// </summary>
internal static class WebResearchFlowServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="WebResearchOptions"/> (default <c>Enabled=false</c>, the kill switch, so a
    /// host that never binds <c>Chat:WebResearch</c> has no web path at all; <c>TryAdd</c> so a value
    /// the host registered first wins), the <see cref="WebResearchComposer"/> and
    /// <see cref="IWebResearchBudgetLimit"/> pointing at the options.
    /// </summary>
    internal static IServiceCollection AddWebResearchFlow(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(new WebResearchOptions());

        // Replaces the closed default AddChatModule registers. A factory, not an instance: it
        // resolves the options lazily, so a host or test that swaps WebResearchOptions after this
        // call (RemoveAll + AddSingleton) is honoured by the budget.
        services.Replace(ServiceDescriptor.Singleton<IWebResearchBudgetLimit>(
            sp => sp.GetRequiredService<WebResearchOptions>()));

        services.AddScoped<WebResearchComposer>();

        return services;
    }
}
