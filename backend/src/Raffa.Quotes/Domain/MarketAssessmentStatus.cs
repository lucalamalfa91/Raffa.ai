namespace Raffa.Quotes.Domain;

/// <summary>
/// The outcome of assessing one <see cref="QuoteLine"/> against the Benchmark Service (task
/// E05/F02/US01/T01, market-assessment; parent story us-01-market-assessment AC-1/AC-2). Every
/// outcome here is a valid, expected result, not an exception — an unfavourable input (missing
/// quote-level supplier/currency/geography, no usable price, or a benchmark with no usable
/// distribution) is reported as an honest, named status with an
/// <c>Raffa.Quotes.Application.Assessment.LineMarketAssessment.Explanation</c> saying why — never
/// fabricated (Appendix C rule 10), the same convention
/// <c>Raffa.Savings.Domain.PriceComparisonStatus</c> already established for the analogous
/// Savings comparison.
/// </summary>
public enum MarketAssessmentStatus
{
    /// <summary>The benchmark had a well-ordered, sufficient distribution: this line's
    /// <c>MarketPosition</c> is populated.</summary>
    Assessed,

    /// <summary>A <c>Raffa.Benchmark.Contracts.BenchmarkQuery</c> could not even be built for this
    /// line — the quote is missing one of <c>Quote.Supplier</c>/<c>Quote.Currency</c>/
    /// <c>Quote.Geography</c>/<c>Quote.PurchaseDate</c>, or the line itself has no usable
    /// product/quantity/term/price (see
    /// <c>Raffa.Quotes.Application.Assessment.MarketAssessmentQueryBuilder</c>). No benchmark
    /// call was made at all.</summary>
    QuoteDataUnresolved,

    /// <summary>A <see cref="Raffa.Benchmark.Contracts.BenchmarkQuery"/> was built and the
    /// Benchmark Service was called successfully, but it reported no usable distribution (ADR-001's
    /// explicit "insufficient market data" outcome, or a malformed/inverted one) — a market position
    /// cannot be determined without fabricating one (Appendix C rule 10). Confidence/provenance are
    /// still reported (spec §11.3's benchmark-trust rule: never withhold provenance just because the
    /// comparison itself abstained).</summary>
    InsufficientBenchmarkData,
}
