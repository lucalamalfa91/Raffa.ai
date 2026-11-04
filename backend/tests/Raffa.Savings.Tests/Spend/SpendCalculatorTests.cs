using Raffa.Savings.Application.Spend;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Savings;

namespace Raffa.Savings.Tests.Spend;

/// <summary>
/// Spesa e risparmi: <see cref="SpendCalculator"/> puts each contract's value in the fiscal quarters
/// it covers, from documented terms and the renewal clause only, and lists what it cannot value
/// exactly instead of estimating it. Every expected figure is worked out by hand from the day counts.
/// </summary>
public sealed class SpendCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);
    private static readonly WorkspaceFinanceSettings Chf = WorkspaceFinanceSettings.Default;

    private static DocumentedContractTerm Term(
        string from, string to, decimal? annual, decimal? total = null, string currency = "CHF",
        string source = DocumentedTermSource.Contract, string recorded = "2025-01-01") =>
        new(DateOnly.Parse(from), DateOnly.Parse(to), annual, total, currency, source, DateTimeOffset.Parse(recorded + "T00:00:00Z"));

    private static SpendContractInput Contract(
        IReadOnlyList<DocumentedContractTerm> terms, bool auto = false, int? months = null, int gap = 0,
        string? billing = null, string supplier = "Acme") =>
        new(Guid.NewGuid(), supplier, supplier + " MSA", "IT", terms, auto, months, gap, "5%", billing);

    private static SpendReport Build(int from, int to, string basis = SpendBasis.Accrual, WorkspaceFinanceSettings? settings = null, params SpendContractInput[] contracts) =>
        SpendCalculator.Build(contracts, settings ?? Chf, Today, from, to, basis);

    private static int Q(SpendReport r, int fy, int q) => r.Periods.ToList().FindIndex(p => p.FiscalYear == fy && p.Quarter == q);

    private static decimal Total(SpendContractRow row) =>
        row.Actual.Sum() + row.Committed.Sum() + row.AutoRenewal.Sum() + row.ToDecide.Sum();

    [Fact]
    public void An_annual_value_spreads_over_the_days_of_its_contract_year()
    {
        var r = Build(2025, 2025, contracts: Contract([Term("2025-01-01", "2025-12-31", 365_000m)]));
        var row = Assert.Single(r.Contracts);

        Assert.Equal([90_000m, 91_000m, 92_000m, 92_000m], row.Actual);
        Assert.Equal(365_000m, Total(row));
    }

    [Fact]
    public void A_leap_contract_year_still_sums_to_its_annual_value()
    {
        // 16 Apr 2027 – 15 Apr 2028 holds 29 Feb 2028: 366 days, 1'000 a day.
        var r = Build(2027, 2028, contracts: Contract([Term("2027-04-16", "2028-04-15", 366_000m)]));
        var row = Assert.Single(r.Contracts);

        Assert.Equal(76_000m, row.Committed[Q(r, 2027, 2)]); // 16 Apr – 30 Jun
        Assert.Equal(366_000m, row.Committed.Sum());        // the documented year, exactly
        Assert.True(row.ToDecide.Sum() > 0);                // after 15 Apr 2028: no automatic renewal
    }

    [Fact]
    public void Days_up_to_today_are_actual_and_the_rest_is_committed()
    {
        var r = Build(2026, 2026, contracts: Contract([Term("2026-01-01", "2026-12-31", 365_000m)]));
        var row = Assert.Single(r.Contracts);
        var q4 = Q(r, 2026, 4);

        Assert.Equal(35_000m, row.Actual[q4]);    // 1 Oct – 4 Nov
        Assert.Equal(57_000m, row.Committed[q4]); // 5 Nov – 31 Dec
    }

    [Fact]
    public void A_renewal_whose_notice_passed_is_binding_and_the_next_one_still_depends_on_notice()
    {
        // Notice 90 days before 14 Nov 2026 = 16 Aug 2026, passed: the 2026-27 term is binding.
        var c = Contract([Term("2025-11-15", "2026-11-14", 120_000m)], auto: true, months: 12, gap: 90);
        var r = Build(2027, 2027, contracts: c);
        var row = Assert.Single(r.Contracts);

        // 2027: 1 Jan – 14 Nov of the 365-day year from 15 Nov 2026 ...
        Assert.Equal(Math.Round(120_000m * 318 / 365, 2), row.Committed.Sum());
        // ... then 15 Nov – 31 Dec of the 366-day year from 15 Nov 2027, renewing unless notice is given.
        Assert.Equal(Math.Round(120_000m * 47 / 366, 2), row.AutoRenewal.Sum());
        Assert.Contains(row.Segments, s => s.Kind == SpendSegmentKind.RolledOver && s.From == new DateOnly(2026, 11, 15) && s.PreviousTermPrice);
        Assert.Contains(SpendNote.PreviousTermPrice, row.Notes);
        Assert.Equal(new DateOnly(2027, 8, 16), row.Decision.DecideBy);
        Assert.Equal(new DateOnly(2027, 11, 14), row.Decision.EndsOn);
    }

    [Fact]
    public void Without_automatic_renewal_the_spend_after_the_end_is_to_decide_at_the_current_price()
    {
        var c = Contract([Term("2026-04-01", "2027-03-31", 365_000m)], gap: 30);
        var r = Build(2027, 2027, contracts: c);
        var row = Assert.Single(r.Contracts);

        Assert.Equal(90_000m, row.Committed.Sum()); // 1 Jan – 31 Mar 2027
        // 1 Apr – 31 Dec 2027 of the 366-day year from 1 Apr 2027, nothing committed.
        Assert.Equal(Math.Round(365_000m * 275 / 366, 2), row.ToDecide.Sum());
        Assert.Equal(0m, row.AutoRenewal.Sum());
        Assert.Equal(new DateOnly(2027, 3, 1), row.Decision.DecideBy); // 30 days before the end
        Assert.Equal(new DateOnly(2027, 3, 31), row.Decision.EndsOn);
    }

    [Fact]
    public void After_its_notice_passed_a_contract_without_renewal_has_no_decision_left_only_an_end()
    {
        var c = Contract([Term("2026-01-01", "2026-11-30", 334_000m)], gap: 60);
        var row = Assert.Single(Build(2026, 2027, contracts: c).Contracts);

        Assert.Null(row.Decision.DecideBy);
        Assert.Equal(new DateOnly(2026, 11, 30), row.Decision.EndsOn);
    }

    [Fact]
    public void An_expired_contract_counts_nothing_after_its_end()
    {
        var r = Build(2024, 2025, contracts: Contract([Term("2024-01-01", "2024-12-31", 366_000m)]));
        var row = Assert.Single(r.Contracts);

        Assert.Equal(366_000m, row.Actual.Take(4).Sum());
        Assert.Equal(0m, Total(row) - row.Actual.Take(4).Sum());
        Assert.Contains(SpendNote.Expired, row.Notes);
    }

    [Fact]
    public void The_term_recorded_later_covers_the_days_two_terms_share()
    {
        var c = Contract([
            Term("2025-01-01", "2026-12-31", 100_000m, recorded: "2025-01-01"),
            Term("2026-07-01", "2027-06-30", 200_000m, source: DocumentedTermSource.Renewal, recorded: "2026-06-01"),
        ]);
        var r = Build(2026, 2026, contracts: c);
        var row = Assert.Single(r.Contracts);

        Assert.Equal(Math.Round(100_000m * 91 / 365, 2), row.Actual[Q(r, 2026, 2)]); // the contract
        Assert.Equal(Math.Round(200_000m * 92 / 365, 2), row.Actual[Q(r, 2026, 3)]); // the renewal from 1 Jul
    }

    [Fact]
    public void A_day_no_document_covers_counts_nothing()
    {
        var c = Contract([
            Term("2024-01-01", "2024-12-31", 366_000m),
            Term("2026-01-01", "2026-12-31", 365_000m, source: DocumentedTermSource.Renewal, recorded: "2026-01-01"),
        ]);
        var r = Build(2025, 2025, contracts: c);

        Assert.Equal(0m, Total(Assert.Single(r.Contracts)));
        Assert.Equal(0, r.FiscalYears.Single().Documented);
    }

    [Fact]
    public void A_total_value_is_spread_over_the_days_of_its_term()
    {
        var r = Build(2026, 2026, contracts: Contract([Term("2026-01-01", "2026-12-31", null, total: 730_000m)]));

        Assert.Equal(180_000m, Assert.Single(r.Contracts).Actual[0]); // 90 days × 2'000
    }

    [Fact]
    public void A_currency_without_a_budget_rate_is_left_out_and_listed()
    {
        var r = Build(2026, 2026, contracts: Contract([Term("2026-01-01", "2026-12-31", 365_000m, currency: "EUR")]));

        Assert.Empty(r.Contracts);
        var x = Assert.Single(r.Excluded);
        Assert.Equal(SpendExclusionReason.NoRate, x.Reason);
        Assert.Equal("EUR", x.Currency);
    }

    [Fact]
    public void A_currency_with_a_budget_rate_is_converted_at_that_rate()
    {
        var settings = Chf with { BudgetRates = new Dictionary<string, decimal> { ["EUR"] = 0.95m } };
        var r = Build(2026, 2026, settings: settings, contracts: Contract([Term("2026-01-01", "2026-12-31", 365_000m, currency: "EUR")]));
        var row = Assert.Single(r.Contracts);

        Assert.Equal("EUR", row.Currency);
        Assert.Equal(0.95m, row.Rate);
        Assert.Equal(85_500m, row.Actual[0]); // 90'000 EUR
        Assert.Equal(346_750m, Total(row));
    }

    [Fact]
    public void Cash_puts_each_billing_period_on_the_day_it_starts()
    {
        var c = Contract([Term("2026-01-01", "2026-12-31", 365_000m)], billing: BillingPeriods.Quarterly);
        var r = Build(2026, 2026, SpendBasis.Cash, contracts: c);
        var row = Assert.Single(r.Contracts);

        Assert.Equal([90_000m, 91_000m, 92_000m, 92_000m], row.Actual); // every invoice day is past
    }

    [Fact]
    public void Cash_after_today_is_committed_and_an_annual_invoice_falls_in_one_quarter()
    {
        var c = Contract([Term("2027-02-01", "2028-01-31", 365_000m)], billing: BillingPeriods.Annual);
        var r = Build(2027, 2027, SpendBasis.Cash, contracts: c);
        var row = Assert.Single(r.Contracts);

        Assert.Equal([365_000m, 0m, 0m, 0m], row.Committed);
    }

    [Fact]
    public void Cash_without_a_billing_period_is_left_out_and_listed()
    {
        var r = Build(2026, 2026, SpendBasis.Cash, contracts: Contract([Term("2026-01-01", "2026-12-31", 365_000m)]));

        Assert.Equal(SpendExclusionReason.NoBillingPeriod, Assert.Single(r.Excluded).Reason);
    }

    [Fact]
    public void Missing_dates_or_value_are_listed_never_estimated()
    {
        var r = Build(2026, 2026, contracts: [
            Contract([], supplier: "NoDates"),
            Contract([Term("2026-01-01", "2026-12-31", null)], supplier: "NoValue"),
        ]);

        Assert.Empty(r.Contracts);
        Assert.Equal(
            [("NoDates", SpendExclusionReason.NoDates), ("NoValue", SpendExclusionReason.NoValue)],
            r.Excluded.Select(x => (x.Supplier, x.Reason)));
    }

    [Fact]
    public void A_fiscal_year_starting_in_April_is_named_by_the_year_it_starts_in()
    {
        var quarters = SpendCalculator.Quarters(2026, 2026, 4);

        Assert.Equal(new DateOnly(2026, 4, 1), quarters[0].From);
        Assert.Equal(new DateOnly(2027, 3, 31), quarters[3].To);
        Assert.Equal("Q1 2026/27", quarters[0].Label);
        Assert.Equal("FY 2026/27", SpendCalculator.FiscalYearLabel(2026, 4));
        Assert.Equal("FY 2026", SpendCalculator.FiscalYearLabel(2026, 1));
        Assert.Equal(2025, SpendCalculator.FiscalYearOf(new DateOnly(2026, 3, 31), 4));
        Assert.Equal(2026, SpendCalculator.FiscalYearOf(new DateOnly(2026, 4, 1), 4));
    }

    [Fact]
    public void Coverage_counts_only_documented_terms()
    {
        var c = Contract([Term("2026-01-01", "2026-12-31", 365_000m)], auto: true, months: 12, gap: 30);
        var r = Build(2026, 2027, contracts: c);

        Assert.Equal([(2026, 1, 1), (2027, 0, 1)], r.FiscalYears.Select(f => (f.FiscalYear, f.Documented, f.Contracts)));
    }

    [Fact]
    public void The_report_span_is_bounded()
    {
        Assert.Throws<ArgumentException>(() => Build(2027, 2026));
        Assert.Throws<ArgumentException>(() => Build(2020, 2030));
        Assert.Throws<ArgumentException>(() => Build(2026, 2026, basis: "estimate"));
    }

    [Theory]
    [InlineData("Annually", BillingPeriods.Annual)]
    [InlineData("per year", BillingPeriods.Annual)]
    [InlineData("every 12 months", BillingPeriods.Annual)]
    [InlineData("Semi-annual", BillingPeriods.SemiAnnual)]
    [InlineData("quarterly in advance", BillingPeriods.Quarterly)]
    [InlineData("ogni 3 mesi", BillingPeriods.Quarterly)]
    [InlineData("Monthly", BillingPeriods.Monthly)]
    [InlineData("one-off", null)]
    [InlineData("biennial", null)]
    [InlineData("", null)]
    public void A_billing_period_is_read_literally(string text, string? expected)
    {
        Assert.Equal(expected, BillingPeriods.Normalize(text));
    }

    [Fact]
    public void A_contract_has_a_billing_period_only_when_its_lines_agree()
    {
        Assert.Equal(BillingPeriods.Annual, BillingPeriods.Common(["Annual", "annually", null]));
        Assert.Null(BillingPeriods.Common(["Annual", "Monthly"]));
        Assert.Null(BillingPeriods.Common([null, ""]));
    }
}
