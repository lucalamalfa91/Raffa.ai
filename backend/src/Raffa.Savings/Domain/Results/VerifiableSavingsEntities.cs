using Raffa.SharedKernel;

namespace Raffa.Savings.Domain.Results;

/// <summary>
/// <c>saving_result</c> (BACKEND-SPEC §2.10): a result a signed contract proves — baseline, period,
/// method, evidence. <see cref="CountsAsNegotiation"/> is false for <c>volume</c> (a quantity
/// reduction, shown apart).
/// </summary>
public sealed class SavingResult : TenantScopedEntity
{
    public Guid? ContractId { get; set; }
    public Guid? CaseId { get; set; }
    public Guid? SupplierId { get; set; }
    public required string SupplierName { get; set; }
    public required string ContractName { get; set; }
    public string? Category { get; set; }

    /// <summary>price | term | clause | volume.</summary>
    public required string Kind { get; set; }
    public bool CountsAsNegotiation { get; set; }
    public decimal AmountAnnual { get; set; }
    public required string Currency { get; set; }
    public required string Note { get; set; }
    public required string BaselineDesc { get; set; }
    public decimal? BaselineValue { get; set; }
    public decimal? NewValue { get; set; }
    public decimal? Quantity { get; set; }
    public required string Method { get; set; }
    public DateOnly? PeriodFrom { get; set; }
    public DateOnly? PeriodTo { get; set; }
    public required string PeriodDesc { get; set; }
    public Guid? EvidenceDocumentId { get; set; }
    public required string EvidenceFileName { get; set; }
    public string? OwnerName { get; set; }
    public DateOnly SignedOn { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string? RecordedBy { get; set; }
}

/// <summary><c>invoice</c>: a supplier invoice — the only thing that proves a saving is realized.</summary>
public sealed class Invoice : TenantScopedEntity
{
    public Guid? SupplierId { get; set; }
    public required string SupplierName { get; set; }
    public Guid? ContractId { get; set; }

    /// <summary>First day of the invoiced month.</summary>
    public DateOnly Period { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public Guid? DocumentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary><c>saving_realization</c>: the part of a result one invoice proves.</summary>
public sealed class SavingRealization : TenantScopedEntity
{
    public required EntityId SavingResultId { get; set; }
    public required EntityId InvoiceId { get; set; }
    public decimal RealizedAmount { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
}

/// <summary><c>savings_goal</c>: the user's goal in their own words, parsed into a target. One active
/// goal per user.</summary>
public sealed class SavingsGoal : TenantScopedEntity
{
    /// <summary>The caller's identity (<c>oid</c>).</summary>
    public required string UserId { get; set; }
    public required string SourceText { get; set; }
    public decimal? Pct { get; set; }
    public decimal? AnnualAmount { get; set; }

    /// <summary>jsonb: <c>{supplier, categories[], contractIds[], label{id,vars}}</c>.</summary>
    public required string ScopeJson { get; set; }

    /// <summary>"FY 2026" | "FY 2027" | "this quarter" (message ids).</summary>
    public required string Period { get; set; }
    public bool Active { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary><c>case_objective</c>: the target the user set in a renewal case (never an estimate).</summary>
public sealed class CaseObjective : TenantScopedEntity
{
    public Guid CaseId { get; set; }
    public Guid ContractId { get; set; }
    public string? OwnerName { get; set; }

    /// <summary>null | none | p5 | p10 | budget | custom.</summary>
    public string? Kind { get; set; }
    public decimal? Pct { get; set; }
    public decimal? Amount { get; set; }
    public string? Text { get; set; }

    /// <summary>null while open; renewed | terminated.</summary>
    public string? Outcome { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
