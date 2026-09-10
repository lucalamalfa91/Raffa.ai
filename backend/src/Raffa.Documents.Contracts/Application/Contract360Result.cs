using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// Outcome of a successful <see cref="Contract360QueryService.GetByIdAsync"/> call — the full
/// Contract 360 aggregate task E02/F03/US02/T01 (us-02-contract-360-aggregate, AC-1) returns for
/// `GET /api/contracts/{id}`: a header plus every tab product spec §8.2 names ("Header: supplier,
/// contract name/type, annual spend, TCV, start/end, renewal date, cancellation deadline. Tabs:
/// Overview, Commercials, Products, Clauses, Obligations, Risks, Documents, Benchmark, Renewal,
/// Activity."). Story us-02's own "Dependencies" row says "360 reuses portfolio endpoint" —
/// <see cref="Contract360Header"/> below is deliberately the same column set as
/// <see cref="PortfolioListItem"/> (`GET /api/contracts`'s row shape) plus the one extra field
/// spec §8.2's header line names beyond spec §8.1's portfolio columns (<c>TotalContractValue</c>,
/// "TCV").
///
/// AC-2 ("Commercials/products read from StructuredContracts + line items; clauses/obligations/
/// risks from extracted facts"): <see cref="Commercials"/>/<see cref="Products"/> are read from
/// <see cref="Contract"/> ("StructuredContracts") and <see cref="ContractLineItem"/>;
/// <see cref="Clauses"/>/<see cref="Obligations"/>/<see cref="Risks"/> come from the extraction
/// pipeline's own tables (<see cref="Clause"/>/<see cref="Obligation"/>/<see cref="Risk"/>).
///
/// <see cref="Benchmark"/> and <see cref="Activity"/> are always empty in this wave — us-02's own
/// "Task-count note": "the benchmark/activity tabs placeholder (R3/R4); they read only validated
/// data and return empty until later waves." Present as empty arrays (not an omitted JSON field)
/// so the web client never has to special-case a missing tab key.
/// </summary>
public sealed record Contract360Result(
    EntityId ContractId,
    Contract360Header Header,
    Contract360Overview Overview,
    Contract360Commercials Commercials,
    IReadOnlyList<Contract360ProductLineItem> Products,
    IReadOnlyList<Contract360Clause> Clauses,
    IReadOnlyList<Contract360Obligation> Obligations,
    IReadOnlyList<Contract360Risk> Risks,
    IReadOnlyList<Contract360Document> Documents,
    IReadOnlyList<Contract360BenchmarkEntry> Benchmark,
    Contract360Renewal Renewal,
    IReadOnlyList<Contract360ActivityEntry> Activity);

/// <summary>
/// Contract 360 header (product spec §8.2: "supplier, contract name/type, annual spend, TCV,
/// start/end, renewal date, cancellation deadline"). Same column set and the same two proxy
/// decisions as <see cref="PortfolioListItem"/> (<see cref="SupplierId"/> is a bare id — Suppliers/
/// Products has no domain types yet; <see cref="Type"/> stands in for "contract name" —
/// <see cref="Contract"/> has no title field yet), plus <see cref="TotalContractValue"/> ("TCV"),
/// the one field spec §8.2's header line names beyond spec §8.1's portfolio columns.
/// <see cref="RenewalDate"/> is derived the same way as the portfolio row: equal to
/// <see cref="EndDate"/> only when <see cref="AutoRenewal"/> is true, otherwise null (a contract
/// that does not auto-renew has no next renewal date, only an end date). <see cref="Status"/>/
/// <see cref="AutoRenewal"/>/<see cref="Risk"/> are carried too even though spec's header sentence
/// does not name them, so the 360 header alone still identifies the same "Status"/"Risk" a user
/// just triaged on the portfolio screen before drilling in — <see cref="Risk"/> is computed the
/// same way as <see cref="PortfolioListItem.Risk"/> (the highest <see cref="RiskSeverity"/> across
/// this contract's <see cref="Risk"/> rows, null when it has none).
/// </summary>
public sealed record Contract360Header(
    EntityId ContractId,
    EntityId? SupplierId,
    ContractDocumentType Type,
    string Status,
    decimal? AnnualSpend,
    decimal? TotalContractValue,
    DateOnly? StartDate,
    DateOnly? EndDate,
    DateOnly? RenewalDate,
    DateOnly? CancellationDeadline,
    bool AutoRenewal,
    RiskSeverity? Risk);

/// <summary>
/// Overview tab: the descriptive/administrative <see cref="Contract"/> fields not already
/// surfaced on <see cref="Contract360Header"/>, so the two tabs do not just repeat one another —
/// an implementer judgment call (spec §8.2 names the tab, not its fields), not a re-litigated
/// council decision.
/// </summary>
public sealed record Contract360Overview(
    string Currency,
    DateOnly? EffectiveDate,
    int? RenewalTermMonths,
    string? PaymentTerms,
    string? GoverningLaw,
    EntityId? ParentContractId,
    int Version,
    DateTimeOffset CreatedAt);

/// <summary>
/// Commercials tab (AC-2: read from "StructuredContracts + line items"): the contract-level
/// commercial terms already on <see cref="Contract"/> itself, plus a rollup over this contract's
/// <see cref="ContractLineItem"/> rows (<see cref="Contract360ProductLineItem"/> on the Products
/// tab is the per-line detail). <see cref="LineItemAnnualCostTotal"/>/
/// <see cref="LineItemTotalCostTotal"/> are null only when there are zero line items to sum; a
/// line item with a missing per-line cost contributes zero to the total rather than making the
/// whole rollup null, so one incompletely-extracted line does not hide the rest.
/// </summary>
public sealed record Contract360Commercials(
    decimal? AnnualSpend,
    decimal? TotalContractValue,
    string Currency,
    string? PaymentTerms,
    bool AutoRenewal,
    int? RenewalTermMonths,
    int LineItemCount,
    decimal? LineItemAnnualCostTotal,
    decimal? LineItemTotalCostTotal);

/// <summary>
/// One row of the Products tab (AC-2: "products read from ... line items") — every
/// <see cref="ContractLineItem"/> for this contract, evidence included (Appendix C rule 2).
/// </summary>
public sealed record Contract360ProductLineItem(
    EntityId LineItemId,
    EntityId? ProductId,
    string? Sku,
    string Description,
    decimal? Quantity,
    string? Unit,
    decimal? UnitPrice,
    decimal? ListPrice,
    decimal? Discount,
    string? BillingPeriod,
    decimal? AnnualCost,
    decimal? TotalCost,
    EntityId? SourceDocumentId,
    string? SourceSpan,
    int? SourcePage,
    double? Confidence);

/// <summary>
/// Clauses tab (AC-2: "clauses ... from extracted facts") — every <see cref="Clause"/> for this
/// contract, evidence included (Appendix C rule 2).
/// </summary>
public sealed record Contract360Clause(
    EntityId ClauseId,
    string ClauseType,
    string RawText,
    string? NormalizedValue,
    RiskSeverity? RiskLevel,
    EntityId? SourceDocumentId,
    string? SourceSpan,
    int? SourcePage,
    double? Confidence);

/// <summary>
/// Obligations tab (AC-2: "obligations ... from extracted facts") — every
/// <see cref="Obligation"/> for this contract, evidence included (Appendix C rule 2).
/// </summary>
public sealed record Contract360Obligation(
    EntityId ObligationId,
    string Party,
    string ObligationType,
    string Description,
    DateOnly? DueDate,
    string? RecurrenceRule,
    string? Criticality,
    string? Status,
    EntityId? SourceDocumentId,
    string? SourceSpan,
    int? SourcePage,
    double? Confidence);

/// <summary>
/// Risks tab (AC-2: "risks ... from extracted facts") — every <see cref="Risk"/> for this
/// contract, evidence included (Appendix C rule 2).
/// </summary>
public sealed record Contract360Risk(
    EntityId RiskId,
    string RiskType,
    RiskSeverity Severity,
    string Description,
    string? Status,
    EntityId? ClauseId,
    EntityId? SourceDocumentId,
    string? SourceSpan,
    int? SourcePage,
    double? Confidence);

/// <summary>
/// Documents tab — every <see cref="Document"/> linked to this contract (mirrors
/// <see cref="DocumentMetadataResult"/>'s field set, the same shape `GET /api/documents/{id}`
/// already returns for one document).
/// </summary>
public sealed record Contract360Document(
    EntityId DocumentId,
    string FileName,
    string MimeType,
    ContractDocumentType DocumentType,
    DocumentProcessingStatus ProcessingStatus,
    DateTimeOffset CreatedAt);

/// <summary>
/// Renewal tab: the deterministic renewal/cancellation fields already recorded on
/// <see cref="Contract"/> itself. The full Renewal Intelligence module (priority score,
/// recommended action — product spec §9) is R2 scope with its own `GET /api/renewals` endpoint
/// (Appendix A Core API Catalogue); this tab only ever shows what the Documents/Contracts bounded
/// context already knows — same "Renewal" derivation as <see cref="Contract360Header.RenewalDate"/>
/// (equals <see cref="EndDate"/> only when <see cref="AutoRenewal"/> is true).
/// </summary>
public sealed record Contract360Renewal(
    DateOnly? EndDate,
    DateOnly? RenewalDate,
    DateOnly? CancellationDeadline,
    bool AutoRenewal,
    int? RenewalTermMonths);

/// <summary>
/// Placeholder row shape for the Benchmark tab. <c>Raffa.Benchmark</c> is an interface-only
/// scaffold with no provider adapter until R3 (backend/README.md solution layout); us-02's own
/// "Task-count note" scopes this tab to "read only validated data and return empty until later
/// waves." No members yet — there is nothing validated to show. A named type (not
/// <see cref="object"/>) so R3 has an obvious, discoverable place to add fields instead of
/// inventing the array-vs-object question then.
/// </summary>
public sealed record Contract360BenchmarkEntry;

/// <summary>
/// Placeholder row shape for the Activity tab — same rationale as
/// <see cref="Contract360BenchmarkEntry"/>. No activity/timeline source is wired to this endpoint
/// yet (R3/R4 per us-02's "Task-count note").
/// </summary>
public sealed record Contract360ActivityEntry;
