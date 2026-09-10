using Raffa.Benchmark.Configuration;
using Raffa.Benchmark.Contracts;
using Raffa.Benchmark.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Benchmark.Tests;

/// <summary>
/// Proves task E04/F01/US01/T02's own wiring claim: <see cref="BenchmarkAdapterOptions"/> and
/// <see cref="IBenchmarkService"/> both resolve from a DI container via
/// <see cref="ServiceCollectionExtensions.AddBenchmarkModule"/>, mirroring
/// <c>Raffa.AiGateway.Tests.ServiceCollectionExtensionsTests</c>. Task E04/F01/US02/T02
/// (fixture-confidence) extends this with proof that the default configuration now resolves a
/// genuinely working, fixture-backed <see cref="IBenchmarkService"/> — completing the registration
/// <see cref="IBenchmarkService"/>'s own doc comment says story us-02-fixture-adapter would add.
/// <c>Raffa.AiGateway.Tests.ServiceCollectionExtensionsTests</c>.
/// Proves task E04/F01/US02/T01's own wiring claim: <see cref="IBenchmarkService"/>'s doc comment
/// says "story us-02-fixture-adapter adds the first adapter" — this is that registration, mirroring
/// <c>Raffa.AiGateway.Tests.ServiceCollectionExtensionsTests</c>' identical proof for
/// <c>AddAiGatewayModule</c>.
///
/// <para>
/// Incidental fix (task E04/F02/US01/T02, out of that task's own module but blocking
/// <c>dotnet build Raffa.slnx</c> for every task): this file failed to compile — a prior merge
/// had spliced <c>AddBenchmarkModule_resolves_a_registry_backed_service_with_the_default_adapter_name</c>'s
/// closing brace together with a second, differently-named test method's signature line, silently
/// discarding that second method's own body. The body restored below is verified against
/// <see cref="ServiceCollectionExtensions.AddBenchmarkModule"/>'s and
/// <see cref="BenchmarkAdapterRegistry"/>'s actual current source, not reconstructed from guesswork.
/// The second, differently-named test could not be recovered (its body is gone, not just
/// misplaced) and is not reinvented here. While diagnosing this, a separate, pre-existing gap
/// surfaced in <c>Raffa.Benchmark</c>'s own DI wiring — <c>AddBenchmarkModule</c> still
/// <c>TryAddSingleton</c>-registers <see cref="Fixtures.FixtureBenchmarkAdapter"/> directly as
/// <see cref="IBenchmarkService"/>, which the preceding <c>TryAddSingleton&lt;IBenchmarkService,
/// BenchmarkAdapterRegistry&gt;</c> call already shadows (first registration wins), and
/// <see cref="Fixtures.FixtureBenchmarkAdapter"/> does not implement
/// <see cref="Adapters.IBenchmarkProviderAdapter"/>, so <see cref="BenchmarkAdapterRegistry"/> can
/// never reach it through its <c>IEnumerable&lt;IBenchmarkProviderAdapter&gt;</c> constructor
/// parameter either — <see cref="IBenchmarkService"/> resolves to an always-adapter-less
/// <see cref="BenchmarkAdapterRegistry"/> today, not <see cref="Fixtures.FixtureBenchmarkAdapter"/>.
/// That is a <c>Raffa.Benchmark</c> module wiring decision, not this (Savings) task's file scope
/// to redesign — left exactly as found, flagged here for that module's own owner.
/// </para>
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBenchmarkModule_resolves_a_registry_backed_service_with_the_default_adapter_name()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddBenchmarkModule();

        using var provider = services.BuildServiceProvider();

        var benchmarkService = provider.GetRequiredService<IBenchmarkService>();
        Assert.IsType<BenchmarkAdapterRegistry>(benchmarkService);

        // No "Benchmark:Adapter" configuration section supplied — BenchmarkAdapterOptions's own
        // property initializer must still produce a usable options object.
        var options = provider.GetRequiredService<BenchmarkAdapterOptions>();
        Assert.Equal(BenchmarkAdapterOptions.DefaultAdapterName, options.ActiveAdapter);
    }

    /// <summary>
    /// Task E04/F01/US02/T02: proves the registry is no longer merely "resolvable" but actually
    /// dispatches to a real adapter under the default configuration — <see
    /// cref="FixtureBenchmarkAdapter"/> is now registered as the <c>"fixture"</c>-named
    /// <c>IBenchmarkProviderAdapter</c> <see cref="ServiceCollectionExtensions.AddBenchmarkModule"/>
    /// wires in, matching <see cref="BenchmarkAdapterOptions.DefaultAdapterName"/>.
    /// </summary>
    [Fact]
    public async Task AddBenchmarkModule_resolves_a_working_fixture_backed_benchmark_service()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddBenchmarkModule();
        using var provider = services.BuildServiceProvider();

        var benchmarkService = provider.GetRequiredService<IBenchmarkService>();
        var result = await benchmarkService.GetBenchmarkAsync(new BenchmarkQuery(
            "AWS", "EC2 Compute", "m5.large", "US", 50m, "12 months", "USD", new DateOnly(2026, 8, 1)));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.HasSufficientData);
        Assert.Equal("fixture", result.Value.Source);
        Assert.Equal(new BenchmarkDistribution(0.085m, 0.096m, 0.108m), result.Value.Distribution);
    }

    [Fact]
    public void AddBenchmarkModule_binds_the_active_adapter_from_the_configured_section()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Benchmark:Adapter:ActiveAdapter"] = "paid-provider",
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddBenchmarkModule();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<BenchmarkAdapterOptions>();

        Assert.Equal("paid-provider", options.ActiveAdapter);
    }

    /// <summary>
    /// Task E04/F01/US02/T02: the only adapter this module ever registers is named
    /// <c>"fixture"</c> (<see cref="FixtureBenchmarkAdapter.Name"/>); configuring a different
    /// <see cref="BenchmarkAdapterOptions.ActiveAdapter"/> name — the shape a later,
    /// council-justified paid-provider adapter would eventually use — must still fail honestly
    /// rather than silently falling back to the fixture or throwing at startup (ADR-001).
    /// </summary>
    [Fact]
    public async Task Resolved_service_fails_honestly_when_the_configured_adapter_name_is_not_registered()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Benchmark:Adapter:ActiveAdapter"] = "paid-provider",
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddBenchmarkModule();
        using var provider = services.BuildServiceProvider();

        var benchmarkService = provider.GetRequiredService<IBenchmarkService>();
        Assert.IsType<BenchmarkAdapterRegistry>(benchmarkService);

        var result = await benchmarkService.GetBenchmarkAsync(new BenchmarkQuery(
            "AWS", "EC2 Compute", null, "US", 100m, "12 months", "USD", new DateOnly(2026, 1, 15)));

        Assert.True(result.IsFailure);
        Assert.Contains("paid-provider", result.Error, StringComparison.Ordinal);
    }
}
