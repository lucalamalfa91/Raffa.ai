using Microsoft.Extensions.DependencyInjection;
using Raffa.Market;
using Raffa.SharedKernel.Market;

namespace Raffa.AiFlows.Tests;

/// <summary>
/// <c>AddAiFlows</c> is the stable entry point the hosts call. So far only the MarketKnowledge
/// flow registers behind it; the flows that have not moved in yet register nothing.
/// </summary>
public sealed class AiFlowsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAiFlows_returns_the_same_collection_and_registers_only_the_flows_that_moved_in()
    {
        var services = new ServiceCollection();

        var returned = services.AddAiFlows();

        Assert.Same(services, returned);
        Assert.Contains(services, d => d.ServiceType == typeof(IMarketPriceEstimator));
        Assert.Contains(services, d => d.ServiceType == typeof(Raffa.Chat.Application.Council.IMarketRagSearch));
        Assert.Equal(2, services.Count);
    }

    [Fact]
    public void AddAiFlows_is_safe_to_call_twice()
    {
        var services = new ServiceCollection();

        // The market researcher's tool reads the Market module's retrieval, which a host supplies.
        services.AddMarketModule();
        services.AddAiFlows().AddAiFlows();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        Assert.NotNull(provider);
    }

    [Fact]
    public void AddAiFlows_rejects_a_null_collection()
    {
        Assert.Throws<ArgumentNullException>(() => AiFlowsServiceCollectionExtensions.AddAiFlows(null!));
    }
}
