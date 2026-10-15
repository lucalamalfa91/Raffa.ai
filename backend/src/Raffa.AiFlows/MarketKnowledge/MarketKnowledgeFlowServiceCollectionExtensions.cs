using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raffa.AiFlows.MarketKnowledge.Estimation;
using Raffa.AiFlows.MarketKnowledge.Ingestion;
using Raffa.AiFlows.MarketKnowledge.Retrieval;
using Raffa.AiGateway.Agents;
using Raffa.Market.Ingestion;
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

        // A-11 / F7-T09: the market ingest batch's data-quality agent and provenance curator
        // (Market:Ingestion:Curation, off by default: the curator is then a pass-through and the
        // ingestion job makes no AI call for it). Resolved only by the ingestion job.
        services.TryAddSingleton(sp =>
        {
            var options = new MarketCurationOptions();
            sp.GetService<IConfiguration>()?.GetSection(MarketCurationOptions.SectionName).Bind(options);
            return options;
        });
        services.TryAddScoped<IMarketBatchCurator>(sp => new MarketBatchCurator(
            sp.GetRequiredService<StepRunner>(),
            sp.GetRequiredService<MarketCurationOptions>(),
            sp.GetRequiredService<Raffa.SharedKernel.IClock>()));

        return services;
    }
}
