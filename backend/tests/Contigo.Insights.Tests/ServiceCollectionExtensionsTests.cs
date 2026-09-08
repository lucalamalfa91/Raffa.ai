using Microsoft.Extensions.DependencyInjection;

namespace Contigo.Insights.Tests;

/// <summary>
/// Placeholder for task E13/F01/US01/T01 (v2-scaffold): <see
/// cref="ServiceCollectionExtensions.AddInsightsModule"/> registers nothing yet (see that
/// method's own doc comment). This proves the extension method exists, has the
/// conventional <c>AddXxxModule</c> fluent signature, and a host can call it and still get
/// back a working <see cref="IServiceCollection"/> — mirrors
/// <c>Contigo.Benchmark.Tests.ServiceCollectionExtensionsTests</c>' first proof for
/// <c>AddBenchmarkModule</c>. Replace/extend as the renewal-strategy-pack / portfolio-
/// criticality tasks (gap G-STRATEGY / G-CRITICALITY, <c>reports/audit/ask-v2-gaps.md</c>)
/// add registrations here.
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
}
