using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Raffa.AiFlows.MarketKnowledge.Estimation;
using Raffa.AiFlows.Negotiation.Tools;
using Raffa.Api.Tests.TestSupport;
using Raffa.SharedKernel.Market;

namespace Raffa.Api.Tests;

/// <summary>
/// The MarketKnowledge flow lives in <c>Raffa.AiFlows</c> but its consumers take it as an
/// optional port (<c>LineItemMarketPriceService</c> receives <see cref="IMarketPriceMatcher"/>?
/// and <see cref="IMarketPriceEstimator"/>?, the Ask flow skips the market researcher without an
/// <see cref="IMarketRagSearch"/>), so a host that forgot <c>AddAiFlows()</c> would degrade
/// silently instead of failing. This resolves them out of the Api host's real service provider.
/// </summary>
public sealed class MarketKnowledgeCompositionTests : IClassFixture<RaffaApiFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MarketKnowledgeCompositionTests(RaffaApiFactory factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "ConnectionStrings:DocumentsContracts",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true");
            builder.UseSetting(
                "ConnectionStrings:Chat",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true");
        });
    }

    [Fact]
    public void Host_resolves_the_market_price_matcher_and_estimator()
    {
        using var scope = _factory.Services.CreateScope();

        var matcher = scope.ServiceProvider.GetService<IMarketPriceMatcher>();
        var estimator = scope.ServiceProvider.GetService<IMarketPriceEstimator>();

        Assert.NotNull(matcher);
        Assert.NotNull(estimator);
        Assert.IsType<MarketPriceEstimator>(estimator);
    }

    [Fact]
    public void Host_resolves_the_market_researchers_rag_tool()
    {
        using var scope = _factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<IMarketRagSearch>());
    }
}
