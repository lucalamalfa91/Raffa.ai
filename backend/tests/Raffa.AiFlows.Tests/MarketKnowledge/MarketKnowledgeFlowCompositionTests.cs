using Microsoft.Extensions.DependencyInjection;
using Raffa.AiFlows.MarketKnowledge.Estimation;
using Raffa.AiFlows.MarketKnowledge.Retrieval;
using Raffa.Chat.Application.Council;
using Raffa.Market;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Market;

namespace Raffa.AiFlows.Tests.MarketKnowledge;

/// <summary>
/// The MarketKnowledge flow over the Market module's data layer: <c>AddMarketModule</c> keeps the
/// deterministic matcher and the deal lookup, <c>AddAiFlows</c> adds the estimator (gateway
/// optional) and the researcher's RAG tool on top.
/// </summary>
public sealed class MarketKnowledgeFlowCompositionTests
{
    [Fact]
    public void The_matcher_stays_in_the_market_module_and_the_estimator_comes_from_the_flow()
    {
        var services = new ServiceCollection();
        services.AddMarketModule();
        services.AddAiFlows();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var matcher = scope.ServiceProvider.GetRequiredService<IMarketPriceMatcher>();
        var estimator = scope.ServiceProvider.GetRequiredService<IMarketPriceEstimator>();

        Assert.IsType<Raffa.Market.Retrieval.MarketPriceMatcher>(matcher);
        Assert.IsType<MarketPriceEstimator>(estimator);
    }

    [Fact]
    public void The_estimator_resolves_without_a_gateway()
    {
        // No IAiGateway registered: the estimator is built with a null gateway (no AI step).
        var services = new ServiceCollection();
        services.AddMarketModule();
        services.AddAiFlows();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Null(scope.ServiceProvider.GetService<Raffa.AiGateway.IAiGateway>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMarketPriceEstimator>());
    }

    [Fact]
    public async Task The_estimator_without_a_gateway_returns_no_estimate_for_a_line_nothing_converts()
    {
        var services = new ServiceCollection();
        services.AddMarketModule();
        services.AddAiFlows();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var estimator = scope.ServiceProvider.GetRequiredService<IMarketPriceEstimator>();

        var estimate = Assert.Single(await estimator.EstimateAsync(
            new MarketPriceContext("Coupa Software", "CHF", 12),
            [new MarketPriceLine("Coupa Procure-to-Pay named users / licenses", null)],
            CancellationToken.None));

        Assert.Null(estimate);
    }

    [Fact]
    public void A_host_registered_estimator_wins_over_the_flows_default()
    {
        var services = new ServiceCollection();
        var custom = new CustomEstimator();
        services.AddSingleton<IMarketPriceEstimator>(custom);
        services.AddMarketModule();
        services.AddAiFlows();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Same(custom, scope.ServiceProvider.GetRequiredService<IMarketPriceEstimator>());
    }

    [Fact]
    public void The_researchers_rag_tool_is_the_flows_search_over_the_market_retrieval()
    {
        var services = new ServiceCollection();
        services.AddMarketModule();
        services.AddAiFlows();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<MarketRagSearch>(scope.ServiceProvider.GetRequiredService<IMarketRagSearch>());
    }

    private sealed class CustomEstimator : IMarketPriceEstimator
    {
        public Task<IReadOnlyList<MarketPriceEstimate?>> EstimateAsync(
            MarketPriceContext context, IReadOnlyList<MarketPriceLine> lines, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
