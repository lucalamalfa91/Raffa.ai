using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raffa.AiFlows.MarketKnowledge.Estimation;
using Raffa.AiFlows.MarketKnowledge.Retrieval;
using Raffa.Market.Retrieval;
using Raffa.SharedKernel.Market;

namespace Raffa.AiFlows.MarketKnowledge;

/// <summary>
/// Registrations of the MarketKnowledge flow (F7): the AI logic over the market corpus. The data
/// layer (ingestion, pgvector retrieval, the deal lookup, the deterministic <c>IMarketPriceMatcher</c>,
/// entities, migrations) stays in <c>Raffa.Market</c> and is registered by <c>AddMarketModule</c>,
/// which a host calls itself; this flow only adds what sits on top of it.
/// </summary>
internal static class MarketKnowledgeFlowServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IMarketPriceEstimator"/> (the last resort behind the market column,
    /// for lines the matcher could not price: a converted band, else an AI estimate; the gateway is
    /// optional, no gateway means no AI step) with the same factory and <c>TryAddScoped</c> shape
    /// <c>AddMarketModule</c> used before the estimator moved here, and the market researcher's
    /// tool <c>IMarketRagSearch</c> (over <see cref="IMarketKnowledgeRetrieval"/>; the Ask flow
    /// skips the researcher when it is not registered).
    /// </summary>
    internal static IServiceCollection AddMarketKnowledgeFlow(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IMarketPriceEstimator>(sp => new MarketPriceEstimator(
            sp.GetRequiredService<IMarketDealLookup>(),
            sp.GetService<Raffa.AiGateway.IAiGateway>(),
            sp.GetService<Microsoft.Extensions.Logging.ILogger<MarketPriceEstimator>>()));

        services.AddScoped<Negotiation.Tools.IMarketRagSearch, MarketRagSearch>();

        return services;
    }
}
