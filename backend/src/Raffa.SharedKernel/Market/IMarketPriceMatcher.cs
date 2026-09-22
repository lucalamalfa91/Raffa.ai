namespace Raffa.SharedKernel.Market;

/// <summary>
/// Cross-module port (ADR-002: the port lives in SharedKernel, the implementation in
/// <c>Raffa.Market</c>, composition in <c>Raffa.Api</c>/<c>Raffa.Worker</c>) that prices a
/// contract's own line items against the shared market corpus — the same persisted
/// <c>market_record</c> rows the market RAG index is built from (R-MKT-01/03), never the provider
/// at question time. <c>Raffa.Documents.Contracts</c> owns the line items and the persisted result
/// but may not reference <c>Raffa.Market</c>, so it calls this port instead — the same split
/// <see cref="Suppliers.ISupplierResolver"/> already gives supplier linking.
///
/// <para>
/// Batched per contract: every line of one contract shares its supplier, currency and term, and
/// the implementation loads that supplier's slice of the corpus once for all of them.
/// </para>
/// </summary>
public interface IMarketPriceMatcher
{
    /// <summary>
    /// Returns one entry per <paramref name="lines"/> element, in the same order: the market band
    /// for that line, or <see langword="null"/> when the corpus holds no comparable record (unknown
    /// supplier, no product whose name the line carries, no record in the contract's own currency,
    /// or too small a sample). Never a guess and never a currency conversion — a line priced in
    /// GBP is only ever compared with GBP market records.
    /// </summary>
    Task<IReadOnlyList<MarketPriceMatch?>> MatchAsync(
        MarketPriceContext context,
        IReadOnlyList<MarketPriceLine> lines,
        CancellationToken cancellationToken);
}

/// <summary>What every line of one contract shares.</summary>
/// <param name="SupplierName">The contract's supplier as the tenant knows it (e.g.
/// <c>"Salesforce, Inc."</c>); <see langword="null"/> when no supplier is linked yet, in which
/// case nothing can be matched.</param>
/// <param name="Currency">ISO 4217 code every line's unit price is expressed in.</param>
/// <param name="TermMonths">The contract's committed term, when known — a same-term record is
/// preferred over a longer or shorter one.</param>
public sealed record MarketPriceContext(string? SupplierName, string Currency, int? TermMonths);

/// <summary>One line item to price: its description and SKU as extracted.</summary>
public sealed record MarketPriceLine(string Description, string? Sku);

/// <summary>
/// The one market record a line was matched to, with the provenance every market figure must
/// carry (ADR-001 w17 clause 4: a representative position with its source, sample size and as-of
/// date — never a bare number).
/// </summary>
/// <param name="RecordId">The market record's own id (<c>GET /api/market/records/{id}</c>).</param>
/// <param name="Product">The market product the line was matched to, e.g. <c>"Sales Cloud Unlimited"</c>.</param>
/// <param name="Geography">The market region of the record, e.g. <c>"UK"</c>.</param>
/// <param name="Currency">The record's currency — always the contract's own.</param>
/// <param name="TermMonths">The record's contract term.</param>
/// <param name="UnitPriceP25">25th percentile unit price.</param>
/// <param name="UnitPriceP50">Median unit price — the "market price" a line is compared with.</param>
/// <param name="UnitPriceP75">75th percentile unit price.</param>
/// <param name="SampleSize">Comparables behind the record.</param>
/// <param name="Provenance">The corpus's own provenance label, e.g.
/// <c>"representative market data · mock feed · updated 2026-07-01"</c>.</param>
/// <param name="UpdatedAt">When the market record itself was last refreshed.</param>
public sealed record MarketPriceMatch(
    string RecordId,
    string Product,
    string Geography,
    string Currency,
    int TermMonths,
    decimal UnitPriceP25,
    decimal UnitPriceP50,
    decimal UnitPriceP75,
    int SampleSize,
    string Provenance,
    DateTimeOffset UpdatedAt);
