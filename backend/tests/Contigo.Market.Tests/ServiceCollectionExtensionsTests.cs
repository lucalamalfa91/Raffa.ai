using Microsoft.Extensions.DependencyInjection;

namespace Contigo.Market.Tests;

/// <summary>
/// Placeholder for task E13/F01/US01/T01 (v2-scaffold): <see
/// cref="ServiceCollectionExtensions.AddMarketModule"/> registers nothing yet (see that
/// method's own doc comment). This proves the extension method exists, has the
/// conventional <c>AddXxxModule</c> fluent signature, and a host can call it and still get
/// back a working <see cref="IServiceCollection"/> — mirrors
/// <c>Contigo.Benchmark.Tests.ServiceCollectionExtensionsTests</c>' first proof for
/// <c>AddBenchmarkModule</c>. Replace/extend as the mock market feed / ingestion / index /
/// benchmark-projection tasks (gap G-MARKET-FEED / G-MARKET-INDEX,
/// <c>reports/audit/ask-v2-gaps.md</c>) add registrations here.
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
}
