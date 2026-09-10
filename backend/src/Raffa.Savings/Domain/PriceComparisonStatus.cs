namespace Raffa.Savings.Domain;

/// <summary>
/// The outcome of <see cref="Raffa.Savings.Application.PriceNormalizationCalculator.Compare"/> —
/// parent story us-01-price-normalization AC-1/AC-2 made concrete and testable. Every outcome here
/// is a valid, expected result, not an exception: an unfavorable input (a zero/negative quantity, a
/// currency the benchmark cannot be compared against, or a benchmark with no usable distribution)
/// is reported as an honest, named status with an
/// <see cref="Raffa.Savings.Application.PriceComparisonResult.Explanation"/> saying why — never
/// fabricated (Appendix C rule 10), the same convention
/// <c>Raffa.Renewals.Application.RenewalCalculationStatus</c> already established for this
/// codebase's other deterministic calculators.
/// </summary>
public enum PriceComparisonStatus
{
    /// <summary>
    /// The benchmark had a well-ordered, sufficient distribution and currencies matched: normalized
    /// unit price, percentile rank, recommended target range and savings range are all populated.
    /// </summary>
    Compared,

    /// <summary>
    /// <c>PriceComparisonRequest.Query.Quantity</c> is zero or negative: the current unit price
    /// cannot be derived by dividing total cost by quantity (Appendix C rule 10 — invalid
    /// structured data is treated the same as missing data, not guessed at). Nothing is computed,
    /// not even the normalized unit price.
    /// </summary>
    InvalidQuantity,

    /// <summary>
    /// <c>PriceComparisonRequest.Query.Currency</c> does not match
    /// <c>PriceComparisonRequest.Benchmark.Currency</c>. This codebase has no currency-conversion
    /// service (Appendix C rule 10 — converting would fabricate an exchange rate it does not
    /// actually know), so the normalized unit price is still reported (in its own currency) but the
    /// percentile/target/savings comparison is not attempted.
    /// </summary>
    CurrencyMismatch,

    /// <summary>
    /// Either <c>PriceComparisonRequest.Benchmark.Distribution</c> is <see langword="null"/>
    /// (ADR-001's explicit "insufficient market data" outcome), or it is present but not
    /// well-ordered (<c>P25 &lt;= P50 &lt;= P75</c> does not hold — a data-quality problem in the
    /// adapter's own output), so a percentile/target/savings comparison would not be meaningful.
    /// The normalized unit price is still reported.
    /// </summary>
    InsufficientBenchmarkData,
}
