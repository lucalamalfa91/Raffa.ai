namespace Raffa.Benchmark.Configuration;

/// <summary>
/// Config-selected active adapter name (task E04/F01/US01/T02: "adapter registry"). Mirrors
/// <c>Raffa.AiGateway.Configuration.AiGatewayModelOptions</c>'s own "config-selected... swap is
/// config-only" convention for ADR-004, applied here to ADR-001's benchmark adapter: which
/// registered <c>Raffa.Benchmark.Adapters.IBenchmarkProviderAdapter</c>
/// <see cref="BenchmarkAdapterRegistry"/> dispatches <c>GetBenchmarkAsync</c> calls to is a
/// deployment configuration value, never a code change — swapping the fixture for a later,
/// council-justified paid-provider adapter (ADR-001) means registering the new adapter and
/// changing <see cref="ActiveAdapter"/>, nothing else.
/// </summary>
public sealed class BenchmarkAdapterOptions
{
    /// <summary>Conventional configuration section path for binding this options object.</summary>
    public const string SectionName = "Benchmark:Adapter";

    /// <summary>
    /// us-02-fixture-adapter's own adapter is expected to register under this name — matches
    /// <c>BenchmarkResult.Source</c>'s own fixture example ("fixture" for the first demo's
    /// internal adapter — ADR-001).
    /// </summary>
    public const string DefaultAdapterName = "fixture";

    /// <summary>
    /// Name of the <c>IBenchmarkProviderAdapter</c> that <see cref="BenchmarkAdapterRegistry"/>
    /// dispatches to. Defaults to <see cref="DefaultAdapterName"/> so a deployment with no
    /// "Benchmark:Adapter" section configured is already pointed at the adapter ADR-001 names as
    /// sufficient for the first demo, once us-02-fixture-adapter registers it — until then, no
    /// adapter of that name exists and <see cref="BenchmarkAdapterRegistry.GetBenchmarkAsync"/>
    /// fails honestly (see that type's own doc comment) rather than fabricating a result.
    /// </summary>
    public string ActiveAdapter { get; init; } = DefaultAdapterName;
}
