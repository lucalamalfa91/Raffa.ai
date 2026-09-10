namespace Raffa.Benchmark.Contracts;

/// <summary>
/// Matching dimensions the Benchmark Service contract recognizes (product spec §10.4, verbatim:
/// "Relevant dimensions include supplier, product, SKU, edition, geography, currency, quantity
/// tier, contract term, customer size, purchase date and billing metric"). A
/// <see cref="BenchmarkResult.ComparisonDimensions"/> set containing only <see cref="Supplier"/>
/// would mean the match used supplier name alone, which spec §10.4 and the benchmark-matching
/// Appendix C rule forbid (us-01-benchmark-interface AC-3). This enum is the fixed vocabulary every
/// adapter — the fixture now, a later council-justified paid provider per ADR-001 — reports against,
/// so a caller always sees *which* dimensions actually drove a match, not just a boolean "matched".
/// </summary>
public enum BenchmarkComparisonDimension
{
    Supplier,
    Product,
    Sku,
    Edition,
    Geography,
    Currency,
    QuantityTier,
    ContractTerm,
    CustomerSize,
    PurchaseDate,
    BillingMetric,
}
