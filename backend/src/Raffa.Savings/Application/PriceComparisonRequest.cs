using Raffa.Benchmark.Contracts;

namespace Raffa.Savings.Application;

/// <summary>
/// Input to <see cref="PriceNormalizationCalculator.Compare"/> — parent story
/// us-01-price-normalization AC-1. <see cref="Query"/> is the exact <see cref="BenchmarkQuery"/> a
/// caller already built to fetch <see cref="Benchmark"/> via
/// <c>Raffa.Benchmark.IBenchmarkService.GetBenchmarkAsync</c> (product spec §10.3's
/// <c>getBenchmark(supplier, product, sku, geography, quantity, term, currency, purchase_date)</c>)
/// — reusing it here (rather than re-declaring supplier/quantity/term/currency on a second type)
/// means <see cref="BenchmarkQuery.Currency"/> and <see cref="BenchmarkQuery.Quantity"/> are
/// guaranteed to be the exact values the benchmark lookup itself used, not a second, possibly
/// drifted copy.
/// </summary>
/// <param name="Query">The same query used to fetch <see cref="Benchmark"/>. Term alignment
/// (comparing a 12-month contract's price against 12-month comparables, not 36-month ones) is the
/// Benchmark Service's own matching responsibility (product spec §10.4;
/// <c>Raffa.Benchmark.IBenchmarkService</c>'s own doc comment) — this calculator trusts that a
/// <see cref="Benchmark"/> fetched using this same <see cref="Query"/> already reflects comparables
/// of <see cref="BenchmarkQuery.Term"/>, so no additional term-arithmetic (for example
/// monthly-to-annual conversion) happens here. This is a deliberate, documented scope boundary
/// (Appendix C rule 10 — no fabricated conversion when the true billing-period relationship is not
/// actually known), not an oversight.</param>
/// <param name="CurrentTotalCost">The total amount actually paid or quoted for
/// <see cref="BenchmarkQuery.Quantity"/> units over <see cref="BenchmarkQuery.Term"/> — for example
/// <c>ContractLineItem.total_cost</c>/<c>annual_cost</c> (product spec §6's core data model), in
/// <see cref="BenchmarkQuery.Currency"/>. Dividing this by <see cref="BenchmarkQuery.Quantity"/> is
/// the "normalize... quantity" half of AC-1; comparing the result to <see cref="Benchmark"/>'s own
/// <see cref="BenchmarkResult.Currency"/> is the "normalize... currency" half.</param>
/// <param name="Benchmark">The already-fetched result of calling
/// <c>IBenchmarkService.GetBenchmarkAsync(Query)</c>. Never fetched by this calculator itself —
/// <see cref="PriceNormalizationCalculator"/> takes it as a plain value, the same "benchmark data
/// only ever arrives as an already-known value, never a live call" convention
/// <c>Raffa.Renewals.Application.PriorityScoreCalculator</c> already established, so Appendix C
/// rule 3 ("never call a benchmark provider directly from renewal, savings or quote business
/// logic") can never become an accidental provider call from this module.</param>
public sealed record PriceComparisonRequest(
    BenchmarkQuery Query,
    decimal CurrentTotalCost,
    BenchmarkResult Benchmark);
