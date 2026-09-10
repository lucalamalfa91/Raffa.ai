using Raffa.Benchmark.Contracts;
using Raffa.SharedKernel;

namespace Raffa.Benchmark;

/// <summary>
/// Benchmark Service interface consumed by domain modules (Renewals, Savings, Quotes; ADR-002
/// module-map). Domain modules depend on this abstraction only — the implementation behind it is
/// the sole place that talks to a provider adapter, so business logic can never call a benchmark
/// provider directly (product spec §10.2 "Strategic requirement": "No business module should
/// depend on Tropic, Vendr or any single provider schema"; the Appendix C benchmark rule: "Never
/// call a benchmark provider directly from renewal, savings or quote business logic").
///
/// Task E04/F01/US01/T01 (this task) defines the normalized contract only: the
/// <see cref="GetBenchmarkAsync"/> signature and its request/response DTOs
/// (<see cref="BenchmarkQuery"/>, <see cref="BenchmarkResult"/>). Task E04/F01/US01/T02 (this
/// story's other task, "adapter registry + provider SDK isolation") adds the pluggable
/// provider-adapter registry behind this interface; story us-02-fixture-adapter adds the first
/// adapter — an internal fixture, never a paid external API for the first `demo` (ADR-001) — that
/// actually performs matching and produces provenance/confidence. Until an adapter is registered,
/// this interface has no implementation or DI registration in the solution — expected for this
/// task's scope (mirrors how <c>Raffa.AiGateway.IAiGateway</c> existed before
/// <c>FixtureAiGateway</c> did).
///
/// Returns <see cref="Result{T}"/> (this codebase's convention for expected failures) rather than
/// throwing.
/// </summary>
public interface IBenchmarkService
{
    /// <summary>
    /// Normalized benchmark lookup — product spec §10.3's exact signature:
    /// <c>getBenchmark(supplier, product, sku, geography, quantity, term, currency,
    /// purchase_date)</c>, carried by <see cref="BenchmarkQuery"/>. Matching must use more than
    /// supplier name (spec §10.4; the Appendix C benchmark-matching rule) — <see cref="BenchmarkQuery"/>
    /// requires product, geography, quantity, term, currency and purchase date alongside supplier,
    /// so a caller cannot construct a supplier-only lookup (us-01-benchmark-interface AC-3).
    ///
    /// The result is never a bare precise-looking number without provenance (ADR-001): when
    /// comparables are too thin to publish a distribution, <see cref="BenchmarkResult.Distribution"/>
    /// is <see langword="null"/> — the explicit "insufficient market data" outcome — while metric,
    /// currency, confidence, source, updated-at and comparison dimensions are still populated so
    /// the caller can show why.
    /// </summary>
    Task<Result<BenchmarkResult>> GetBenchmarkAsync(
        BenchmarkQuery query, CancellationToken cancellationToken = default);
}
