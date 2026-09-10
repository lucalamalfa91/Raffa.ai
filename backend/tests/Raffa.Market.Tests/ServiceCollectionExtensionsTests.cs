using Raffa.Benchmark;
using Raffa.Benchmark.Adapters;
using Raffa.Benchmark.Configuration;
using Raffa.Benchmark.Contracts;
using Raffa.Market.Benchmark;
using Raffa.Market.Mock;
using Raffa.Market.Retrieval;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Market.Tests;

/// <summary>
/// Proves task E13/F02/US01/T01's own wiring claims: <see cref="ServiceCollectionExtensions.AddMarketModule"/>
/// registers a working mock feed, notes retrieval, and a <see cref="MarketFeedBenchmarkAdapter"/>
/// that becomes the <em>default</em> active <c>Raffa.Benchmark</c> adapter — without editing
/// <c>Raffa.Benchmark</c> — regardless of whether the host calls <c>AddBenchmarkModule()</c> or
/// <see cref="ServiceCollectionExtensions.AddMarketModule"/> first (see that method's own
/// <c>MakeMarketFeedTheDefaultActiveAdapter</c> doc comment for why order cannot matter here).
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddMarketModule_returns_the_same_service_collection_for_fluent_chaining()
    {
        var services = new ServiceCollection();

        var result = services.AddMarketModule();

        Assert.Same(services, result);
    }

    [Fact]
    public void AddMarketModule_resolves_the_mock_provider_and_in_memory_retrieval()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddMarketModule();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<MockMarketIntelligenceProvider>(provider.GetRequiredService<IMarketIntelligenceProvider>());
        Assert.IsType<InMemoryMarketKnowledgeRetrieval>(provider.GetRequiredService<IMarketKnowledgeRetrieval>());
    }

    [Fact]
    public void AddMarketModule_registers_market_feed_alongside_fixture_not_instead_of_it()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddBenchmarkModule();
        services.AddMarketModule();
        using var provider = services.BuildServiceProvider();

        var adapterNames = provider.GetServices<IBenchmarkProviderAdapter>().Select(a => a.Name).ToList();

        Assert.Contains(BenchmarkAdapterOptions.DefaultAdapterName, adapterNames); // "fixture" — still registered.
        Assert.Contains(MarketFeedBenchmarkAdapter.AdapterName, adapterNames);     // "market-feed" — newly added.
    }

    [Fact]
    public void AddMarketModule_after_AddBenchmarkModule_makes_market_feed_the_default_active_adapter()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddBenchmarkModule(); // registers the "fixture"-defaulting BenchmarkAdapterOptions factory first.
        services.AddMarketModule();    // must still end up owning the default.

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<BenchmarkAdapterOptions>();

        Assert.Equal(MarketFeedBenchmarkAdapter.AdapterName, options.ActiveAdapter);
    }

    [Fact]
    public void AddBenchmarkModule_after_AddMarketModule_still_leaves_market_feed_the_default_active_adapter()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddMarketModule();    // installs the "market-feed"-defaulting factory first.
        services.AddBenchmarkModule(); // TryAddSingleton must see it already registered and no-op.

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<BenchmarkAdapterOptions>();

        Assert.Equal(MarketFeedBenchmarkAdapter.AdapterName, options.ActiveAdapter);
    }

    [Fact]
    public void An_explicit_active_adapter_configuration_still_overrides_the_market_feed_default()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{BenchmarkAdapterOptions.SectionName}:ActiveAdapter"] = BenchmarkAdapterOptions.DefaultAdapterName,
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddBenchmarkModule();
        services.AddMarketModule();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<BenchmarkAdapterOptions>();

        Assert.Equal(BenchmarkAdapterOptions.DefaultAdapterName, options.ActiveAdapter);
    }

    [Fact]
    public async Task IBenchmarkService_dispatches_to_the_market_feed_adapter_by_default()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddBenchmarkModule();
        services.AddMarketModule();
        using var provider = services.BuildServiceProvider();

        var benchmarkService = provider.GetRequiredService<IBenchmarkService>();
        Assert.IsType<BenchmarkAdapterRegistry>(benchmarkService);

        var result = await benchmarkService.GetBenchmarkAsync(new BenchmarkQuery(
            Supplier: "Allianz",
            Product: "Commercial Property Insurance",
            Sku: null,
            Geography: "CH",
            Quantity: 1m,
            Term: "12 months",
            Currency: "CHF",
            PurchaseDate: new DateOnly(2026, 6, 1)));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.HasSufficientData);
        // The fixture adapter has no Allianz rows at all (its own catalog is US SaaS only) — a
        // confident Allianz result can only have come from the market-feed adapter, proving the
        // registry really did dispatch to it, not merely that BenchmarkAdapterOptions says so.
        Assert.Equal("market-feed (representative, mock)", result.Value.Source);
        Assert.NotEqual(BenchmarkAdapterOptions.DefaultAdapterName, result.Value.Source);
    }
}
