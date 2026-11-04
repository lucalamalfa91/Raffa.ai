using Raffa.Savings.Application.Spend;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Savings;

namespace Raffa.Savings.Tests.Spend;

/// <summary>
/// Spesa e risparmi, the savings layer: <see cref="SavingsAllocator"/> puts recorded results, their
/// invoice proof and the cases' objectives in the same fiscal quarters as the spend, in the
/// reporting currency, and lists what it cannot put in time or convert.
/// </summary>
public sealed class SavingsAllocatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);
    private static readonly Guid Acme = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static SpendReport Spend(int from, int to, WorkspaceFinanceSettings? settings = null, string currency = "CHF")
    {
        var term = new DocumentedContractTerm(
            new DateOnly(2025, 11, 15), new DateOnly(2026, 11, 14), 120_000m, null, currency, DocumentedTermSource.Contract, DateTimeOffset.UnixEpoch);
        var c = new SpendContractInput(Acme, "Acme", "Acme MSA", "IT", [term], AutoRenew: true, RenewalTermMonths: 12, NoticeGapDays: 30, "5%", null);
        return SpendCalculator.Build([c], settings ?? WorkspaceFinanceSettings.Default, Today, from, to, SpendBasis.Accrual);
    }

    private static SavingInput Saving(
        decimal annual, string from, string to, string kind = "price", string currency = "CHF", Guid? contract = null,
        params (string Month, decimal Amount)[] proofs) =>
        new(Guid.NewGuid(), contract ?? Acme, "Acme", "Acme MSA", kind, kind != "volume", annual, currency,
            from == "" ? null : DateOnly.Parse(from), to == "" ? null : DateOnly.Parse(to), new DateOnly(2025, 11, 1),
            "note", "base", "period", "method", "evidence.pdf",
            proofs.Select(p => new SavingRealizationInput(DateOnly.Parse(p.Month), p.Amount)).ToList());

    private static CaseObjectiveInfo Objective(string? kind, decimal? pct = null, decimal? amount = null, string? outcome = null) =>
        new(Guid.NewGuid(), Acme, "Marta", kind, pct, amount, null, outcome);

    [Fact]
    public void A_signed_result_counts_over_its_period_and_invoices_in_their_month()
    {
        var report = SavingsAllocator.Apply(
            Spend(2026, 2026),
            [Saving(36_500m, "2026-01-01", "2026-12-31", proofs: [("2026-02-01", 9_000m), ("2026-05-01", 9_100m)])],
            [],
            WorkspaceFinanceSettings.Default);
        var row = Assert.Single(report.Contracts);

        Assert.Equal([9_000m, 9_100m, 9_200m, 9_200m], row.SavingsContractual); // 100 a day
        Assert.Equal([9_000m, 9_100m, 0m, 0m], row.SavingsRealized);
        var entry = Assert.Single(report.Savings);
        Assert.Equal(new DateOnly(2026, 5, 31), entry.ProvenTo);
        Assert.Equal(18_100m, entry.RealizedReporting);
        Assert.Null(entry.Excluded);
    }

    [Fact]
    public void A_quantity_reduction_is_kept_apart_and_never_counts_as_realized()
    {
        var report = SavingsAllocator.Apply(
            Spend(2026, 2026),
            [Saving(36_500m, "2026-01-01", "2026-12-31", kind: "volume", proofs: [("2026-02-01", 9_000m)])],
            [],
            WorkspaceFinanceSettings.Default);
        var row = Assert.Single(report.Contracts);

        Assert.Equal(36_500m, row.QuantityReduction.Sum());
        Assert.Equal(0m, row.SavingsContractual.Sum());
        Assert.Equal(0m, row.SavingsRealized.Sum());
        Assert.False(Assert.Single(report.Savings).CountsAsNegotiation);
    }

    [Fact]
    public void A_result_without_a_period_or_a_rate_is_listed_not_put_in_time()
    {
        var report = SavingsAllocator.Apply(
            Spend(2026, 2026),
            [Saving(10_000m, "", ""), Saving(10_000m, "2026-01-01", "2026-12-31", currency: "EUR")],
            [],
            WorkspaceFinanceSettings.Default);

        Assert.Equal(0m, Assert.Single(report.Contracts).SavingsContractual.Sum());
        Assert.Equal(
            [SavingExclusionReason.NoPeriod, SavingExclusionReason.NoRate],
            report.Savings.Select(s => s.Excluded).Order(StringComparer.Ordinal));
        Assert.Null(report.Savings.Single(s => s.Currency == "EUR").AmountAnnualReporting);
    }

    [Fact]
    public void A_result_in_another_currency_converts_at_the_budget_rate()
    {
        var settings = WorkspaceFinanceSettings.Default with { BudgetRates = new Dictionary<string, decimal> { ["EUR"] = 0.9m } };
        var report = SavingsAllocator.Apply(Spend(2026, 2026), [Saving(36_500m, "2026-01-01", "2026-12-31", currency: "EUR")], [], settings);

        Assert.Equal(32_850m, Assert.Single(report.Contracts).SavingsContractual.Sum());
        Assert.Equal(32_850m, Assert.Single(report.Savings).AmountAnnualReporting);
    }

    [Fact]
    public void A_result_on_a_contract_that_is_not_validated_counts_apart()
    {
        var report = SavingsAllocator.Apply(Spend(2026, 2026), [Saving(36_500m, "2026-01-01", "2026-12-31", contract: Guid.NewGuid())], [], WorkspaceFinanceSettings.Default);

        Assert.Equal(0m, Assert.Single(report.Contracts).SavingsContractual.Sum());
        Assert.Equal(36_500m, report.OtherSavings!.SavingsContractual.Sum());
    }

    [Fact]
    public void An_open_case_objective_counts_over_the_term_it_negotiates()
    {
        // The current term ends 14 Nov 2026 (notice passed: the 2026-27 term is binding, ending 14 Nov 2027);
        // p10 of 120'000 = 12'000 a year, over the next term from 15 Nov 2027.
        var report = SavingsAllocator.Apply(Spend(2027, 2028), [], [Objective("p10")], WorkspaceFinanceSettings.Default);
        var row = Assert.Single(report.Contracts);
        var entry = Assert.Single(report.Objectives);

        Assert.Equal(12_000m, entry.AnnualTarget);
        Assert.Equal(new DateOnly(2027, 11, 15), entry.From);
        Assert.False(entry.AssumedTermMonths);
        Assert.Equal(Math.Round(12_000m * 47 / 366, 2), row.Objective.Take(4).Sum()); // 15 Nov – 31 Dec 2027
        Assert.Equal(12_000m, row.Objective.Sum());
    }

    [Fact]
    public void A_custom_amount_objective_is_converted_from_the_contracts_currency()
    {
        var settings = WorkspaceFinanceSettings.Default with { BudgetRates = new Dictionary<string, decimal> { ["EUR"] = 0.9m } };
        var report = SavingsAllocator.Apply(Spend(2027, 2028, settings, "EUR"), [], [Objective("custom", amount: 10_000m)], settings);

        Assert.Equal(9_000m, Assert.Single(report.Objectives).AnnualTarget);
    }

    [Fact]
    public void A_closed_case_has_no_objective_and_a_case_without_a_target_is_listed_without_one()
    {
        var closed = SavingsAllocator.Apply(Spend(2027, 2028), [], [Objective("p10", outcome: "renewed")], WorkspaceFinanceSettings.Default);
        Assert.Empty(closed.Objectives);

        var none = SavingsAllocator.Apply(Spend(2027, 2028), [], [Objective("none")], WorkspaceFinanceSettings.Default);
        var entry = Assert.Single(none.Objectives);
        Assert.Null(entry.AnnualTarget);
        Assert.Null(entry.Excluded);
        Assert.Equal(0m, Assert.Single(none.Contracts).Objective.Sum());
    }
}
