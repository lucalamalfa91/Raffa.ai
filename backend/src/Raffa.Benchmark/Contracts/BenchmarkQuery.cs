namespace Raffa.Benchmark.Contracts;

/// <summary>
/// Normalized request for <see cref="Raffa.Benchmark.IBenchmarkService.GetBenchmarkAsync"/> —
/// product spec §10.3's exact signature: <c>getBenchmark(supplier, product, sku, geography,
/// quantity, term, currency, purchase_date)</c>. Every field but <see cref="Sku"/> is required, so
/// a caller cannot build this query from supplier name alone — the contract itself, not just
/// adapter discipline, is how spec §10.4 / the Appendix C benchmark-matching rule ("matching must
/// use more than supplier name", us-01-benchmark-interface AC-3) is enforced at the interface
/// level. <see cref="Sku"/> is the one optional dimension because not every purchase is SKU-level
/// (for example a services or usage-based contract) — spec §10.4 lists SKU as one of several match
/// dimensions, not a mandatory key.
/// </summary>
/// <param name="Supplier">Supplier/vendor name, e.g. <c>"AWS"</c>, <c>"Salesforce"</c>.</param>
/// <param name="Product">Product or product family name being priced.</param>
/// <param name="Sku">SKU/edition identifier, when the purchase is SKU-level; <see langword="null"/> otherwise.</param>
/// <param name="Geography">Market/region the purchase applies to (country or region code).</param>
/// <param name="Quantity">Purchased quantity (seats, units, usage tier) the price is normalized against.</param>
/// <param name="Term">Contract term (e.g. <c>"12 months"</c>, <c>"36 months"</c>).</param>
/// <param name="Currency">ISO 4217 currency code the requested comparison is expressed in.</param>
/// <param name="PurchaseDate">The purchase/quote date, so comparables can be filtered to a relevant window.</param>
public sealed record BenchmarkQuery(
    string Supplier,
    string Product,
    string? Sku,
    string Geography,
    decimal Quantity,
    string Term,
    string Currency,
    DateOnly PurchaseDate);
