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
    IReadOnlyList<string> Notes);

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
    IReadOnlyList<SpendExclusion> Excluded);
