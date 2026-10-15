using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Raffa.AiFlows.MarketKnowledge.Ingestion;
using Raffa.Market;
using Raffa.AiFlows.Tests.TestSupport;
using Raffa.Market.Ingestion;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.MarketKnowledge.Ingestion;

/// <summary>A-11: with a market connection string the ingestion job resolves with the curator; it is off unless configured.</summary>
public sealed class MarketCurationCompositionTests
{
    private static ServiceProvider Build(Dictionary<string, string?>? settings = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build());
        services.AddScoped<IAuditWriter, RecordingAuditWriter>();
        services.AddMarketModule("Host=localhost;Database=unused;Username=u;Password=p");
        services.AddAiFlows();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void The_ingestion_job_resolves_with_the_curator_and_it_is_disabled_by_default()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<MarketIngestionService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMarketBatchCurator>());
        Assert.False(provider.GetRequiredService<MarketCurationOptions>().Enabled);
    }

    [Fact]
    public void Curation_is_switched_on_by_configuration()
    {
        using var provider = Build(new() { ["Market:Ingestion:Curation:Enabled"] = "true" });

        Assert.True(provider.GetRequiredService<MarketCurationOptions>().Enabled);
    }
}
