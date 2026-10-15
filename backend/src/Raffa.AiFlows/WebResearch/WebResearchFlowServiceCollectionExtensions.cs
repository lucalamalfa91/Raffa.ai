using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Raffa.AiFlows.WebResearch.Configuration;
using Raffa.AiFlows.WebResearch.Orchestration;
using Raffa.AiGateway;
using Raffa.AiGateway.Logging;
using Raffa.Chat.Application.WebResearch;
using Raffa.SharedKernel;

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

        // F3-T10: the kill switch and the other settings are read through IOptionsMonitor, so a
        // configuration change applies without a restart. A closed registration of the monitor over the
        // registered options singleton (resolved lazily, so a swapped instance is honoured); TryAdd so a
        // host that registered its own monitor wins.
        services.TryAddSingleton<IOptionsMonitor<WebResearchOptions>>(sp => new WebResearchOptionsMonitor(
            sp.GetRequiredService<WebResearchOptions>(), sp.GetService<IConfiguration>()));

        // A factory, not constructor selection: the composer has a second, options-instance constructor
        // for tests and tools, and the container must not weigh the two.
        services.AddScoped(sp => new WebResearchComposer(
            sp.GetRequiredService<IAiGateway>(),
            sp.GetRequiredService<IOptionsMonitor<WebResearchOptions>>(),
            sp.GetRequiredService<IClock>()));

        // The web-research and web-mode halves of Ask (ADR-030 / ADR-032), consumed by AskCopilotService.
        // F3-T11: the same keyed query hasher the AI gateway registers (TryAdd: whichever module runs
        // first registers the one config-bound instance), so the flow's audit rows and the gateway's
        // ai_call row carry the same queryHash.
        services.TryAddSingleton<IQueryHasher>(sp => new KeyedQueryHasher(
            sp.GetService<IConfiguration>() is { } configuration
                ? () => configuration[KeyedQueryHasher.KeyConfigurationPath]
                : () => null,
            sp.GetService<ILogger<KeyedQueryHasher>>()));
        services.AddScoped<WebResearchFlow>();
        services.AddScoped<WebModeFlow>();

        return services;
    }
}
