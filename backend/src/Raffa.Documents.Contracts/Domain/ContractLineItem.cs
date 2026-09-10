using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Domain;

/// <summary>
/// A single priced line on a <see cref="Contract"/> — one row of the commercial/pricing table
/// (product spec §6 "ContractLineItem"): the product/SKU purchased, quantity, unit economics,
/// and billing cadence. <see cref="ProductId"/> is a cross-module reference by id only
/// (Suppliers/Products owns the Product aggregate, per the module map's dependency-direction
/// rule) — no physical FK crosses that bounded-context boundary, the same treatment
/// <see cref="Contract.SupplierId"/> already gets. Price/SKU/line-item extraction is its own
/// bounded extraction stage (spec §7.2), so every line item carries the same evidence pointer
/// and confidence score as a <see cref="Clause"/> (Appendix C rule 2: never show a consequential
/// extracted fact without source evidence and confidence metadata).
/// </summary>
public sealed class ContractLineItem : TenantScopedEntity
{
    public required EntityId ContractId { get; set; }
    public EntityId? ProductId { get; set; }

    public string? Sku { get; set; }
    public required string Description { get; set; }

    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? ListPrice { get; set; }

    /// <summary>Percentage off list price (0-100), when the source document expresses the
    /// discount as a rate rather than only as a lower <see cref="UnitPrice"/>.</summary>
    public decimal? Discount { get; set; }

    public string? BillingPeriod { get; set; }
    public decimal? AnnualCost { get; set; }
    public decimal? TotalCost { get; set; }

    /// <summary>Evidence pointer + page/section span + confidence (Appendix C rule 2; spec §7.3
    /// "every extracted fact carries source span + confidence"), mirroring
    /// <see cref="Clause.SourceDocumentId"/>/<see cref="Clause.SourceSpan"/>/
    /// <see cref="Clause.SourcePage"/>/<see cref="Clause.Confidence"/>. Added by task
    /// E02/F01/US02/T01 (us-02-staged-extraction, AC-2) — this task's `price/SKU` stage is the
    /// first writer of this entity that needs it; the original contract-schema task
    /// (E02/F02/US01/T01) had no extraction caller yet.
    ///
    /// NOTE: this block has repeatedly been re-duplicated by independent phase-barrier merges
    /// landing the same "add evidence pointer" change from more than one converging task, each
    /// time failing the build with CS0102 ("already contains a definition") until the next
    /// implementer collapsed it back to one declaration per member — see git history on this file.
    /// The migration and EF configuration (<see cref="Infrastructure.Configurations
    /// .ContractLineItemConfiguration"/>) have only ever added/referenced one physical column per
    /// member throughout, so collapsing duplicate C# declarations back to one is never a schema
    /// change.</summary>
    public EntityId? SourceDocumentId { get; set; }
    public string? SourceSpan { get; set; }
    public int? SourcePage { get; set; }
    public double? Confidence { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>Optimistic-concurrency guard — see <see cref="Contract.Version"/>.</summary>
    public int Version { get; set; } = 1;
}
