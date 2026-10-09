using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Raffa.AiFlows.MarketKnowledge.Estimation;
using Raffa.Market;
using Raffa.SharedKernel.Market;

namespace Raffa.Worker.Tests;

/// <summary>
/// The Worker prices a contract's line items at extraction time
/// (<c>LineItemMarketPriceService</c> takes <see cref="IMarketPriceMatcher"/>? and
/// <see cref="IMarketPriceEstimator"/>? as optional dependencies), so a Worker composed without
/// the MarketKnowledge flow would silently price nothing. This composes the host the way
/// <c>Program.cs</c> does (<c>AddWorkerHost</c>, then <c>AddMarketModule</c>) and resolves both.
/// </summary>
public sealed class MarketKnowledgeCompositionTests
{
    [Fact]
    public void Worker_resolves_the_market_price_matcher_and_estimator()
    {
        var builder = Host.CreateApplicationBuilder();
        const string connectionString =
            "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true";
        builder.Services.AddWorkerHost(connectionString, connectionString, connectionString);
        builder.Services.AddMarketModule(null);

        using var host = builder.Build();
        using var scope = host.Services.CreateScope();

        var matcher = scope.ServiceProvider.GetService<IMarketPriceMatcher>();
        var estimator = scope.ServiceProvider.GetService<IMarketPriceEstimator>();

        Assert.NotNull(matcher);
        Assert.NotNull(estimator);
        Assert.IsType<MarketPriceEstimator>(estimator);
    }
}
