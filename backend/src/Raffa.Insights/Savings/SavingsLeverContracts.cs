using Raffa.Benchmark.Contracts;
using Raffa.Insights.Application;
using Raffa.SharedKernel;

namespace Raffa.Insights.Savings;

/// <summary>The kinds of saving lever <see cref="SavingsLeverCalculator"/> can ground.</summary>
public enum SavingsLeverType
{
    /// <summary>Peers closing the same supplier obtained a discount: ask for the same on the
    /// renewal baseline.</summary>
    MarketDiscount,

    /// <summary>A priced line sits above the market band: re-price it to P50/P25.</summary>
    AboveBandRepricing,

    /// <summary>A longer term is priced lower on the market: trade term for price.</summary>
    MultiYearTerm,

    /// <summary>The contract carries a price increase / indexation clause: cap or strike it.</summary>
    UpliftCap,

    /// <summary>The notice deadline is the moment the supplier still has to compete: use it.</summary>
    NoticeTiming,

    /// <summary>Peers obtained longer payment terms.</summary>
    PaymentTerms,

    /// <summary>Volumes are committed with no right to reduce: ask for a true-down / flex.</summary>
    VolumeFlexibility,
}

/// <summary>One market deal, reduced to the fields the calculator reads (a projection of
/// <c>Raffa.Market.Contracts.MarketDeal</c>, which this module cannot reference).</summary>
public sealed record MarketDealSnapshot(
    string RecordId,
    string Product,
    string? Category,
    string Currency,
    int TermMonths,
    decimal UnitPriceP25,
    decimal UnitPriceP50,
    decimal UnitPriceP75,
    double? DiscountAchievedPct,
    double? UpliftCapPct,
    int? NoticeDays,
    string? PaymentTerms,
    int SampleSize,
    string ClosingPeriod);

public sealed record SavingsLeverInputs(
    EntityId ContractId,
    string SupplierName,
    string Currency,
    decimal? AnnualSpend,
    DateOnly? EndDate,
    DateOnly? CancellationDeadline,
    bool AutoRenewal,
    int? RenewalTermMonths,
    IReadOnlyList<PricedLine> PricedLines,
    IReadOnlyList<NegotiationClauseSnapshot> Clauses,
    string? PaymentTerms,
    IReadOnlyList<MarketDealSnapshot> MarketDeals,
    decimal? TargetAmount,
    decimal? TargetPercent,
    DateOnly AsOfDate);

/// <summary>
/// One grounded lever. Amounts are whole units of <see cref="SavingsLeverPlan.Currency"/>
/// (rounded away from zero), percentages carry two decimals — exactly the forms
/// <c>Raffa.Chat.Application.Guards.NumericGuard</c> compares against, so every number here can be
/// echoed by the answer verbatim.
/// </summary>
/// <param name="Key">Stable kebab-case identity (also the tail of the pack citation key).</param>
/// <param name="CitationKeys">The pack keys this lever is grounded in (contract facts, market
/// records, clause facts) — the caller must make every one of them a pack item.</param>
/// <param name="WhatToAsk">The ask, in one sentence, the buyer can put to the supplier.</param>
public sealed record SavingsLever(
    string Key,
    SavingsLeverType Type,
    string Label,
    decimal? EstimatedLow,
    decimal? EstimatedHigh,
    decimal? Percent,
    string Rationale,
    string WhatToAsk,
    IReadOnlyList<string> CitationKeys);

public enum SavingsFeasibility
{
    /// <summary>No quantified target was asked for.</summary>
    NoTarget,

    /// <summary>The grounded levers' high estimate covers the target.</summary>
    Reachable,

    /// <summary>The high estimate covers at least 60 percent of the target.</summary>
    Stretch,

    /// <summary>The grounded levers do not add up to the target.</summary>
    NotSupported,
}

public sealed record SavingsLeverPlan(
    EntityId ContractId,
    string SupplierName,
    string Currency,
    decimal? AnnualSpend,
    decimal? TargetAmount,
    decimal? TargetPercent,
    DateOnly? CancellationDeadline,
    int? DaysToDeadline,
    IReadOnlyList<SavingsLever> Levers,
    decimal CoverageLow,
    decimal CoverageHigh,
    SavingsFeasibility Feasibility,
    string Explanation);
