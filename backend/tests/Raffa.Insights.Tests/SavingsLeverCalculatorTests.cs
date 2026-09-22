using Raffa.Benchmark.Contracts;
using Raffa.Insights.Application;
using Raffa.Insights.Savings;
using Raffa.SharedKernel;

namespace Raffa.Insights.Tests;

/// <summary>
/// Proves <see cref="SavingsLeverCalculator"/> grounds every lever in a fact, a band, a clause or
/// a market deal — and never invents an amount when the facts do not support one.
/// </summary>
public sealed class SavingsLeverCalculatorTests
{
    private static readonly EntityId ContractId = EntityId.New();
    private static readonly DateOnly AsOf = new(2026, 9, 21);

    private static MarketDealSnapshot Deal(
        string recordId = "MKT-SNOW-EU-01",
        int termMonths = 12,
        decimal p25 = 88m, decimal p50 = 100m, decimal p75 = 118m,
        double? discount = 9d, double? upliftCap = null, int? noticeDays = null, string? paymentTerms = null,
        string currency = "EUR") =>
        new(recordId, "ITSM Professional", "Enterprise Software", currency, termMonths, p25, p50, p75, discount, upliftCap, noticeDays, paymentTerms, 40, "2026-Q2");

    private static SavingsLeverInputs Inputs(
        decimal? annualSpend = 230000m,
        DateOnly? deadline = null,
        bool autoRenewal = true,
        IReadOnlyList<PricedLine>? lines = null,
        IReadOnlyList<NegotiationClauseSnapshot>? clauses = null,
        string? paymentTerms = null,
        IReadOnlyList<MarketDealSnapshot>? deals = null,
        decimal? targetAmount = null,
        decimal? targetPercent = null) =>
        new(ContractId, "ServiceNow", "EUR", annualSpend, new DateOnly(2028, 4, 10), deadline ?? new DateOnly(2027, 10, 13), autoRenewal, 12,
            lines ?? [], clauses ?? [], paymentTerms, deals ?? [], targetAmount, targetPercent, AsOf);

    [Fact]
    public void Market_discount_is_applied_to_the_annual_spend_with_a_conservative_floor()
    {
        var plan = SavingsLeverCalculator.Compute(Inputs(deals: [Deal(discount: 9d)], targetAmount: 20000m));

        var lever = Assert.Single(plan.Levers, l => l.Type == SavingsLeverType.MarketDiscount);
        Assert.Equal(20700m, lever.EstimatedHigh);
        Assert.Equal(10350m, lever.EstimatedLow);
        Assert.Equal(9m, lever.Percent);
        Assert.Contains("market:MKT-SNOW-EU-01", lever.CitationKeys);
        Assert.Equal(8.7m, plan.TargetPercent);
        Assert.Equal(SavingsFeasibility.Reachable, plan.Feasibility);
    }

    [Fact]
    public void Above_band_priced_lines_are_repriced_to_p50_and_p25()
    {
        var line = new PricedLine(null, "ITSM Professional", 100m, 120m, "EUR", 12, new BenchmarkDistribution(88m, 100m, 118m), 40);
        var plan = SavingsLeverCalculator.Compute(Inputs(lines: [line]));

        var lever = Assert.Single(plan.Levers, l => l.Type == SavingsLeverType.AboveBandRepricing);
        Assert.Equal(2000m, lever.EstimatedLow);
        Assert.Equal(3200m, lever.EstimatedHigh);
        Assert.Contains($"fact:{ContractId}:priced-line[0].unitPrice", lever.CitationKeys);
    }

    [Fact]
    public void A_line_inside_the_band_grounds_no_repricing_lever()
    {
        var line = new PricedLine(null, "ITSM Professional", 100m, 95m, "EUR", 12, new BenchmarkDistribution(88m, 100m, 118m), 40);
        var plan = SavingsLeverCalculator.Compute(Inputs(lines: [line]));

        Assert.DoesNotContain(plan.Levers, l => l.Type == SavingsLeverType.AboveBandRepricing);
    }

    [Fact]
    public void A_cheaper_multi_year_band_grounds_the_term_lever()
    {
        var plan = SavingsLeverCalculator.Compute(Inputs(deals:
        [
            Deal(recordId: "R12", termMonths: 12, p50: 100m, discount: null),
            Deal(recordId: "R36", termMonths: 36, p50: 90m, discount: null),
        ]));

        var lever = Assert.Single(plan.Levers, l => l.Type == SavingsLeverType.MultiYearTerm);
        Assert.Equal(10m, lever.Percent);
        Assert.Equal(23000m, lever.EstimatedHigh);
        Assert.Contains("market:R36", lever.CitationKeys);
    }

    [Fact]
    public void An_uplift_clause_with_a_percentage_is_priced_against_the_market_cap()
    {
        var plan = SavingsLeverCalculator.Compute(Inputs(
            clauses: [new NegotiationClauseSnapshot("PriceIncrease", "Fees may increase by up to 8% at each renewal.")],
            deals: [Deal(discount: null, upliftCap: 5d)]));

        var lever = Assert.Single(plan.Levers, l => l.Type == SavingsLeverType.UpliftCap);
        Assert.Equal(8m, lever.Percent);
        Assert.Equal(18400m, lever.EstimatedHigh);
        Assert.Equal(6900m, lever.EstimatedLow);
        Assert.Contains($"fact:{ContractId}:clause[0]", lever.CitationKeys);
        Assert.Contains("5%", lever.WhatToAsk);
    }

    [Fact]
    public void Notice_timing_is_always_grounded_when_a_deadline_exists_and_carries_no_amount()
    {
        var plan = SavingsLeverCalculator.Compute(Inputs(deadline: new DateOnly(2027, 10, 13)));

        var lever = Assert.Single(plan.Levers, l => l.Type == SavingsLeverType.NoticeTiming);
        Assert.Null(lever.EstimatedHigh);
        Assert.Contains("2027-10-13", lever.Rationale);
        Assert.Equal(387, plan.DaysToDeadline);
    }

    [Fact]
    public void Longer_market_payment_terms_ground_a_qualitative_lever()
    {
        var plan = SavingsLeverCalculator.Compute(Inputs(paymentTerms: "Net 30", deals: [Deal(discount: null, paymentTerms: "Net 60")]));

        var lever = Assert.Single(plan.Levers, l => l.Type == SavingsLeverType.PaymentTerms);
        Assert.Contains("net 60", lever.WhatToAsk, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Committed_quantities_without_a_true_down_clause_ground_the_flexibility_lever()
    {
        var line = new PricedLine(null, "ITSM Professional", 250m, 100m, "EUR", 12, null, null);
        var plan = SavingsLeverCalculator.Compute(Inputs(lines: [line]));

        Assert.Contains(plan.Levers, l => l.Type == SavingsLeverType.VolumeFlexibility);

        var withTrueDown = SavingsLeverCalculator.Compute(Inputs(
            lines: [line],
            clauses: [new NegotiationClauseSnapshot("Volume", "Customer may reduce the number of licences at each anniversary.")]));

        Assert.DoesNotContain(withTrueDown.Levers, l => l.Type == SavingsLeverType.VolumeFlexibility);
    }

    [Fact]
    public void No_spend_and_no_market_yields_only_qualitative_levers_and_never_an_amount()
    {
        var plan = SavingsLeverCalculator.Compute(Inputs(annualSpend: null, deals: [Deal(discount: 9d)], targetAmount: 20000m));

        Assert.All(plan.Levers, l => Assert.Null(l.EstimatedHigh));
        Assert.Equal(0m, plan.CoverageHigh);
        Assert.Equal(SavingsFeasibility.NotSupported, plan.Feasibility);
    }

    [Fact]
    public void A_percentage_target_is_turned_into_an_amount_on_the_spend()
    {
        var plan = SavingsLeverCalculator.Compute(Inputs(targetPercent: 10m, deals: [Deal(discount: 9d)]));

        Assert.Equal(23000m, plan.TargetAmount);
        Assert.Equal(SavingsFeasibility.Stretch, plan.Feasibility);
    }

    [Fact]
    public void Explanation_renders_amounts_without_separators_so_the_numeric_guard_can_match_them()
    {
        var plan = SavingsLeverCalculator.Compute(Inputs(deals: [Deal(discount: 9d)], targetAmount: 20000m));

        Assert.Contains("EUR 20000", plan.Explanation);
        Assert.Contains("EUR 230000", plan.Explanation);
        Assert.Contains("8.7%", plan.Explanation);
        Assert.DoesNotContain("230,000", plan.Explanation);
    }
}
