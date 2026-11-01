using System.Reflection;
using Raffa.Savings.Application.Results;
using Raffa.SharedKernel.Savings;

namespace Raffa.Savings.Tests.Results;

/// <summary>The goal parser (prototype parseGoal; BACKEND-SPEC acceptance test 11), "Verso il tuo
/// obiettivo" and the no-estimate rule on every 4.0 results response.</summary>
public sealed class SavingsGoalTests
{
    private static readonly DateOnly Today = new(2026, 9, 8);
    private static readonly Guid Salesforce = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Microsoft = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Aws = Guid.Parse("00000000-0000-0000-0000-000000000003");

    private static readonly IReadOnlyList<ResultsContract> SalesforceOnly =
        [ResultsReportBuilderTests.Contract(Salesforce, "Salesforce", "CRM", 640000, new DateOnly(2026, 10, 18))];

    private static readonly IReadOnlyList<ResultsContract> Three =
    [
        ResultsReportBuilderTests.Contract(Salesforce, "Salesforce", "CRM", 640000, new DateOnly(2026, 10, 18)),
        ResultsReportBuilderTests.Contract(Microsoft, "Microsoft", "Productivity", 1026000, new DateOnly(2026, 12, 1)),
        ResultsReportBuilderTests.Contract(Aws, "AWS", "Cloud infrastructure", 2400000, new DateOnly(2027, 3, 2)),
    ];

    [Fact]
    public void Italian_it_goal_next_year_is_twenty_percent_of_it_contracts_in_the_next_fiscal_year()
    {
        var draft = DeterministicSavingsGoalParser.Parse("voglio risparmiare il 20% sui contratti IT l'anno prossimo", SalesforceOnly, Today)!;

        Assert.True(draft.Ok);
        Assert.Equal(20m, draft.Pct);
        Assert.Equal(128000m, draft.Annual);
        Assert.Equal(DeterministicSavingsGoalParser.ItCategories, draft.Scope.Categories);
        Assert.Null(draft.Scope.Supplier);
        Assert.Equal([Salesforce], draft.Scope.ContractIds);
        Assert.Equal("FY 2027", draft.Period);
        Assert.Equal("IT contracts (software and cloud)", draft.Label.Id);
        Assert.Equal("What I understood", draft.Kicker.Id);
        Assert.Equal("Save {target} on {scope} · {period}.", draft.Title.Id);
        var target = Assert.IsType<ResultsMsg>(draft.Title.Vars!["target"]);
        Assert.Equal("{pct}% — {amount} a year", target.Id);
        Assert.Equal("CHF 128k", target.Vars!["amount"]);
        Assert.Equal(3, draft.Rows.Count);
        Assert.Equal("{spend} of annual spend · 1 validated contract", draft.Rows[0].V.Id);
    }

    [Fact]
    public void Ambiguous_goal_returns_ok_false_and_the_one_question()
    {
        var draft = DeterministicSavingsGoalParser.Parse("voglio risparmiare sui contratti IT", SalesforceOnly, Today)!;

        Assert.False(draft.Ok);
        Assert.Equal("Tell me how much: a percentage (“20%”) or an amount (“CHF 300k”).", draft.Question!.Id);
        Assert.Equal("I need one more thing", draft.Kicker.Id);
    }

    [Fact]
    public void Blank_goal_is_not_parsed() =>
        Assert.Null(DeterministicSavingsGoalParser.Parse("   ", SalesforceOnly, Today));

    [Theory]
    [InlineData("CHF 300k sui contratti software nel 2026", null, 300000, "IT contracts (software and cloud)", "FY 2026")]
    [InlineData("10% sul cloud", 10, 240000, "cloud contracts", "FY 2026")]
    [InlineData("risparmiare 1.5m su tutto questo trimestre", null, 1500000, "all validated contracts", "this quarter")]
    [InlineData("CHF 50'000 con Microsoft", null, 50000, "{supplier}", "FY 2026")]
    public void Examples_parse_like_the_prototype(string text, int? pct, int annual, string label, string period)
    {
        var draft = DeterministicSavingsGoalParser.Parse(text, Three, Today)!;

        Assert.True(draft.Ok);
        if (pct is not null) Assert.Equal(pct, (int?)draft.Pct);
        Assert.Equal(annual, draft.Annual);
        Assert.Equal(label, draft.Label.Id);
        Assert.Equal(period, draft.Period);
    }

    [Fact]
    public void Amount_without_percent_derives_the_percent_of_spend()
    {
        var draft = DeterministicSavingsGoalParser.Parse("CHF 300k sui contratti software nel 2026", Three, Today)!;

        // 300k / (640k + 1,026k + 2,400k) = 7.38% → one decimal.
        Assert.Equal(7.4m, draft.Pct);
    }

    [Fact]
    public void Progress_with_the_open_salesforce_case_matches_the_prototype()
    {
        var draft = DeterministicSavingsGoalParser.Parse("Voglio risparmiare il 20% sui contratti IT l’anno prossimo", SalesforceOnly, Today)!;
        var goal = new SavingsGoalView(Guid.NewGuid(), draft.Text, draft.Pct, draft.Annual, draft.Scope, draft.Period, draft.PeriodLabel, draft.Label, draft.Title, DateTimeOffset.UnixEpoch);
        var caseId = Guid.NewGuid();
        var objectives = new[] { new CaseObjectiveInfo(caseId, Salesforce, "Luca Lamalfa", null, null, null, null, null) };

        var progress = SavingsGoalProgressBuilder.Build(goal, Today, SalesforceOnly, [], objectives);

        Assert.Equal(128000m, progress.Target);
        Assert.Equal(0m, progress.Signed);
        Assert.Equal(0m, progress.Objectives);
        Assert.Equal(128000m, progress.Gap);
        Assert.Equal("gap", progress.Verdict);
        Assert.Equal(128000m, progress.IfAllHit);
        var row = Assert.Single(progress.Rows);
        Assert.Equal(caseId, row.CaseId);
        Assert.Equal(40, row.Days);
        Assert.True(row.Primary);
        Assert.Equal(128000m, row.IfHit);
    }

    [Fact]
    public void Progress_counts_signed_results_in_scope_and_case_targets()
    {
        var goal = new SavingsGoalView(Guid.NewGuid(), "10%", 10, null, new SavingsGoalScope(null, null, []), "FY 2026",
            ResultsMsg.Of("FY {year}"), ResultsMsg.Of("all validated contracts"), ResultsMsg.Of("x"), DateTimeOffset.UnixEpoch);
        var objectives = new[] { new CaseObjectiveInfo(Guid.NewGuid(), Microsoft, null, "p5", null, null, null, null) };

        var progress = SavingsGoalProgressBuilder.Build(goal, Today, Three, ResultsReportBuilderTests.History(), objectives);

        Assert.Equal(406600m, progress.Target);           // 10% of 4,066k
        Assert.Equal(147000m, progress.Signed);           // FY 2026 negotiation results, volume excluded
        Assert.Equal(51300m, progress.Objectives);        // 5% of Microsoft's 1,026k
        Assert.Equal(208300m, progress.Gap);
        Assert.Equal(36.2m, progress.SignedWidth);
    }

    /// <summary>"No endpoint returns an estimate" (acceptance test 9): none of the 4.0 results
    /// responses has an estimate, a low/high range or a confidence.</summary>
    [Theory]
    [InlineData(typeof(ResultsReport))]
    [InlineData(typeof(SavingsGoalDraft))]
    [InlineData(typeof(SavingsGoalView))]
    [InlineData(typeof(SavingsGoalProgress))]
    public void No_results_response_carries_an_estimate(Type response)
    {
        var names = PropertyNames(response, []);

        Assert.NotEmpty(names);
        Assert.DoesNotContain(names, n =>
            n.Contains("estimat", StringComparison.OrdinalIgnoreCase)
            || n.Contains("confidence", StringComparison.OrdinalIgnoreCase)
            || n.Contains("benchmark", StringComparison.OrdinalIgnoreCase)
            || n is "Low" or "High");
    }

    private static List<string> PropertyNames(Type type, HashSet<Type> seen)
    {
        var names = new List<string>();
        if (!seen.Add(type) || type.Namespace?.StartsWith("Raffa", StringComparison.Ordinal) != true) return names;
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            names.Add(p.Name);
            var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            if (t.IsGenericType) t = t.GetGenericArguments().Last();
            names.AddRange(PropertyNames(t, seen));
        }

        return names;
    }
}
