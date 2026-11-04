using Raffa.SharedKernel.Contracts;

namespace Raffa.Savings.Application.Spend;

/// <summary>How a contract's value is placed in time: by the days it covers, or by the day it is invoiced.</summary>
public static class SpendBasis
{
    /// <summary>Competenza: the value of each day the contract runs.</summary>
    public const string Accrual = "accrual";

    /// <summary>Cassa: the value of each billing period, on the day it starts (billed in advance).</summary>
    public const string Cash = "cash";

    public static bool IsKnown(string? basis) => basis is Accrual or Cash;
}

/// <summary>What a stretch of a contract's timeline is, and so how sure its value is.</summary>
public static class SpendSegmentKind
{
    /// <summary>A term a document states (the contract, or a recorded renewal).</summary>
    public const string Documented = "documented";

    /// <summary>Renewed by itself: the notice deadline passed, so the term is binding. Valued at the
    /// previous term's price (the contract may allow an increase, shown apart).</summary>
    public const string RolledOver = "rolledOver";

    /// <summary>Will renew by itself unless notice is given in time; at the current price.</summary>
    public const string AutoRenewal = "autoRenewal";

    /// <summary>The term ends with no automatic renewal: nothing is committed; at the current price.</summary>
    public const string ToDecide = "toDecide";
}

/// <summary>Why a contract is left out of the totals (it is listed, never estimated).</summary>
public static class SpendExclusionReason
{
    /// <summary>No term with both a start and an end date was read.</summary>
    public const string NoDates = "noDates";

    /// <summary>No term states an annual or a total value.</summary>
    public const string NoValue = "noValue";

    /// <summary>The contract is in a currency the admin set no budget rate for.</summary>
    public const string NoRate = "noRate";

    /// <summary>Cash basis only: no billing period was read from the priced lines.</summary>
    public const string NoBillingPeriod = "noBillingPeriod";
}

/// <summary>Notes on one contract row.</summary>
public static class SpendNote
{
    /// <summary>Some future stretch is valued at the last documented price.</summary>
    public const string PreviousTermPrice = "previousTermPrice";

    /// <summary>It renews by itself but the renewal term was not read: the renewal runs to the horizon.</summary>
    public const string RenewalTermUnknown = "renewalTermUnknown";

    /// <summary>A documented term states no value: its days count nothing.</summary>
    public const string PartialValue = "partialValue";

    /// <summary>No automatic renewal and the last term ended: nothing after it is counted.</summary>
    public const string Expired = "expired";
}

/// <summary>One validated contract as the spend calculator reads it.</summary>
/// <param name="NoticeGapDays">Days between a term's end and its last day to give notice.</param>
/// <param name="Uplift">The increase the contract allows at renewal, as read ("5%", "CPI+2%"); "" when none.</param>
public sealed record SpendContractInput(
    Guid ContractId,
    string Supplier,
    string Name,
    string? Category,
    IReadOnlyList<DocumentedContractTerm> Terms,
    bool AutoRenew,
    int? RenewalTermMonths,
    int NoticeGapDays,
    string Uplift,
    string? BillingPeriod);

/// <summary>A fiscal quarter of the report.</summary>
public sealed record SpendPeriod(string Key, int FiscalYear, int Quarter, string Label, DateOnly From, DateOnly To);

/// <param name="Documented">Contracts with a documented term in this fiscal year.</param>
/// <param name="Contracts">Contracts in the totals.</param>
public sealed record SpendFiscalYear(int FiscalYear, string Label, DateOnly From, DateOnly To, int Documented, int Contracts);

/// <summary>A stretch of a contract's timeline. <see cref="AnnualValue"/> is in the reporting
/// currency, null when the term states only a total value.</summary>
public sealed record SpendSegment(DateOnly From, DateOnly To, string Kind, string Source, decimal? AnnualValue, bool PreviousTermPrice);

/// <summary>The next decision on a contract: the last day to give notice, and the end of the term it applies to.</summary>
public sealed record SpendDecision(DateOnly? DecideBy, DateOnly? EndsOn, bool AutoRenew, int? RenewalTermMonths, string Uplift);

/// <summary>
/// One contract's value per period (aligned with <see cref="SpendReport.Periods"/>), in the reporting
/// currency: <see cref="Actual"/> already ran or was invoiced, <see cref="Committed"/> is binding
/// and still ahead, <see cref="AutoRenewal"/> follows unless notice is given, <see cref="ToDecide"/>
/// depends on a decision.
/// </summary>
/// <param name="Rate">Units of the reporting currency per unit of <see cref="Currency"/>.</param>
public sealed record SpendContractRow(
    Guid ContractId,
    string Supplier,
    string Name,
    string Category,
    string Currency,
    decimal Rate,
    string? BillingPeriod,
    IReadOnlyList<decimal> Actual,
    IReadOnlyList<decimal> Committed,
    IReadOnlyList<decimal> AutoRenewal,
    IReadOnlyList<decimal> ToDecide,
    IReadOnlyList<SpendSegment> Segments,
    SpendDecision Decision,
    IReadOnlyList<string> Notes)
{
    /// <summary>Signed results (negotiated, not quantity reductions) of the contract, per period:
    /// their annual value over the days of their period.</summary>
    public IReadOnlyList<decimal> SavingsContractual { get; init; } = [];

    /// <summary>What invoices prove of those results, in the period of the invoiced month.</summary>
    public IReadOnlyList<decimal> SavingsRealized { get; init; } = [];

    /// <summary>Spend no longer paid because quantities were cut: shown apart, never a saving.</summary>
    public IReadOnlyList<decimal> QuantityReduction { get; init; } = [];

    /// <summary>The objective of the contract's open case (a user target, not signed), over the term it negotiates.</summary>
    public IReadOnlyList<decimal> Objective { get; init; } = [];
}

/// <summary>Why a saving or an objective is in the list but not in the per-period figures.</summary>
public static class SavingExclusionReason
{
    /// <summary>In a currency the admin set no budget rate for.</summary>
    public const string NoRate = "noRate";

    /// <summary>The result states no period, so its value cannot be put in time.</summary>
    public const string NoPeriod = "noPeriod";

    /// <summary>The objective's contract has no stated annual value, or its term end is unknown.</summary>
    public const string NoBase = "noBase";
}

/// <summary>
/// A recorded result (<c>saving_result</c>), as the register shows it: what it is, its base, period,
/// method and evidence, and what invoices proved. Amounts in the reporting currency (null with
/// <see cref="Excluded"/> = <see cref="SavingExclusionReason.NoRate"/>).
/// </summary>
/// <param name="CountsAsNegotiation">False for a quantity reduction (shown apart).</param>
/// <param name="ProvenTo">The last day of the last invoiced month that proves it.</param>
public sealed record SpendSavingEntry(
    Guid Id,
    Guid? ContractId,
    string Supplier,
    string Contract,
    string Kind,
    bool CountsAsNegotiation,
    decimal AmountAnnual,
    string Currency,
    decimal? AmountAnnualReporting,
    DateOnly? From,
    DateOnly? To,
    DateOnly SignedOn,
    string Note,
    string Baseline,
    string Period,
    string Method,
    string Evidence,
    decimal RealizedReporting,
    DateOnly? ProvenTo,
    int InvoiceCount,
    string? Excluded);

/// <summary>The objective of an open renewal case, in the reporting currency, over the term it negotiates.</summary>
/// <param name="AssumedTermMonths">True when the renewal term was not read and 12 months are used.</param>
public sealed record SpendObjectiveEntry(
    Guid ContractId,
    Guid CaseId,
    string Supplier,
    string? Kind,
    decimal? Pct,
    decimal? AnnualTarget,
    DateOnly? From,
    DateOnly? To,
    string? Owner,
    bool AssumedTermMonths,
    string? Excluded);

/// <summary>Savings of results whose contract is not among the validated ones, per period.</summary>
public sealed record SpendOtherSavings(
    IReadOnlyList<decimal> SavingsContractual,
    IReadOnlyList<decimal> SavingsRealized,
    IReadOnlyList<decimal> QuantityReduction);

/// <summary>A saving result as the allocator reads it (from <c>saving_result</c> and its realizations).</summary>
public sealed record SavingInput(
    Guid Id,
    Guid? ContractId,
    string Supplier,
    string Contract,
    string Kind,
    bool CountsAsNegotiation,
    decimal AmountAnnual,
    string Currency,
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo,
    DateOnly SignedOn,
    string Note,
    string Baseline,
    string Period,
    string Method,
    string Evidence,
    IReadOnlyList<SavingRealizationInput> Realizations);

/// <summary>The part of a result one invoice proves, and the invoiced month (its first day).</summary>
public sealed record SavingRealizationInput(DateOnly InvoiceMonth, decimal Amount);

/// <summary>A contract left out of the totals, and why.</summary>
public sealed record SpendExclusion(Guid ContractId, string Supplier, string Name, string Reason, string Currency);

/// <summary><c>GET /api/savings/spend</c>: the value of the validated contracts per fiscal quarter.</summary>
public sealed record SpendReport(
    string Currency,
    string Basis,
    int FiscalYearStartMonth,
    int CurrentFiscalYear,
    DateOnly Today,
    IReadOnlyList<SpendPeriod> Periods,
    IReadOnlyList<SpendFiscalYear> FiscalYears,
    IReadOnlyList<SpendContractRow> Contracts,
    IReadOnlyList<SpendExclusion> Excluded)
{
    /// <summary>The recorded results (contractual and quantity reductions), newest first.</summary>
    public IReadOnlyList<SpendSavingEntry> Savings { get; init; } = [];

    /// <summary>The objectives of the open renewal cases.</summary>
    public IReadOnlyList<SpendObjectiveEntry> Objectives { get; init; } = [];

    /// <summary>Savings of results not tied to a validated contract, per period.</summary>
    public SpendOtherSavings? OtherSavings { get; init; }
}
