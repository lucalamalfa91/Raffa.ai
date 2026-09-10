using Raffa.Benchmark.Adapters;
using Raffa.Benchmark.Configuration;
using Raffa.Benchmark.Contracts;
using Raffa.SharedKernel;

namespace Raffa.Benchmark;

/// <summary>
/// The pluggable provider-adapter registry task E04/F01/US01/T02 ("adapter registry + provider SDK
/// isolation") adds behind <see cref="IBenchmarkService"/> — module-map.md's "Benchmark Service
/// (impl)" node. Resolves every <see cref="IBenchmarkProviderAdapter"/> registered in the container
/// by name (config-selected via <see cref="BenchmarkAdapterOptions.ActiveAdapter"/>) and delegates
/// <see cref="GetBenchmarkAsync"/> to it — domain modules (Renewals, Savings, Quotes) depend on
/// <see cref="IBenchmarkService"/> only and never see this type, <see cref="IBenchmarkProviderAdapter"/>,
/// or a concrete adapter (ADR-002; spec §10.2).
///
/// Task E04/F01/US01/T01 left <see cref="IBenchmarkService"/> with no implementation or DI
/// registration — expected for that task's own scope ("mirrors how
/// <c>Raffa.AiGateway.IAiGateway</c> existed before <c>FixtureAiGateway</c> did"). This type is
/// this task's addition: a real, DI-resolvable <see cref="IBenchmarkService"/> — but still no
/// concrete adapter, since none exists in the solution yet. Story us-02-fixture-adapter's own task
/// runs in the same wave-spec phase as this one (parallel, neither depends on the other), so it
/// could not have registered its adapter here even if this task's scope included wiring one in.
/// Constructed with zero adapters, <see cref="GetBenchmarkAsync"/> fails every call with a named,
/// catchable <see cref="Result{T}"/> error rather than fabricating a result (ADR-001) or throwing —
/// the same "insufficient market data is honest, a bare number is not" posture
/// <c>BenchmarkResult.HasSufficientData</c>'s own doc comment describes for a thin comparable set,
/// applied here to the "no adapter at all" case.
/// </summary>
public sealed class BenchmarkAdapterRegistry : IBenchmarkService
{
    private readonly IReadOnlyDictionary<string, IBenchmarkProviderAdapter> _adaptersByName;
    private readonly BenchmarkAdapterOptions _options;

    public BenchmarkAdapterRegistry(
        IEnumerable<IBenchmarkProviderAdapter> adapters, BenchmarkAdapterOptions options)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(options);

        var byName = new Dictionary<string, IBenchmarkProviderAdapter>(StringComparer.OrdinalIgnoreCase);

        foreach (var adapter in adapters)
        {
            if (!byName.TryAdd(adapter.Name, adapter))
            {
                throw new InvalidOperationException(
                    $"Duplicate benchmark provider adapter name '{adapter.Name}'. Every " +
                    $"{nameof(IBenchmarkProviderAdapter)} registered in the container must have a " +
                    "unique Name (the registry must be able to tell adapters apart unambiguously).");
            }
        }

        _adaptersByName = byName;
        _options = options;
    }

    /// <inheritdoc/>
    public Task<Result<BenchmarkResult>> GetBenchmarkAsync(
        BenchmarkQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!_adaptersByName.TryGetValue(_options.ActiveAdapter, out var adapter))
        {
            var registered = _adaptersByName.Count == 0
                ? "none"
                : string.Join(", ", _adaptersByName.Keys);

            return Task.FromResult(Result<BenchmarkResult>.Failure(
                $"No benchmark provider adapter named '{_options.ActiveAdapter}' is registered " +
                $"(registered adapters: {registered}). Domain modules never fabricate a benchmark " +
                "result when no adapter is available (ADR-001)."));
        }

        return adapter.GetBenchmarkAsync(query, cancellationToken);
    }
}
