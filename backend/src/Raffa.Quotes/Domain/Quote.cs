using Raffa.SharedKernel;

namespace Raffa.Quotes.Domain;

/// <summary>
/// An uploaded supplier quote file (task E05/F01/US01/T01, quote-extraction; parent story
/// us-01-quote-line-extraction AC-1 "POST /api/quotes uploads a quote and creates an extraction
/// job"; product spec §4.4/§11 "New Purchase / Quote Check"; module-map.md "Quotes | Quote,
/// QuoteLine, Assessment, NegotiationOutcome | /api/quotes"). Deliberately the same
/// upload-metadata shape as <c>Raffa.Documents.Contracts.Domain.Document</c> (filename/mime
/// type/storage path/checksum/processing status) rather than a reference to that type: ADR-002
/// forbids this module from referencing <c>Raffa.Documents.Contracts</c> at all
/// (<c>Raffa.ArchitectureTests.DependencyDirectionTests</c>'s allow-list for
/// <c>Raffa.Quotes</c> is exactly <c>[SharedKernel, Benchmark]</c>), and a quote is not a
/// contract — reusing <c>Document</c>/<c>Contract</c> would also route every uploaded quote
/// through <c>StagedExtractionService.EnsureContractAsync</c>, silently creating a phantom
/// <c>Contract</c> row for a document that is never a contract (product spec §11's own Quote →
/// Benchmark → Assessment → Negotiate → Contract flow treats "becomes a Contract" as a later,
/// explicit step, not an upload-time side effect).
///
/// <para>
/// Task-01 (quote-extraction) deliberately did <b>not</b> carry the spec §6 Quote-level aggregate
/// fields ("supplier, dates, currency, values, status") this module-map row also names —
/// "Supplier/currency/status aggregation reads naturally as a later normalization/assessment
/// concern once line items exist to aggregate" (this class's own prior doc comment;
/// `backend/README.md`'s "Quote Check" section echoed the same gap as "Deliberately out of
/// task-01's own scope... benchmark matching/assessment/negotiation remain future work no task has
/// picked up yet"). <b>Task E05/F02/US01/T01 (market-assessment) is that task</b>: AC-1 ("Match
/// normalized line items to the Benchmark Service (multi-dimensional)") is structurally impossible
/// without a supplier/geography/currency/purchase-date to put in a
/// <c>Raffa.Benchmark.Contracts.BenchmarkQuery</c> (every one of those four is a required,
/// non-nullable constructor parameter there) — unlike the identical-looking gap on
/// <c>Raffa.Documents.Contracts.Domain.Contract</c> that <c>Raffa.IntegrationTests
/// .R3IntegrationFixture</c>'s own doc comment left unaddressed for Savings, ADR-002 does not
/// forbid this: <c>Raffa.Quotes</c> owns both this entity and <see cref="QuoteLine"/> itself
/// (no cross-module reference is involved), so — unlike Savings reaching into
/// Documents/Contracts — there is no architectural reason to leave the gap open once a task
/// actually needs to close it.
/// </para>
///
/// <para>
/// <see cref="Supplier"/>/<see cref="Currency"/>/<see cref="Geography"/>/<see cref="PurchaseDate"/>
/// are populated by <c>Raffa.Quotes.Application.QuoteUploadService.UploadAsync</c> from explicit,
/// optional caller-supplied upload fields — never inferred/guessed from the document text (Appendix
/// C rule 10): nothing in this codebase extracts a document-level supplier/geography/currency
/// today (<c>Raffa.Quotes.Application.Extraction.QuoteLineJsonSchema</c> only asks the AI Gateway
/// `extract` role for per-line facts), and spec §11.1's own "Identify supplier" workflow step has
/// no task of its own yet. All four are nullable: a quote uploaded without them simply cannot be
/// matched against the Benchmark Service yet (<c>Raffa.Quotes.Application.Assessment
/// .MarketAssessmentQueryBuilder</c> reports that honestly, per line, rather than fabricating a
/// placeholder value) — the same "insufficient data is an honest, expected outcome" posture ADR-001
/// already establishes for the benchmark side of this same match.
/// </para>
/// </summary>
public sealed class Quote : TenantScopedEntity
{
    public required string FileName { get; set; }
    public required string MimeType { get; set; }

    /// <summary>Tenant-prefixed object storage path (ADR-009) — built via the same
    /// <c>Raffa.SharedKernel.Storage.DocumentStoragePath.Build</c> helper
    /// <c>Raffa.Documents.Contracts.Application.DocumentUploadService</c> uses; that helper is
    /// already generic over "an uploaded document's id", not specific to
    /// <c>Raffa.Documents.Contracts.Domain.Document</c>.</summary>
    public required string StoragePath { get; set; }

    public required string Checksum { get; set; }

    public QuoteProcessingStatus ProcessingStatus { get; set; } = QuoteProcessingStatus.Uploaded;

    /// <summary>Supplier/vendor name this quote was received from (e.g. <c>"AWS"</c>,
    /// <c>"Salesforce"</c>) — <c>Raffa.Benchmark.Contracts.BenchmarkQuery.Supplier</c>'s source
    /// for every line on this quote (spec §10.3/§10.4: matching must use more than supplier name
    /// alone, but supplier is still one required dimension). See this class's own doc comment for
    /// why this is caller-supplied at upload time, not inferred.</summary>
    public string? Supplier { get; set; }

    /// <summary>ISO 4217 currency code this quote's line prices are expressed in —
    /// <c>BenchmarkQuery.Currency</c>'s source. This codebase has no currency-conversion service
    /// anywhere (Appendix C rule 10), so an absent value here means matching cannot proceed for any
    /// line on this quote, not a silently-assumed default like <c>"USD"</c>.</summary>
    public string? Currency { get; set; }

    /// <summary>Market/region this quote applies to (country or region code, e.g. <c>"US"</c>,
    /// <c>"EU"</c>) — <c>BenchmarkQuery.Geography</c>'s source; one of the required
    /// multi-dimensional match fields spec §10.4 names (never matched on supplier name alone).</summary>
    public string? Geography { get; set; }

    /// <summary>The quote/purchase date — <c>BenchmarkQuery.PurchaseDate</c>'s source, so
    /// comparables can be filtered to a relevant window (spec §10.3). Defaulted to this row's own
    /// <see cref="CreatedAt"/> date by <c>QuoteUploadService.UploadAsync</c> when the caller does not
    /// supply one explicitly — an honest proxy ("received close to when it was uploaded"), not a
    /// guess at market conditions, so this is effectively always present once a quote exists; kept
    /// nullable for schema honesty rather than a non-nullable column with a hidden default.</summary>
    public DateOnly? PurchaseDate { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
