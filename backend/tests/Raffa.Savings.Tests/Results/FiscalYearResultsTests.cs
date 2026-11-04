using Raffa.Savings.Application.Results;
using Raffa.Savings.Application.Spend;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Savings;

namespace Raffa.Savings.Tests.Results;

/// <summary>
/// Spesa e risparmi, phase 3: Risultati and "Verso il tuo obiettivo" on the workspace's fiscal year
/// and on the same periods as the spend; a contract in a currency without a budget rate counts nothing.
/// </summary>
public sealed class FiscalYearResultsTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);
    private static readonly Guid Acme = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void A_fiscal_year_starting_in_April_decides_what_is_in_this_year_and_this_quarter()
    {
        Assert.True(ResultsReportBuilder.InPeriod(new DateOnly(2027, 2, 1), "fy", Today, 4));
        Assert.False(ResultsReportBuilder.InPeriod(new DateOnly(2026, 3, 31), "fy", Today, 4));
        Assert.True(ResultsReportBuilder.InPeriod(new DateOnly(2026, 12, 15), "q", Today, 4)); // fiscal Q3: Oct-Dec
        Assert.False(ResultsReportBuilder.InPeriod(new DateOnly(2027, 1, 5), "q", Today, 4));

        var months = ResultsReportBuilder.Months("fy", Today, 4);
        Assert.Equal(12, months.Count);
        Assert.Equal("Apr 26", months[0].Label);
        Assert.Equal("Jan 27", months[9].Label);
        Assert.False(months[7].Future); // November 2026
        Assert.True(months[8].Future);
    }

    [Fact]
    public void The_report_names_the_fiscal_year_by_the_year_it_starts_in()
    {
        var report = ResultsReportBuilder.Build(new DateOnly(2027, 2, 10), "fy", [], [], [], "CHF", 4);

        Assert.Equal(2026, report.FiscalYear);
    }

    [Fact]
    public void A_contract_without_a_budget_rate_has_no_objective_amount()
    {
        var contract = new ResultsContract(Acme, "Acme", "IT", 0m, new DateOnly(2027, 1, 1), 58, "5%", true, HasRate: false);
        var objective = new CaseObjectiveInfo(Guid.NewGuid(), Acme, "Marta", "p10", null, null, null, null);

        var report = ResultsReportBuilder.Build(Today, "fy", [], [contract], [objective]);

        Assert.Null(Assert.Single(report.OpenObjectives).TargetAmount);
    }

    [Fact]
    public void A_draft_follows_the_fiscal_year_when_it_starts_after_January()
    {
        var today = new DateOnly(2027, 2, 10); // fiscal year 2026/27 (April start)
        var draft = DeterministicSavingsGoalParser.Parse("Voglio risparmiare il 10% l'anno prossimo", [], today)!;

        var fiscal = DeterministicSavingsGoalParser.OnFiscalYear(draft, today, 4);

        Assert.Equal("FY 2028", draft.Period);  // the parser's calendar reading
        Assert.Equal("FY 2027", fiscal.Period); // next fiscal year: April 2027 - March 2028
        Assert.Equal("2027/28", fiscal.PeriodLabel.Vars!["year"]);
        Assert.Same(draft, DeterministicSavingsGoalParser.OnFiscalYear(draft, today, 1));
    }

    [Fact]
    public void Goal_progress_reads_the_goals_fiscal_year_on_the_spends_periods()
    {
        // 120'000 a year, renewing by itself (notice passed): FY 2027 spend is 120'000 exactly
        // (two contract years, 318 + 47 days); a result signed in 2026 saves 12'000 a year from 15 Nov 2026.
        var term = new DocumentedContractTerm(new DateOnly(2025, 11, 15), new DateOnly(2026, 11, 14), 120_000m, null, "CHF", DocumentedTermSource.Contract, DateTimeOffset.UnixEpoch);
        var input = new SpendContractInput(Acme, "Acme", "Acme MSA", "IT", [term], true, 12, 30, "5%", null);
        var spend = SpendCalculator.Build([input], WorkspaceFinanceSettings.Default, Today, 2027, 2027, SpendBasis.Accrual);
        var saving = new SavingInput(Guid.NewGuid(), Acme, "Acme", "Acme MSA", "price", true, 12_000m, "CHF",
            new DateOnly(2026, 11, 15), new DateOnly(2027, 11, 14), new DateOnly(2026, 10, 20), "-10%", "base", "12 months", "m", "e.pdf", []);
        spend = SavingsAllocator.Apply(spend, [saving], [], WorkspaceFinanceSettings.Default);
        var contract = new ResultsContract(Acme, "Acme", "IT", 120_000m, new DateOnly(2027, 10, 15), 345, "5%", true);
        var goal = new SavingsGoalView(Guid.NewGuid(), "5%", 5m, null, new SavingsGoalScope(null, null, []), "FY 2027",
            ResultsMsg.Of("FY {year}"), ResultsMsg.Of("all contracts"), ResultsMsg.Of("t"), DateTimeOffset.UnixEpoch);

        var progress = SavingsGoalProgressBuilder.FromSpend(goal, spend, [0, 1, 2, 3], [contract], []);

        var fySpend = Math.Round(120_000m * 318 / 365, 2) + Math.Round(120_000m * 47 / 366, 2);
        Assert.Equal(fySpend, progress.Spend);
        Assert.Equal(Math.Round(fySpend * 5 / 100, 2), progress.Target);
        Assert.Equal(Math.Round(12_000m * 318 / 365, 2), progress.Signed); // what the result saves in FY 2027
        var row = Assert.Single(progress.Rows);
        Assert.Equal(new DateOnly(2027, 10, 15), row.NoticeDeadline); // 30 days before 14 Nov 2027
        // The result already saves more than 5% of FY 2027's spend: nothing left to cover.
        Assert.Equal(0m, progress.Gap);
        Assert.Equal("covered", progress.Verdict);
    }
}
