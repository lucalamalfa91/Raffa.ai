using Microsoft.Extensions.DependencyInjection;
using Raffa.AiFlows.DocumentExtraction;
using Raffa.AiFlows.MarketKnowledge;
using Raffa.AiFlows.QuoteExtraction;

namespace Raffa.AiFlows;

/// <summary>
/// Composition-root wiring for the AI flows layer (ADR-002 amendment: <c>Raffa.AiFlows</c> sits
/// above the domain modules and is referenced only by the hosts). Each flow gets its own
/// <c>AddXxxFlow</c> registration method that this entry point calls; the hosts
/// (<c>Raffa.Api</c>'s <c>Program.cs</c> and <c>Raffa.Worker</c>'s <c>AddWorkerHost</c>) call
/// <see cref="AddAiFlows"/> once.
/// The flows move in one at a time; so far: document extraction (<c>AddDocumentExtractionFlow</c>),
/// MarketKnowledge (F7, <c>AddMarketKnowledgeFlow</c>) and quote extraction (<c>AddQuoteExtractionFlow</c>).
/// </summary>
public static class AiFlowsServiceCollectionExtensions
{
    /// <summary>Registers every AI flow's services. Safe to call from more than one host path.</summary>
    public static IServiceCollection AddAiFlows(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDocumentExtractionFlow();
        services.AddMarketKnowledgeFlow();
        services.AddQuoteExtractionFlow();

        return services;
    }
}
