using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Domain;

/// <summary>
/// Canonical, normalized contract record (product spec §6 core data model). Extracted facts
/// are deterministic once persisted here — the LLM proposes, domain code and human correction
/// decide what is stored (Appendix C rule 1: never store critical contract truth only inside
/// an LLM response). <see cref="SupplierId"/> is a cross-module reference by id only
/// (Suppliers/Products owns the Supplier aggregate); no physical FK crosses a bounded-context
/// boundary (ADR-002 module map, dependency-direction architecture test).
/// </summary>
public sealed class Contract : TenantScopedEntity
{
    public EntityId? SupplierId { get; set; }

    /// <summary>Amendments/renewals may override earlier terms (spec §6.1 contract hierarchy);
    /// null for a root MSA / root contract.</summary>
    public EntityId? ParentContractId { get; set; }

    public required ContractDocumentType Type { get; set; }
    public required string Status { get; set; }
    public required string Currency { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateOnly? EffectiveDate { get; set; }
    public DateOnly? CancellationDeadline { get; set; }

    public decimal? AnnualSpend { get; set; }
    public decimal? TotalContractValue { get; set; }
    public bool AutoRenewal { get; set; }
    public int? RenewalTermMonths { get; set; }
    public string? PaymentTerms { get; set; }
    public string? GoverningLaw { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Human-readable name for this contract — set to the uploaded file name at T+0, then
    /// optionally overwritten by the headline LLM if it finds a better title in the document text.
    /// Never null once the contract is created by <c>DocumentUploadService</c>; nullable only for
    /// contracts created before this feature (those row-migrated rows will still show the contract
    /// type as a fallback on the portfolio list).
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Whether this contract's identity fields (supplier, type, status, dates) have been set by
    /// the fast headline pass (<see cref="ContractIdentityState.Provisional"/>) or the full
    /// 7-stage enrich pipeline (<see cref="ContractIdentityState.Official"/>). Defaults to
    /// <see cref="ContractIdentityState.Official"/> for backward-compatibility: all contracts that
    /// existed before this feature are already fully extracted.
    /// </summary>
    public ContractIdentityState IdentityState { get; set; } = ContractIdentityState.Official;

    /// <summary>
    /// The raw supplier name string extracted by the headline pass, always persisted regardless of
    /// confidence (plan: "always persist provisional_supplier_name even below 0.8 confidence").
    /// Separate from <see cref="SupplierId"/>: the enrich pass resolves a canonical supplier from
    /// this raw string and sets <see cref="SupplierId"/>, but the raw string stays visible on
    /// the portfolio row even when no canonical match is found.
    /// </summary>
    public string? ProvisionalSupplierName { get; set; }

    /// <summary>Optimistic-concurrency guard (Appendix C rule 5 — never destructively overwrite
    /// contract history or human corrections). EF Core includes this in the WHERE clause of
    /// every UPDATE/DELETE (<see cref="Infrastructure.Configurations.ContractConfiguration"/>),
    /// so a write against a stale read fails with
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> instead of
    /// silently clobbering a concurrent re-extraction or human correction. The correction
    /// write-path increments it on every accepted change; this schema only carries the guard.
    /// </summary>
    public int Version { get; set; } = 1;
}
