using Microsoft.Extensions.DependencyInjection;
using Raffa.AiFlows.QuoteExtraction;
using Raffa.AiFlows.QuoteExtraction.Agents;
using Raffa.AiFlows.QuoteExtraction.Orchestration;
using Raffa.Market;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel.Market;

namespace Raffa.AiFlows.Tests;

/// <summary>
/// <c>AddAiFlows</c> is the stable entry point the hosts call. So far it wires two flows: quote
/// extraction and MarketKnowledge (F7). A host that has not composed the module a flow sits on gets
/// nothing for that flow, so its container still validates.
/// </summary>
public sealed class AiFlowsServiceCollectionExtensionsTests
{
    private const string ConnectionString = "Host=localhost;Database=never-opened";

    [Fact]
    public void AddAiFlows_returns_the_same_collection_and_registers_only_the_market_flow_without_the_quotes_module()
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

    [Fact]
    public void AddAiFlows_registers_the_quote_extraction_flow_scoped_once_the_quotes_module_is_composed()
    {
        var services = new ServiceCollection();
        services.AddQuotesModule(ConnectionString);

        services.AddAiFlows();

        Assert.Equal(ServiceLifetime.Scoped, SingleDescriptor(services, typeof(QuoteLineExtractionService)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, SingleDescriptor(services, typeof(QuoteExtractionPipeline)).Lifetime);
    }

    [Fact]
    public void AddAiFlows_called_twice_still_registers_each_quote_flow_service_once()
    {
        var services = new ServiceCollection();
        services.AddQuotesModule(ConnectionString);

        services.AddAiFlows().AddAiFlows();

        SingleDescriptor(services, typeof(QuoteLineExtractionService));
        SingleDescriptor(services, typeof(QuoteExtractionPipeline));
    }

    [Fact]
    public void The_quotes_module_no_longer_registers_the_line_extraction_service_itself()
    {
        var services = new ServiceCollection();

        services.AddQuotesModule(ConnectionString);

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(QuoteLineExtractionService));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(QuoteExtractionPipeline));
    }

    [Fact]
    public void AddQuoteExtractionFlow_registers_nothing_when_the_quotes_module_is_absent()
    {
        var services = new ServiceCollection();

        services.AddQuoteExtractionFlow();

        Assert.Empty(services);
    }

    private static ServiceDescriptor SingleDescriptor(IServiceCollection services, Type serviceType) =>
        Assert.Single(services, d => d.ServiceType == serviceType);
}
