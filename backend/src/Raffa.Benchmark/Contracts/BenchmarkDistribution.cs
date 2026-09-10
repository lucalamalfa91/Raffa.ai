namespace Raffa.Benchmark.Contracts;

/// <summary>
/// The P25/P50/P75 price distribution (product spec §10.3 "Normalized response" table: "Yes when
/// provider supports distribution"). Present on <see cref="BenchmarkResult"/> only when the
/// adapter had enough comparables to publish one — see <see cref="BenchmarkResult.HasSufficientData"/>.
/// </summary>
/// <param name="P25">25th percentile price/metric value.</param>
/// <param name="P50">Median (50th percentile) price/metric value.</param>
/// <param name="P75">75th percentile price/metric value.</param>
public sealed record BenchmarkDistribution(decimal P25, decimal P50, decimal P75);
