using Raffa.Chat.Application.Council;
using Raffa.Chat.Application.Guards;
using Raffa.Chat.Application.Pack;
using Raffa.Chat.Application.Planning;
using Raffa.Chat.Tests.TestSupport;

namespace Raffa.Chat.Tests.Council;

/// <summary>
/// Plan F2-D03 / F2-T08: the verdict on the goal has one owner, a deterministic component that reads
/// the calculators' pack items, so verdict and feasibility agree in 100% of the cases; nothing is
/// "reachable" without a goal; every verdict is an upper bound with its range, in the question's
/// language, and every figure in it is grounded.
/// </summary>
public sealed class CouncilVerdictTests
{
    private const string Eur = "EUR";

    private static PackItem Item(string key, string title, string? subtitle, string snippet, params PackValue[] values) =>
        new(key, PackCorpus.Calc, title, subtitle, null, null, snippet, null, null, null, "deterministic calculator", values);

    private static PackValue Amount(string key, decimal value) =>
        new(key, value.ToString("0", System.Globalization.CultureInfo.InvariantCulture), PackValueKind.Amount, Eur);

    private static PackItem TargetItem(string? label, decimal? target, decimal low, decimal high, decimal? spend = 230000m)
    {
        var values = new List<PackValue>();
        if (target is { } t)
        {
            values.Add(Amount("targetAmount", t));
        }

        if (spend is { } s)
        {
            values.Add(Amount("annualSpend", s));
        }

        values.Add(Amount("coverageLow", low));
        values.Add(Amount("coverageHigh", high));
        return Item("calc:savings-target", "ServiceNow — saving target and lever coverage", label,
            "Explanation: say so and name the gap.", [.. values]);
    }

    private static PackItem Lever(string name, decimal low, decimal high) =>
        Item($"calc:lever[{name}]", $"ServiceNow — {name}", null, $"{name} lever.", Amount("estimatedLow", low), Amount("estimatedHigh", high));

    /// <summary>The calculators' rule (SavingsLeverCalculator.Compute), restated independently.</summary>
    private static CouncilVerdictKind CalculatorFeasibility(decimal high, decimal target) =>
        high >= target ? CouncilVerdictKind.Reachable
        : high >= target * 0.6m ? CouncilVerdictKind.Stretch
        : CouncilVerdictKind.NotSupported;

    private static string Label(CouncilVerdictKind kind) => kind switch
    {
        CouncilVerdictKind.Reachable => CouncilVerdict.ReachableLabel,
        CouncilVerdictKind.Stretch => CouncilVerdict.StretchLabel,
        _ => CouncilVerdict.NotSupportedLabel,
    };

    private static SavingsGoal Goal(decimal amount) => new(amount, null, Eur, null, null);

    public static TheoryData<decimal, decimal> TargetsAndCoverages()
    {
        var data = new TheoryData<decimal, decimal>();
        foreach (var target in new[] { 5000m, 20000m, 100000m })
        {
            foreach (var factor in new[] { 0m, 0.3m, 0.59m, 0.6m, 0.61m, 0.99m, 1m, 1.01m, 3m })
            {
                data.Add(target, Math.Round(target * factor, 0));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(TargetsAndCoverages))]
    public void The_verdict_matches_the_calculators_feasibility_whether_or_not_the_item_carries_the_label(decimal target, decimal high)
    {
        var expected = CalculatorFeasibility(high, target);
        var low = Math.Round(high / 2, 0);

        var labelled = CouncilVerdict.Evaluate([TargetItem(Label(expected), target, low, high)], Goal(target));
        var unlabelled = CouncilVerdict.Evaluate([TargetItem(null, target, low, high)], Goal(target));
        var fromLevers = CouncilVerdict.Evaluate([TargetItem(null, target, 0, 0) with { Values = [Amount("targetAmount", target)] }, Lever("a", low, high)], Goal(target));

        Assert.Equal(expected, labelled!.Kind);
        Assert.Equal(expected, unlabelled!.Kind);
        Assert.Equal(expected, fromLevers!.Kind);
    }

    [Fact]
    public void The_calculators_label_wins_over_a_conflicting_recomputation()
    {
        // The calculator said "stretch" (e.g. a window or rounding rule this component cannot see).
        var evaluation = CouncilVerdict.Evaluate([TargetItem(CouncilVerdict.StretchLabel, 20000m, 5000m, 25000m)], Goal(20000m));

        Assert.Equal(CouncilVerdictKind.Stretch, evaluation!.Kind);
    }

    [Fact]
    public void The_labels_are_the_ones_the_calculators_write()
    {
        Assert.Equal("target reachable", CouncilVerdict.ReachableLabel);
        Assert.Equal("target is a stretch", CouncilVerdict.StretchLabel);
        Assert.Equal("target not supported by the evidence", CouncilVerdict.NotSupportedLabel);
        Assert.Equal("no target named", CouncilVerdict.NoTargetLabel);
    }

    [Theory]
    [InlineData("How can I save more with ServiceNow?")]
    [InlineData("quali leve per risparmiare su ServiceNow?")]
    public void Without_a_goal_nothing_is_reachable_whatever_the_coverage(string question)
    {
        // Even a label that says "target reachable" (a stale or portfolio item) cannot make a goalless turn "reachable".
        var pack = new[] { TargetItem(CouncilVerdict.ReachableLabel, null, 1000m, 100000m) };

        var item = CouncilVerdict.BuildItem(pack, goal: null, question)!;
        var noTargetNamed = CouncilVerdict.BuildItem(pack, new SavingsGoal(null, null, null, null, null), question)!;

        foreach (var verdict in new[] { item, noTargetNamed })
        {
            Assert.DoesNotContain("reachable", verdict.Title, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("raggiungibile", verdict.Title, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("raggiungibile", verdict.Snippet, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("targetAmount", verdict.Values.Select(v => v.Key));
        }
    }

    [Theory]
    [InlineData(11500, CouncilVerdictKind.MeaningfulSaving)] // exactly the 5% threshold of EUR 230000
    [InlineData(11499, CouncilVerdictKind.LimitedSaving)]
    [InlineData(80000, CouncilVerdictKind.MeaningfulSaving)]
    [InlineData(0, CouncilVerdictKind.LimitedSaving)]
    public void Without_a_goal_the_threshold_is_five_percent_of_the_annual_spend(int high, CouncilVerdictKind expected)
    {
        var evaluation = CouncilVerdict.Evaluate([TargetItem(CouncilVerdict.NoTargetLabel, null, 0, high)], goal: null);

        Assert.Equal(expected, evaluation!.Kind);
    }

    [Fact]
    public void Without_a_goal_and_without_spend_only_the_range_is_stated()
    {
        var evaluation = CouncilVerdict.Evaluate([TargetItem(CouncilVerdict.NoTargetLabel, null, 100m, 900m, spend: null)], goal: null);

        Assert.Equal(CouncilVerdictKind.CoverageOnly, evaluation!.Kind);
    }

    [Fact]
    public void A_pack_with_no_lever_coverage_has_no_verdict()
    {
        Assert.Null(CouncilVerdict.BuildItem([Item("fact:x", "Fact", null, "Nothing here.")], Goal(20000m), "q"));
        Assert.Null(CouncilVerdict.BuildItem([], null, "q"));
    }

    [Fact]
    public void The_goal_amount_is_taken_from_the_goal_when_the_item_has_none_and_from_a_percentage_of_the_spend()
    {
        var byAmount = CouncilVerdict.Evaluate([TargetItem(null, null, 0, 30000m)], Goal(20000m))!;
        var byPercent = CouncilVerdict.Evaluate(
            [TargetItem(null, null, 0, 10000m)],
            new SavingsGoal(null, 10m, Eur, null, null))!; // 10% of 230000 = 23000 > 10000 * ... stretch? 10000 < 13800 -> not supported

        Assert.Equal(20000m, byAmount.Target);
        Assert.Equal(CouncilVerdictKind.Reachable, byAmount.Kind);
        Assert.Equal(23000m, byPercent.Target);
        Assert.Equal(CouncilVerdictKind.NotSupported, byPercent.Kind);
    }

    [Fact]
    public void Every_verdict_is_an_upper_bound_with_its_range_in_the_question_language()
    {
        var pack = new[] { TargetItem(CouncilVerdict.ReachableLabel, 20000m, 8000m, 25000m), Lever("Market discount", 5000m, 20700m) };

        var en = CouncilVerdict.BuildItem(pack, Goal(20000m), "which levers can save 20k on ServiceNow?")!;
        var it = CouncilVerdict.BuildItem(pack, Goal(20000m), "quali leve per risparmiare 20k su ServiceNow?")!;

        Assert.Equal(CouncilVerdict.CitationKey, en.CitationKey);
        Assert.Equal(PackCorpus.Calc, en.Corpus);
        Assert.Contains("upper bound", en.Title, StringComparison.Ordinal);
        Assert.Contains("between EUR 8000 and EUR 25000", en.Snippet, StringComparison.Ordinal);
        Assert.Contains("EUR 20000 target", en.Snippet, StringComparison.Ordinal);
        Assert.Contains("The biggest lever: ServiceNow — Market discount.", en.Snippet, StringComparison.Ordinal);
        Assert.Contains(en.Values, v => v.Key == "coverageLow" && v.Value == "8000");
        Assert.Contains(en.Values, v => v.Key == "coverageHigh" && v.Value == "25000");
        Assert.Contains(en.Values, v => v.Key == "targetAmount" && v.Value == "20000");

        Assert.Contains("limite superiore", it.Title, StringComparison.Ordinal);
        Assert.Contains("tra EUR 8000 e EUR 25000", it.Snippet, StringComparison.Ordinal);
        Assert.Contains("La leva più grande: ServiceNow — Market discount.", it.Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reason_is_built_from_numbers_and_the_biggest_lever_never_from_the_calculators_instruction()
    {
        var pack = new[] { TargetItem(CouncilVerdict.NotSupportedLabel, 40000m, 1000m, 5000m), Lever("Small", 100m, 900m), Lever("Large", 900m, 4100m) };

        var verdict = CouncilVerdict.BuildItem(pack, Goal(40000m), "How do I save 40k with ServiceNow?")!;

        Assert.DoesNotContain("say so", verdict.Snippet, StringComparison.Ordinal);
        Assert.Contains("ServiceNow — Large", verdict.Snippet, StringComparison.Ordinal);
        Assert.DoesNotContain("ServiceNow — Small", verdict.Snippet, StringComparison.Ordinal);
        Assert.Contains("not supported", verdict.Title, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> EveryKindInBothLanguages()
    {
        var data = new TheoryData<string, string>();
        foreach (var label in new[] { CouncilVerdict.ReachableLabel, CouncilVerdict.StretchLabel, CouncilVerdict.NotSupportedLabel, CouncilVerdict.NoTargetLabel })
        {
            data.Add(label, "Where can we save on ServiceNow?");
            data.Add(label, "Dove possiamo risparmiare su ServiceNow?");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryKindInBothLanguages))]
    public void Every_figure_the_verdict_states_is_grounded(string label, string question)
    {
        var withGoal = label != CouncilVerdict.NoTargetLabel;
        var pack = new List<PackItem>
        {
            TargetItem(label, withGoal ? 20000m : null, 8000m, 15000m),
            Lever("Market discount", 5000m, 15000m),
        };

        var verdict = CouncilVerdict.BuildItem(pack, withGoal ? Goal(20000m) : null, question)!;

        PackItem[] groundedBy = [.. pack, verdict with { Snippet = string.Empty }];
        Assert.True(NumericGuard.Validate(verdict.Snippet, groundedBy).Passed);
        Assert.True(NumericGuard.Validate(verdict.Title, groundedBy).Passed);
    }

    [Fact]
    public void A_lever_title_carrying_an_ungrounded_number_is_left_out_of_the_text()
    {
        var pack = new[]
        {
            TargetItem(CouncilVerdict.ReachableLabel, 20000m, 8000m, 25000m),
            Item("calc:lever[odd]", "Odd lever worth 99999%", null, "x", Amount("estimatedHigh", 25000m)),
        };

        var verdict = CouncilVerdict.BuildItem(pack, Goal(20000m), "q?")!;

        Assert.DoesNotContain("99999", verdict.Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void On_the_savings_golden_pack_the_verdict_agrees_with_the_portfolio_target()
    {
        var pack = ScreenshotSavingsPack.Build().Where(i => i.CitationKey != CouncilVerdict.CitationKey).ToList();
        var portfolioTarget = pack.Single(i => i.CitationKey == "calc:portfolio-target");
        var goal = new SavingsGoal(null, null, Eur, 90, "this quarter");

        var withGoal = CouncilVerdict.BuildItem(pack, goal, "Where can I save the most this quarter?")!;
        var without = CouncilVerdict.BuildItem(pack, null, "Where can I save the most this quarter?")!;

        Assert.Equal(CouncilVerdict.ReachableLabel, portfolioTarget.Subtitle);
        Assert.Contains("is reachable", withGoal.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("reachable", without.Title, StringComparison.Ordinal);
        Assert.Contains(withGoal.Values, v => v.Key == "coverageHigh" && v.Value == "30730");
    }
}
