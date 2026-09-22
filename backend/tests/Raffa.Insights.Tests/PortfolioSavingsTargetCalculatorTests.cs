using Raffa.Insights.Savings;
using Raffa.SharedKernel;

namespace Raffa.Insights.Tests;

public sealed class PortfolioSavingsTargetCalculatorTests
{
    private static readonly DateOnly AsOf = new(2026, 9, 21);

    private static SavingsLeverPlan Plan(EntityId id, string supplier, decimal low, decimal high, string currency = "EUR", DateOnly? deadline = null)
    {
        var levers = high > 0
            ? new List<SavingsLever>
            {
                new("market-discount", SavingsLeverType.MarketDiscount, "Market discount", low, high, 9m, "r", "ask", ["market:X"]),
                new("notice-timing", SavingsLeverType.NoticeTiming, "Notice", null, null, null, "r", "ask", []),
            }
            : new List<SavingsLever> { new("notice-timing", SavingsLeverType.NoticeTiming, "Notice", null, null, null, "r", "ask", []) };

        return new SavingsLeverPlan(id, supplier, currency, 100000m, null, null, deadline, null, levers, low, high, SavingsFeasibility.NoTarget, "x");
    }

    private static PortfolioSavingsCandidateInputs Candidate(string supplier, decimal low, decimal high, DateOnly? deadline, string currency = "EUR")
    {
        var id = EntityId.New();
        return new(id, supplier, currency, 100000m, deadline, deadline?.AddMonths(6), true, Plan(id, supplier, low, high, currency, deadline));
    }

    [Fact]
    public void Candidates_inside_the_window_are_ranked_by_high_estimate_and_summed_cumulatively()
    {
        var plan = PortfolioSavingsTargetCalculator.Compute(
            [
                Candidate("Zoom", 5000m, 12000m, AsOf.AddDays(20)),
                Candidate("ServiceNow", 10000m, 30000m, AsOf.AddDays(60)),
                Candidate("Salesforce", 20000m, 50000m, AsOf.AddDays(200)),
            ],
            targetAmount: 40000m,
            windowDays: 90,
            AsOf);

        Assert.Equal(["ServiceNow", "Zoom"], plan.InWindow.Select(c => c.SupplierName));
        Assert.Equal([30000m, 42000m], plan.InWindow.Select(c => c.RunningCumulativeHigh));
        Assert.Equal(["Salesforce"], plan.BeyondWindow.Select(c => c.SupplierName));
        Assert.Equal(42000m, plan.CoverageHigh);
        Assert.Equal(SavingsFeasibility.Reachable, plan.Feasibility);
    }

    [Fact]
    public void A_contract_with_no_fixed_deadline_can_be_acted_on_now()
    {
        var plan = PortfolioSavingsTargetCalculator.Compute([Candidate("AWS", 8000m, 16000m, deadline: null)], 10000m, 90, AsOf);

        var only = Assert.Single(plan.InWindow);
        Assert.True(only.InWindow);
        Assert.Contains("opened now", only.WhyNow);
    }

    [Fact]
    public void A_passed_deadline_moves_the_contract_to_the_next_period()
    {
        var plan = PortfolioSavingsTargetCalculator.Compute([Candidate("Okta", 1000m, 2000m, AsOf.AddDays(-5))], 1000m, 90, AsOf);

        Assert.Empty(plan.InWindow);
        Assert.Single(plan.BeyondWindow);
        Assert.Equal(SavingsFeasibility.NotSupported, plan.Feasibility);
    }

    [Fact]
    public void Contracts_without_a_quantified_lever_are_never_summed()
    {
        var plan = PortfolioSavingsTargetCalculator.Compute(
            [Candidate("Notion", 0m, 0m, AsOf.AddDays(10)), Candidate("Slack", 3000m, 6000m, AsOf.AddDays(10))],
            5000m, 90, AsOf);

        Assert.Equal(6000m, plan.CoverageHigh);
        Assert.Equal(2, plan.InWindow.Count);
    }

    [Fact]
    public void A_second_currency_is_listed_but_not_summed()
    {
        var plan = PortfolioSavingsTargetCalculator.Compute(
            [Candidate("Salesforce", 10000m, 20000m, AsOf.AddDays(10), "USD"), Candidate("SAP", 30000m, 60000m, AsOf.AddDays(10), "EUR")],
            50000m, 90, AsOf);

        Assert.Equal("EUR", plan.Currency);
        Assert.Equal(60000m, plan.CoverageHigh);
        Assert.Contains(plan.InWindow, c => c.Currency == "USD" && c.WhyNow.Contains("not summed", StringComparison.Ordinal));
    }

    [Fact]
    public void Default_window_is_ninety_days_when_none_is_named()
    {
        var plan = PortfolioSavingsTargetCalculator.Compute([], null, null, AsOf);

        Assert.Equal(90, plan.WindowDays);
        Assert.Equal(SavingsFeasibility.NoTarget, plan.Feasibility);
    }
}
