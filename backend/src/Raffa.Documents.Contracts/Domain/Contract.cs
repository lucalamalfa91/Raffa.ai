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
