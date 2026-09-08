namespace Contigo.Market.Contracts;

/// <summary>
/// Contigo's own normalized market-intelligence record (`inputs/requirements.md` R-MKT-01;
/// council decision carried into us-01-market-intelligence: "the mock record shape is Contigo's
/// own normalized contract; the third-party API will be mapped onto it, not the reverse" —
/// OQ-askv2-001, `reports/open-questions.md`). Every field below is the R-MKT-01 field list
/// verbatim; no business module (Chat, Renewals, Savings, Quotes) references this shape directly
/// — they only ever see it through <see cref="Contigo.Benchmark.Contracts.BenchmarkResult"/>
/// (<see cref="Benchmark.MarketFeedBenchmarkAdapter"/>) or <see cref="MarketNote"/>
/// (<see cref="Retrieval.IMarketKnowledgeRetrieval"/>) — spec §10.2's "no business module should
/// depend on ... any single provider schema", extended here to the mock feed's own record shape.
/// </summary>
/// <param name="Provider">
/// The named data source this record came from — product spec §10.2's own Benchmark Service
/// diagram names "Internal Dataset" as one of four Provider Adapter kinds (alongside "Provider
/// A", "Provider B", "Customer History"); ADR-001's amendment renames that exact option "the mock
/// market-intelligence feed". Every record in the checked-in mock fixture carries the literal
/// value <c>"Internal Dataset"</c> — distinct from <see cref="Source"/> (always <c>"mock"</c>,
/// R-MKT-02) — so a later, council-justified live provider (R-MKT-05, e.g. a named third-party
/// vendor) can report its own name here without changing the shape.
/// </param>
/// <param name="RecordId">Stable identifier for this record, unique within one feed version —
/// <c>GET /api/market/records/{id}</c>'s own lookup key (T02).</param>
/// <param name="Supplier">Supplier/vendor name, e.g. <c>"Salesforce"</c>, <c>"Allianz"</c> —
/// never a guid (R-SUP-04's "names, never guids" convention, applied here to the market side).</param>
/// <param name="Category">Coarse market category, e.g. <c>"Enterprise Software"</c>,
/// <c>"Insurance"</c>, <c>"Facilities"</c>, <c>"Telco"</c>, <c>"Logistics"</c>,
/// <c>"Professional Services"</c> (R-MKT-02's own named categories).</param>
/// <param name="Product">Product or product family name being priced.</param>
/// <param name="Geography">Market/region the deal applies to — the mock fixture uses
/// <c>"EU"</c> / <c>"CH"</c> / <c>"US"</c> (R-MKT-02).</param>
/// <param name="Currency">ISO 4217 currency code the unit prices are expressed in — the mock
/// fixture uses <c>CHF</c> / <c>EUR</c> / <c>USD</c> (R-MKT-02).</param>
/// <param name="CompanySizeBand">Buyer company-size band, e.g. <c>"500-2000"</c> (employees).</param>
/// <param name="TermMonths">Contract term, in months.</param>
/// <param name="AnnualValueBand">Coarse annual contract value band, e.g. <c>"250k-500k"</c>.</param>
/// <param name="UnitPriceP25">25th percentile unit price across this record's own comparable set.</param>
/// <param name="UnitPriceP50">Median (50th percentile) unit price.</param>
/// <param name="UnitPriceP75">75th percentile unit price.</param>
/// <param name="NegotiatedClauses">Negotiated clause examples observed for this record (liability
/// cap, termination for convenience, price protection, …) — always an array, possibly empty;
/// never <see langword="null"/> (R-MKT-01's own <c>negotiatedClauses[]</c> notation, unlike the
/// trailing optional fields below, carries no <c>?</c>).</param>
/// <param name="ClosingPeriod">When the underlying deals closed, e.g. <c>"2026-Q1"</c>.</param>
/// <param name="SampleSize">Number of comparables behind this record. The mock fixture
/// deliberately includes several rows with <c>sampleSize &lt; 5</c> so
/// <see cref="Benchmark.MarketFeedBenchmarkAdapter"/>'s abstain path is exercised (R-MKT-02).</param>
/// <param name="Source">Always the literal <c>"mock"</c> for every record in the checked-in
/// fixture (R-MKT-02 AC, us-01-market-intelligence AC-1) — distinct from <see cref="Provider"/>.</param>
/// <param name="Representative">Always <see langword="true"/> for every record in the checked-in
/// fixture (R-MKT-02, us-01-market-intelligence AC-1) — this is illustrative, labelled data, never
/// presented as live market truth (ADR-001).</param>
/// <param name="UpdatedAt">When this record was last refreshed — <see cref="MarketProvenance.Label"/>
/// formats this into the UX provenance string (R-MKT-04).</param>
/// <param name="Sku">SKU/edition identifier, when the deal is SKU-level (for example AWS EC2
/// instance types); <see langword="null"/> for a product-level price.</param>
/// <param name="DiscountAchievedPct">Discount achieved off list price, as a percentage, when known.</param>
/// <param name="UpliftCapPct">Negotiated renewal price-uplift cap, as a percentage, when known.</param>
/// <param name="NoticeDays">Termination/renewal notice period, in days, when known.</param>
/// <param name="PaymentTerms">Payment terms observed, e.g. <c>"Net 30"</c>, when known.</param>
/// <param name="LicenseRestrictions">Licence/usage restrictions from the provider, stored
/// internally where relevant (spec §10.3) — not necessarily surfaced to end users.</param>
public sealed record MarketDeal(
    string Provider,
    string RecordId,
    string Supplier,
    string Category,
    string Product,
    string Geography,
    string Currency,
    string CompanySizeBand,
    int TermMonths,
    string AnnualValueBand,
    decimal UnitPriceP25,
    decimal UnitPriceP50,
    decimal UnitPriceP75,
    IReadOnlyList<NegotiatedClause> NegotiatedClauses,
    string ClosingPeriod,
    int SampleSize,
    string Source,
    bool Representative,
    DateTimeOffset UpdatedAt,
    string? Sku = null,
    double? DiscountAchievedPct = null,
    double? UpliftCapPct = null,
    int? NoticeDays = null,
    string? PaymentTerms = null,
    string? LicenseRestrictions = null);

/// <summary>
/// One negotiated-clause example on a <see cref="MarketDeal"/> (R-MKT-01: "negotiated clauses
/// (liability cap, termination for convenience, price protection…)"). <paramref name="Type"/> is
/// a short, free-text category (not a fixed enum — the mock feed's own clause taxonomy is
/// illustrative, not a locked vocabulary); <paramref name="Value"/> is the human-readable clause
/// detail, e.g. <c>"90 days notice, no penalty"</c>.
/// </summary>
public sealed record NegotiatedClause(string Type, string Value);
