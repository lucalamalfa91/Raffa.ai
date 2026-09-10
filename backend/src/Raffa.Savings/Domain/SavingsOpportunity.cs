using Raffa.SharedKernel;

namespace Raffa.Savings.Domain;

/// <summary>
/// A trackable savings opportunity (task E04/F02/US02/T01, savings-opportunity; parent story
/// us-02-savings-opportunity AC-2 "SavingsOpportunity captures supplier/contract/type/current-spend/
/// estimated-range/confidence/status/owner"; product spec §6 core data model row "SavingsOpportunity
/// | supplier, contract/quote, type, current_spend, estimated savings range, confidence, status,
/// owner"; module-map.md "Savings | SavingsOpportunity, RealizedSavings | /api/savings").
///
/// <para>
/// <see cref="SupplierId"/>/<see cref="ContractId"/> are cross-module references by id only —
/// deliberately no foreign key, the same treatment
/// <c>Raffa.Documents.Contracts.Domain.Contract.SupplierId</c> and
/// <c>Raffa.Renewals.Domain.RenewalAction.ContractId</c> already give their own cross-module
/// references: ADR-002 forbids <c>Raffa.Savings</c> from referencing
/// <c>Raffa.Suppliers.Products</c> or <c>Raffa.Documents.Contracts</c> at all
/// (`Raffa.ArchitectureTests.DependencyDirectionTests`'s allow-list for this module is exactly
/// `[SharedKernel, Benchmark]`), so this entity structurally cannot validate that either id names an
/// existing, tenant-owned row — that cross-module existence check, if ever added, belongs in
/// `Raffa.Api`, "the one project allowed to reference every module"
/// (`backend/README.md` "Dependency direction"). Both are nullable: spec §6 names the field pair
/// "contract/quote" (an opportunity may originate from a portfolio contract comparison, in scope
/// this wave, or — R4, out of scope per epic-04's own "Out of scope" list — a new-purchase quote
/// match); only <see cref="ContractId"/> exists today, and a <c>QuoteId</c> column is that future
/// task's own migration to add, not invented here ahead of need.
/// </para>
///
/// <para>
/// <see cref="Type"/> is free text, not a closed enum: unlike <see cref="Status"/> (see that
/// property's own doc comment), no ADR or spec fixes a vocabulary for "type" of savings opportunity,
/// and — unlike <c>Raffa.Renewals.Domain.RenewalActionStatus</c>'s own "closed because only a
/// human sets it" reasoning — this field is set by whatever process identifies the opportunity
/// (today: a test or a future real caller, not yet a human through this API), so inventing a
/// taxonomy the product spec never gave would be a fabricated decision (Appendix C rule 10), not a
/// discovered one.
/// </para>
/// </summary>
public sealed class SavingsOpportunity : TenantScopedEntity
{
    public EntityId? SupplierId { get; set; }

    public EntityId? ContractId { get; set; }

    public required string Type { get; set; }

    /// <summary>The current spend this opportunity is evaluated against — e.g.
    /// <c>Raffa.Savings.Application.PriceComparisonRequest.CurrentTotalCost</c>, or the
    /// contract's own <c>annual_cost</c>/<c>total_cost</c> (spec §6's <c>ContractLineItem</c> row) —
    /// in <see cref="Currency"/>.</summary>
    public required decimal CurrentSpend { get; set; }

    /// <summary>ISO 4217 currency code <see cref="CurrentSpend"/>/<see cref="EstimatedSavingsLow"/>/
    /// <see cref="EstimatedSavingsHigh"/> are expressed in — same "every money value in this module
    /// carries its own explicit currency" convention
    /// <c>Raffa.Benchmark.Contracts.BenchmarkQuery.Currency</c>/<c>BenchmarkResult.Currency</c>
    /// already establish; this codebase has no currency-conversion service anywhere (Appendix C rule
    /// 10), so a bare, currency-less decimal would be misleading.</summary>
    public required string Currency { get; set; }

    /// <summary>The more conservative end of the estimated total saving — typically
    /// <c>Raffa.Savings.Application.PriceComparisonResult.TotalSavingsRangeLow</c> — never
    /// negative.</summary>
    public required decimal EstimatedSavingsLow { get; set; }

    /// <summary>The more aggressive end of the estimated total saving — typically
    /// <c>PriceComparisonResult.TotalSavingsRangeHigh</c> — always <c>&gt;=</c>
    /// <see cref="EstimatedSavingsLow"/>.</summary>
    public required decimal EstimatedSavingsHigh { get; set; }

    /// <summary>Raffa's own confidence score in <c>[0, 1]</c> for this opportunity — echoes
    /// <c>Raffa.Benchmark.Contracts.BenchmarkResult.Confidence</c> (spec §4.3 "Show benchmark
    /// confidence and provenance"), not re-derived here.</summary>
    public required double Confidence { get; set; }

    /// <summary>The Procurement workflow lifecycle — see <see cref="SavingsOpportunityStatus"/>'s
    /// own doc comment.</summary>
    public required SavingsOpportunityStatus Status { get; set; }

    /// <summary>Free-text — who is tracking/pursuing this opportunity. <see langword="null"/> until
    /// assigned via `PATCH /api/savings/{id}` (unlike
    /// <c>Raffa.Renewals.Domain.RenewalAction.Owner</c>, which is always set at the same time as
    /// the row itself — a <see cref="SavingsOpportunity"/> can be
    /// <see cref="SavingsOpportunityStatus.Identified"/> before anyone owns it). Not a foreign key to
    /// a workspace member — same interim gap as every other free-text "owner" in this codebase
    /// (ADR-010 not wired in yet); see <c>RenewalAction.Owner</c>'s own doc comment.</summary>
    public string? Owner { get; set; }

    /// <summary>When this opportunity was identified (caller-supplied via <c>IClock</c>, not a
    /// database default) — orders `GET /api/savings`'s list, newest first.</summary>
    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>When this row was last written (identify or a later `PATCH`) — same "no hidden
    /// clock" convention every other timestamped write in this codebase follows.</summary>
    public required DateTimeOffset UpdatedAt { get; set; }
}
