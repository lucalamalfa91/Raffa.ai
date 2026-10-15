namespace Raffa.Market.Infrastructure.Entities;

/// <summary>
/// The persisted <c>market_record</c> row (task objective, verbatim column list: "recordId PK,
/// feedVersion, provider, json payload, provenance label, updatedAt"). One row per
/// <see cref="Contracts.MarketDeal"/> the ingestion job (<see cref="Ingestion.MarketIngestionService"/>)
/// has ever seen — shared, read-only, never tenant-scoped (ADR-024 "three sources, one rule": "the
/// provider is called only by that job; at question time Ask reads Raffa's own store and nothing
/// else"). Deliberately carries **no** <c>tenant_id</c> column and no
/// <c>Raffa.SharedKernel.Tenancy.ITenantContext</c>/RLS wiring anywhere in this module — see
/// <see cref="MarketDbContext"/>'s own doc comment for why that is a defect to add, not an
/// oversight to fix.
/// </summary>
/// <param name="RecordId">
/// <see cref="Contracts.MarketDeal.RecordId"/> verbatim — the natural, stable business key this
/// table's own primary key (task objective: "recordId PK"), not a generated surrogate uuid.
/// <c>GET /api/market/records/{id}</c>'s own lookup key.
/// </param>
public sealed class MarketRecordEntity
{
    public required string RecordId { get; set; }

    /// <summary>F7-T08: the corpus version this row belongs to (with <see cref="RecordId"/> the key).
    /// Readers only see the active version.</summary>
    public string CorpusVersion { get; set; } = MarketCorpusVersions.Initial;

    /// <summary>
    /// The feed version this row was last (re)written under (<see cref="Contracts.MarketFeedSnapshot.FeedVersion"/>)
    /// — <see cref="Ingestion.MarketIngestionService"/>'s own idempotency key half: "a second run
    /// with the same feed version and payload hash changes zero rows" (task objective).
    /// </summary>
    public required string FeedVersion { get; set; }

    /// <summary>
    /// Echoes <see cref="Contracts.MarketDeal.Provider"/> as its own column (rather than requiring
    /// every reader to deserialize <see cref="PayloadJson"/> first) purely for cheap
    /// filtering/display — the full record, including this same value, always also lives in
    /// <see cref="PayloadJson"/>.
    /// </summary>
    public required string Provider { get; set; }

    /// <summary>
    /// The full <see cref="Contracts.MarketDeal"/>, serialized as JSON (task objective: "json
    /// payload") — a <c>jsonb</c> column, not one column per field. Reconstructed via
    /// <c>System.Text.Json</c> by every reader (<see cref="Benchmark.MarketFeedBenchmarkAdapter"/>'s
    /// DB-backed path, <c>MarketEndpointExtensions</c>) rather than re-flattened into a wide
    /// relational shape — the record's own R-MKT-01 shape already carries every field a reader
    /// needs, and a `jsonb` payload lets this table absorb a future R-MKT-05 live-provider field
    /// addition without a migration. <see cref="Ingestion.MarketIngestionService"/>'s own
    /// idempotency check compares this exact string (its own doc comment: "same feed version and
    /// payload hash" — a byte-identical serialization is at least as strict a comparison as a hash
    /// of it, with no extra column or collision risk).
    /// </summary>
    public required string PayloadJson { get; set; }

    /// <summary>
    /// <see cref="Contracts.MarketProvenance.Label"/>'s own formatted output for this record at
    /// ingestion time (task objective: "provenance label") — precomputed and stored so a reader
    /// (the record endpoint, the DB-backed benchmark adapter) never needs to re-derive it from
    /// <see cref="UpdatedAt"/>; parent story AC-5's own "returns one record with its provenance
    /// label and updatedAt for the citation panel".
    /// </summary>
    public required string ProvenanceLabel { get; set; }

    /// <summary>Echoes <see cref="Contracts.MarketDeal.UpdatedAt"/> as its own column (same
    /// "cheap to read without deserializing the payload" reasoning as <see cref="Provider"/>).</summary>
    public required DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// F7-T04: SHA-256 (hex) of the canonical payload JSON, the ingestion idempotency key.
    /// <see langword="null"/> on rows written before the column existed (back-filled by the next
    /// ingestion run).
    /// </summary>
    public string? PayloadHash { get; set; }

    // F7-T02: denormalised, indexable copies of the fields the read paths filter on, written by the
    // ingestion job (Ingestion.MarketFilterColumns). Nullable: rows that predate them are back-filled
    // by the next ingestion run, and readers treat NULL as "not filterable in SQL, check in memory".

    /// <summary>Supplier folded for matching (lowercase words, legal suffixes dropped).</summary>
    public string? SupplierNorm { get; set; }

    /// <summary>Product folded for matching (lowercase alphanumeric words).</summary>
    public string? ProductNorm { get; set; }

    /// <summary>Category, trimmed and lowercased.</summary>
    public string? Category { get; set; }

    /// <summary>Geography code, trimmed and uppercased.</summary>
    public string? Geography { get; set; }

    /// <summary>Currency code, trimmed and uppercased.</summary>
    public string? Currency { get; set; }

    /// <summary>Contract term in months.</summary>
    public int? TermMonths { get; set; }

    /// <summary>Billing unit of the price (e.g. per user per month), lowercased; null when the record has none.</summary>
    public string? UnitMetric { get; set; }
}
