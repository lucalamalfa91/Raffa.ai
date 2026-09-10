using Raffa.Benchmark.Contracts;
using Raffa.SharedKernel;

namespace Raffa.Benchmark.Adapters;

/// <summary>
/// Seam every benchmark provider adapter implements (task E04/F01/US01/T02: "adapter registry +
/// provider SDK isolation"). <see cref="BenchmarkAdapterRegistry"/> is the only type that resolves
/// one of these by <see cref="Name"/> and calls it; domain modules (Renewals, Savings, Quotes —
/// module-map.md's allowed <c>Raffa.Benchmark</c> callers) depend on
/// <see cref="IBenchmarkService"/> only and never see this interface or a concrete adapter (spec
/// §10.2 "no business module should depend on Tropic, Vendr or any single provider schema"; the
/// Appendix C benchmark rule: "never call a benchmark provider directly from renewal, savings or
/// quote business logic").
///
/// Story us-02-fixture-adapter adds the first implementation — an internal fixture, never a paid
/// external API for the first `demo` (ADR-001) — expected to register under
/// <see cref="Configuration.BenchmarkAdapterOptions.DefaultAdapterName"/>. A later,
/// council-justified paid-provider adapter (ADR-001: "a paid external API may only be introduced
/// as a later, council-justified adapter — never a hard dependency of the first demo") implements
/// this same interface under its own <see cref="Name"/> and is selected via
/// <see cref="Configuration.BenchmarkAdapterOptions.ActiveAdapter"/> — a config change, never a
/// change to <see cref="BenchmarkAdapterRegistry"/>, <see cref="IBenchmarkService"/>, or any domain
/// module (mirrors <c>Raffa.AiGateway.Configuration.AiGatewayModelOptions</c>'s own
/// "config-selected... swap is config-only" convention for ADR-004).
///
/// This is the one seam allowed to reference a provider SDK (module-map.md: "Benchmark Service
/// (impl) ──references──► provider adapters (isolated)"); <see cref="BenchmarkAdapterRegistry"/>
/// and <see cref="IBenchmarkService"/> itself never do (ADR-002). This task adds no concrete
/// adapter and therefore no provider SDK package reference at all — that is
/// us-02-fixture-adapter's own, still-internal-fixture, file scope.
/// </summary>
public interface IBenchmarkProviderAdapter
{
    /// <summary>
    /// Stable adapter identifier used for config-selection
    /// (<see cref="Configuration.BenchmarkAdapterOptions.ActiveAdapter"/>) and typically echoed
    /// back on the resulting <see cref="BenchmarkResult.Source"/>. Must be unique across every
    /// adapter registered in the same container — <see cref="BenchmarkAdapterRegistry"/>'s
    /// constructor throws if two adapters share a name, rather than silently letting one shadow
    /// the other.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Same normalized contract as <see cref="IBenchmarkService.GetBenchmarkAsync"/> — an adapter
    /// is the thing that actually matches <paramref name="query"/> against its own comparables
    /// (fixture data today; a provider SDK call for a later adapter) and produces the
    /// provenance/confidence <see cref="BenchmarkResult"/> requires.
    /// </summary>
    Task<Result<BenchmarkResult>> GetBenchmarkAsync(
        BenchmarkQuery query, CancellationToken cancellationToken = default);
}
