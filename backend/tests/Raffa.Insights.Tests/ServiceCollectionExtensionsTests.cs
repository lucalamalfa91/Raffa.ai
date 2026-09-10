using Raffa.Insights.Criticality;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Insights.Tests;

/// <summary>
/// Proves <see cref="ServiceCollectionExtensions.AddInsightsModule"/>. Started as task
/// E13/F01/US01/T01's placeholder (proving only the fluent-chaining shape, since
/// <c>AddInsightsModule</c> registered nothing yet); task E13/F07/US01/T01
/// (insights-calculators) adds the resolution proofs below now that it registers
/// <see cref="InsightsOptions"/> and <see cref="CriticalityScoreCalculator"/> — mirrors
/// <c>Raffa.Benchmark.Tests.ServiceCollectionExtensionsTests</c>' own "first proof, then real
/// resolution" shape.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddInsightsModule_returns_the_same_service_collection_for_fluent_chaining()
    {
        var services = new ServiceCollection();

        var result = services.AddInsightsModule();

        Assert.Same(services, result);
    }

    [Fact]
    public void AddInsightsModule_resolves_a_criticality_score_calculator()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddInsightsModule();

        using var provider = services.BuildServiceProvider();
        var calculator = provider.GetRequiredService<CriticalityScoreCalculator>();

        Assert.NotNull(calculator);
    }

    [Fact]
    public void AddInsightsModule_resolves_insights_options_with_the_council_default_weights_when_unconfigured()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddInsightsModule();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<InsightsOptions>();

        Assert.Equal(0.30m, options.RenewalUrgency);
        Assert.Equal(0.20m, options.RiskSeverity);
        Assert.Equal(0.20m, options.SpendWeight);
        Assert.Equal(0.20m, options.SavingsPotential);
        Assert.Equal(0.10m, options.OpenCriticalFacts);
    }
}
