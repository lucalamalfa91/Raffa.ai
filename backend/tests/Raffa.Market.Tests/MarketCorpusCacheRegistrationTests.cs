using Raffa.Benchmark.Adapters;
using Raffa.Market.Retrieval;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Market.Tests;

/// <summary>The corpus cache as the host composes it (<c>AddMarketModule</c> with a Market connection
/// string): one shared instance, options bound from <c>Market:CorpusCache</c>, and the singleton benchmark
/// adapter resolving with it. Nothing connects to a database.</summary>
public sealed class MarketCorpusCacheRegistrationTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddMarketModule("Host=localhost;Database=offline;Username=u;Password=p");
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
    }

    [Fact]
    public void The_cache_is_one_shared_instance_with_the_default_interval_and_the_adapter_resolves()
    {
        using var provider = Build([]);

        var cache = provider.GetRequiredService<MarketCorpusCache>();
        var options = provider.GetRequiredService<MarketCorpusCacheOptions>();
        var adapters = provider.GetServices<IBenchmarkProviderAdapter>().ToList();

        Assert.Same(cache, provider.GetRequiredService<MarketCorpusCache>());
        Assert.True(cache.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(30), options.RevalidateInterval);
        Assert.Contains(adapters, a => a.Name == "market-feed");
    }

    [Fact]
    public void The_interval_and_the_kill_switch_bind_from_configuration()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Market:CorpusCache:RevalidateInterval"] = "00:02:00",
            ["Market:CorpusCache:Enabled"] = "false",
        });

        Assert.Equal(TimeSpan.FromMinutes(2), provider.GetRequiredService<MarketCorpusCacheOptions>().RevalidateInterval);
        Assert.False(provider.GetRequiredService<MarketCorpusCache>().Enabled);
    }
}
