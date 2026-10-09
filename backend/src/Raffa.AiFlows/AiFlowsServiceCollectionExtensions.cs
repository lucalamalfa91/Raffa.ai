using Microsoft.Extensions.DependencyInjection;

namespace Raffa.AiFlows;

/// <summary>
/// Composition-root wiring for the AI flows layer (ADR-002 amendment: <c>Raffa.AiFlows</c> sits
/// above the domain modules and is referenced only by the hosts). Each flow gets its own
/// <c>AddXxxFlow</c> registration method that this entry point calls; the hosts
/// (<c>Raffa.Api</c>'s <c>Program.cs</c> and <c>Raffa.Worker</c>'s <c>AddWorkerHost</c>) call
/// <see cref="AddAiFlows"/> once.
/// Currently empty: no flow lives here yet, so the call registers nothing and changes no
/// behaviour. The flows move in one at a time in later steps.
/// </summary>
public static class AiFlowsServiceCollectionExtensions
{
    /// <summary>Registers every AI flow's services. Safe to call from more than one host path.</summary>
    public static IServiceCollection AddAiFlows(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services;
    }
}
