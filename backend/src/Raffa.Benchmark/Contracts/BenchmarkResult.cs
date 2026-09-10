namespace Raffa.Benchmark.Contracts;

/// <summary>
/// Normalized response from <see cref="Raffa.Benchmark.IBenchmarkService.GetBenchmarkAsync"/> —
/// product spec §10.3's "Normalized response" table. <see cref="Distribution"/> is
/// <see langword="null"/> exactly when comparables were too thin to publish P25/P50/P75 — the
/// explicit "insufficient market data" outcome ADR-001 requires instead of a bare
/// precise-looking number without provenance; check <see cref="HasSufficientData"/> before reading
/// it. Metric, currency, confidence, source, updated-at and comparison dimensions are always
/// populated regardless — spec §10.3 marks all of them "Yes" required, independent of whether a
/// distribution is available, so a caller can always show provenance even for an
/// insufficient-data result.
/// </summary>
/// <param name="Distribution">P25/P50/P75, or <see langword="null"/> for an explicit
/// insufficient-market-data outcome (ADR-001).</param>
/// <param name="Metric">The unit the comparison is expressed in (e.g. <c>"per seat / year"</c>,
/// <c>"per GB / month"</c>).</param>
/// <param name="Currency">ISO 4217 currency code the values are expressed in — normalized to the
/// request's <see cref="BenchmarkQuery.Currency"/> by the adapter.</param>
/// <param name="Confidence">Raffa's own confidence score in <c>[0, 1]</c> for this result (spec
/// §10.3: "Confidence — Yes — Raffa score"), reflecting comparability of the matched data, not a
/// provider-reported confidence.</param>
/// <param name="Source">Provider/source identifier this result came from (e.g. <c>"fixture"</c> for
/// the first `demo`'s internal adapter — ADR-001 — or a named paid provider once one is
/// council-justified).</param>
/// <param name="UpdatedAt">When the underlying comparable data was last refreshed.</param>
/// <param name="ComparisonDimensions">Which <see cref="BenchmarkComparisonDimension"/> values the
/// adapter actually matched on. Never just <see cref="BenchmarkComparisonDimension.Supplier"/> —
/// spec §10.4 / the Appendix C benchmark-matching rule forbid matching on supplier name alone
/// (us-01-benchmark-interface AC-3).</param>
/// <param name="SampleSize">Number of comparables behind this result, when the adapter can report
/// one (spec §10.3: "Sample size — If available").</param>
/// <param name="LicenseRestrictions">Provider license/usage restrictions to retain internally
/// (spec §10.3: "License restrictions — Store internally where relevant"); not necessarily surfaced
/// to end users.</param>
public sealed record BenchmarkResult(
    BenchmarkDistribution? Distribution,
    string Metric,
    string Currency,
    double Confidence,
    string Source,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<BenchmarkComparisonDimension> ComparisonDimensions,
    int? SampleSize = null,
    string? LicenseRestrictions = null)
{
    /// <summary>
    /// <see langword="false"/> when <see cref="Distribution"/> is <see langword="null"/> — the
    /// explicit "insufficient market data" outcome (ADR-001) a caller must check before rendering
    /// P25/P50/P75, the same gating role <c>Raffa.AiGateway.Contracts.AiAnswerResult
    /// .CanDetermine</c> plays for its own nullable <c>Answer</c>.
    /// </summary>
    public bool HasSufficientData => Distribution is not null;
}
