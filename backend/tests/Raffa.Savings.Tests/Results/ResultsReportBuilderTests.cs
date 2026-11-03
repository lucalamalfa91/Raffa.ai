using Raffa.Savings.Application.Results;
using Raffa.SharedKernel.Savings;

namespace Raffa.Savings.Tests.Results;

/// <summary>SQ.report() parity (reference/savings-quote-v4.js) and BACKEND-SPEC acceptance test 9.</summary>
public sealed class ResultsReportBuilderTests
{
    private static readonly DateOnly Today = new(2026, 9, 8);

    private static readonly Guid Salesforce = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Microsoft = Guid.Parse("00000000-0000-0000-0000-000000000002");

    /// <summary>SQ.HIST, as the "full" fixture seeds it.</summary>
    internal static IReadOnlyList<ResultsReportItem> History() =>
    [
        Item("h1", "Snowflake", "Data & analytics", "price", 42000, new DateOnly(2026, 3, 14), 17500, new ResultsRealizationProof(new(2026, 4, 1), new(2026, 8, 1), 5, 12)),
        Item("h2", "SAP Ariba", "ERP & finance", "term", 61000, new DateOnly(2026, 1, 22), 35600, new ResultsRealizationProof(new(2026, 2, 1), new(2026, 8, 1), 7, 12)),
        Item("h3", "Workday", "HR", "volume", 28000, new DateOnly(2025, 11, 5), 0, null),
        Item("h4", "Atlassian", "Engineering tools", "volume", 14000, new DateOnly(2026, 5, 19), 0, null),
        Item("h5", "Adobe", "Productivity", "price", 35000, new DateOnly(2026, 8, 28), 0, null),
        Item("h6", "Zoom", "Productivity", "clause", 9000, new DateOnly(2026, 6, 11), 0, null),
    ];

    internal static ResultsReportItem Item(string id, string supplier, string cat, string kind, decimal amount, DateOnly when, decimal realized, ResultsRealizationProof? proof) =>
        new(Guid.NewGuid(), supplier, supplier + " contract", cat, kind, amount, when, id, "baseline", "12 months", "method", supplier + ".pdf", "Marta Keller", realized, proof);

    internal static ResultsContract Contract(Guid id, string supplier, string cat, decimal spend, DateOnly notice) =>
        new(id, supplier, cat, spend, notice, notice.DayNumber - Today.DayNumber, "7%", true);

    [Fact]
    public void Full_history_matches_the_prototype_headline_numbers()
    {
        var report = ResultsReportBuilder.Build(Today, "fy", History(), [], []);

        // h3 (Workday, Nov 2025) is outside FY 2026; volume results are apart.
        Assert.Equal(147000m, report.Headline.Contractual);
        Assert.Equal(53100m, report.Headline.Realized);
        Assert.Equal(14000m, report.Headline.Volume);
        Assert.Equal(4, report.Headline.Count);
        Assert.Equal(1, report.Headline.VolumeCount);
        Assert.Equal(36, report.Kpis.ProvenPercent);
        Assert.Equal(36750m, report.Kpis.AveragePerContract);
        Assert.Equal(["Adobe", "Zoom", "Snowflake", "SAP Ariba"], report.Contractual.Select(r => r.Supplier));
        Assert.Equal(["Atlassian"], report.Volume.Select(r => r.Supplier));
    }

    [Fact]
    public void Months_are_the_fiscal_year_with_the_year_on_january()
    {
        var report = ResultsReportBuilder.Build(Today, "fy", History(), [], []);

        Assert.Equal(12, report.Months.Count);
        Assert.Equal("Jan 26", report.Months[0].Label);
        Assert.Equal("Feb", report.Months[1].Label);
        Assert.Equal(61000m, report.Months[0].Amount);
        Assert.Equal(42000m, report.Months[2].Amount);
        Assert.Equal(0m, report.Months[4].Amount); // Atlassian (volume, May) is not a signed negotiation result
        Assert.Equal(61000m, report.Peak);
        Assert.True(report.Months[9].Future);
        Assert.False(report.Months[8].Future);
    }

    [Fact]
    public void Levers_exclude_quantity_reductions()
    {
        var report = ResultsReportBuilder.Build(Today, "fy", History(), [], []);

        Assert.Equal(["price", "term", "clause"], report.Levers.Select(l => l.Kind));
        Assert.Equal(77000m, report.Levers[0].Amount);
        Assert.Equal(2, report.Levers[0].Count);
        Assert.DoesNotContain(report.Levers, l => l.Kind == "volume");
    }

    [Fact]
    public void A_signed_result_without_invoices_is_not_realized()
    {
        var signed = Item("live", "Salesforce", "CRM", "price", SavingResultRecord.PriceSaving(156m, 148m, 340m * 12m), Today, 0, null);

        var report = ResultsReportBuilder.Build(Today, "fy", [signed], [], []);

        Assert.Equal(32640m, report.Headline.Contractual);
        Assert.Equal(0m, report.Headline.Realized);
        Assert.False(Assert.Single(report.Contractual).Realized);
        Assert.Equal(0, report.Kpis.ProvenPercent);
    }

    [Fact]
    public void A_volume_result_never_counts_toward_negotiation_totals()
    {
        var volume = Item("v", "Workday", "HR", "volume", 28000, Today, 0, null);

        var report = ResultsReportBuilder.Build(Today, "fy", [volume], [], []);

        Assert.Equal(0m, report.Headline.Contractual);
        Assert.Equal(0, report.Kpis.SignedCount);
        Assert.Equal(28000m, report.Headline.Volume);
        Assert.Empty(report.BySupplier);
        Assert.Empty(report.Proof);
    }

    [Fact]
    public void Zero_scenario_after_validation_shows_the_open_case_without_objective()
    {
        var contracts = new[] { Contract(Salesforce, "Salesforce", "CRM", 640000, new DateOnly(2026, 10, 18)) };
        var caseId = Guid.NewGuid();
        var objectives = new[] { new CaseObjectiveInfo(caseId, Salesforce, "Luca Lamalfa", null, null, null, null, null) };

        var report = ResultsReportBuilder.Build(Today, "fy", [], contracts, objectives);

        Assert.Equal(0m, report.Headline.Contractual);
        Assert.Null(report.Kpis.AveragePerContract);
        Assert.Null(report.Kpis.ProvenPercent);
        Assert.Equal(640000m, report.Kpis.AnalysedSpend);
        Assert.Equal(1, report.Kpis.ValidatedCount);
        Assert.Equal(1, report.Kpis.WithoutObjectiveCount);
        Assert.Equal(40, report.Kpis.NextNotice!.Days);
        var open = Assert.Single(report.OpenObjectives);
        Assert.True(open.HasCase);
        Assert.Equal(caseId, open.CaseId);
        Assert.Null(open.TargetAmount);
        Assert.Equal("Salesforce", Assert.Single(report.BySupplier).Label);
    }

    [Fact]
    public void An_open_objective_without_a_notice_date_sorts_after_the_dated_ones()
    {
        // No notice date read: Days falls back to 0, which must not read as "due today" and lead the list.
        var undated = new ResultsContract(Guid.NewGuid(), "GitLab", "DevOps", 101000, null, 0, "7%", true);
        var contracts = new[] { undated, Contract(Salesforce, "Salesforce", "CRM", 640000, new DateOnly(2026, 10, 18)) };

        var report = ResultsReportBuilder.Build(Today, "fy", [], contracts, []);

        Assert.Equal(["Salesforce", "GitLab"], report.OpenObjectives.Select(o => o.Supplier).ToArray());
        Assert.Equal("Salesforce", report.Kpis.NextNotice!.Supplier);
    }

    [Fact]
    public void Case_objectives_are_user_targets_on_contract_spend_and_closed_cases_drop_out()
    {
        var contracts = new[]
        {
            Contract(Salesforce, "Salesforce", "CRM", 640000, new DateOnly(2026, 10, 18)),
            Contract(Microsoft, "Microsoft", "Productivity", 1026000, new DateOnly(2026, 12, 1)),
        };
        var objectives = new[]
        {
            new CaseObjectiveInfo(Guid.NewGuid(), Salesforce, null, "p10", null, null, null, null),
            new CaseObjectiveInfo(Guid.NewGuid(), Microsoft, null, "p5", null, null, null, "renewed"),
        };

        var report = ResultsReportBuilder.Build(Today, "fy", [], contracts, objectives);

        var open = Assert.Single(report.OpenObjectives);
        Assert.Equal(64000m, open.TargetAmount);
        Assert.Equal(64000m, report.Kpis.OpenObjectives);
        Assert.Equal("Salesforce", report.Kpis.NextNotice!.Supplier);
    }

    [Fact]
    public void In_period_matches_sq()
    {
        Assert.True(ResultsReportBuilder.InPeriod(new DateOnly(2026, 1, 1), "fy", Today));
        Assert.False(ResultsReportBuilder.InPeriod(new DateOnly(2025, 12, 31), "fy", Today));
        Assert.True(ResultsReportBuilder.InPeriod(new DateOnly(2026, 7, 1), "q", Today));
        Assert.False(ResultsReportBuilder.InPeriod(new DateOnly(2026, 6, 30), "q", Today));
        Assert.True(ResultsReportBuilder.InPeriod(new DateOnly(2025, 11, 5), "12m", Today));
        Assert.Equal("Oct 25", ResultsReportBuilder.Months("12m", Today)[0].Label);
    }

    [Theory]
    [InlineData(0, "CHF 0k")]
    [InlineData(128000, "CHF 128k")]
    [InlineData(17500, "CHF 18k")]
    [InlineData(2400000, "CHF 2.4M")]
    public void K_formats_like_sq(decimal n, string expected) => Assert.Equal(expected, ResultsFormat.K(n));
}
