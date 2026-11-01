namespace Raffa.SharedKernel.Savings;

/// <summary>
/// A verifiable saving (BACKEND-SPEC §2.10 <c>saving_result</c>): what a signed contract proves, with
/// its baseline, period, method and evidence. Never an estimate.
/// </summary>
/// <param name="Kind"><c>price</c> | <c>term</c> | <c>clause</c> (negotiation results) or <c>volume</c>
/// (a quantity reduction, shown apart and never counted as a negotiation result).</param>
/// <param name="AmountAnnual">CHF per year, from the formula in <paramref name="Method"/>.</param>
/// <param name="SignedOn">The day the contract was signed (the result's date).</param>
public sealed record SavingResultRecord(
    Guid? ContractId,
    Guid? CaseId,
    string SupplierName,
    string ContractName,
    string? Category,
    string Kind,
    decimal AmountAnnual,
    string Currency,
    string Note,
    string BaselineDescription,
    decimal? BaselineValue,
    decimal? NewValue,
    decimal? Quantity,
    string Method,
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo,
    string PeriodDescription,
    Guid? EvidenceDocumentId,
    string EvidenceFileName,
    string? OwnerName,
    DateOnly SignedOn,
    string? RecordedBy)
{
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string>(StringComparer.Ordinal) { "price", "term", "clause", "volume" };

    /// <summary>Contractual price result: (old unit price − new unit price) × contracted quantity
    /// (the formula of <c>savings-quote-v4.js</c>).</summary>
    public static decimal PriceSaving(decimal oldUnitPrice, decimal newUnitPrice, decimal quantity) =>
        (oldUnitPrice - newUnitPrice) * quantity;
}

/// <summary>Records a signed result (called when a renewal closes with a signed contract).</summary>
public interface ISavingResultRecorder
{
    /// <returns>The id of the stored <c>saving_result</c>.</returns>
    Task<Guid> RecordAsync(TenantId tenantId, SavingResultRecord result, CancellationToken cancellationToken = default);
}
