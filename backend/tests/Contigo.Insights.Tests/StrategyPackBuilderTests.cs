using Contigo.Benchmark.Contracts;
using Contigo.Insights.Contracts;
using Contigo.Insights.Strategy;
using Contigo.SharedKernel;

namespace Contigo.Insights.Tests;

/// <summary>
/// Proves task E13/F07/US01/T01's <see cref="StrategyPackBuilder"/> — parent story us-01-insights
/// AC-4 ("a passed deadline is stated as passed; no benchmark match → no targets, levers only,
/// 'insufficient market data'") and the story's own "Council decisions carried into this story"
/// (section order; the four next-step tracker steps).
/// </summary>
public sealed class StrategyPackBuilderTests
{
    private static readonly DateOnly AsOfDate = new(2026, 9, 9);

    private static StrategyInputs Inputs(
        string? supplierName = "Salesforce",
        DateOnly? renewalDate = null,
        DateOnly? cancellationDeadline = null,
        int? daysUntilRenewal = null,
        int? daysUntilCancellationDeadline = null,
        bool autoRenewal = true,
        IReadOnlyList<PricedLine>? pricedLines = null,
        IReadOnlyList<CriticalFactConfidence>? criticalFacts = null) =>
        new(
            EntityId.New(),
            supplierName,
            renewalDate,
            cancellationDeadline,
            daysUntilRenewal,
            daysUntilCancellationDeadline,
            autoRenewal,
            pricedLines ?? [new PricedLine(null, "Sales Cloud Enterprise", 100m, 2300m, "USD", 12, null, null)],
            criticalFacts ?? [],
            AsOfDate);

    // ----- Section order -----

    [Fact]
    public void Pack_carries_all_four_sections_for_the_contract()
    {
        var pack = StrategyPackBuilder.Build(Inputs());

        Assert.NotNull(pack.WhenYouMustMove);
        Assert.NotEmpty(pack.WhereYouCanPush);
        Assert.NotEmpty(pack.Targets);
        Assert.Equal(4, pack.NextSteps.Count);
    }

    // ----- When you must move: passed deadline stated, not hidden -----

    [Fact]
    public void A_passed_cancellation_deadline_is_stated_as_passed()
    {
        var pack = StrategyPackBuilder.Build(Inputs(
            cancellationDeadline: new DateOnly(2026, 8, 1), daysUntilCancellationDeadline: -39));

        Assert.True(pack.WhenYouMustMove.PassedDeadline);
        Assert.Equal(-39, pack.WhenYouMustMove.DaysLeft);
        Assert.Contains("passed", pack.WhenYouMustMove.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_future_cancellation_deadline_is_not_flagged_as_passed()
    {
        var pack = StrategyPackBuilder.Build(Inputs(
            cancellationDeadline: new DateOnly(2026, 12, 1), daysUntilCancellationDeadline: 83));

        Assert.False(pack.WhenYouMustMove.PassedDeadline);
        Assert.Equal(83, pack.WhenYouMustMove.DaysLeft);
    }

    [Fact]
    public void Prefers_the_cancellation_deadline_over_the_renewal_date_for_days_left()
    {
        var pack = StrategyPackBuilder.Build(Inputs(
            daysUntilRenewal: 120, daysUntilCancellationDeadline: 30));

        Assert.Equal(30, pack.WhenYouMustMove.DaysLeft);
    }

    [Fact]
    public void No_auto_renewal_is_a_determined_fact_not_a_passed_deadline()
    {
        var pack = StrategyPackBuilder.Build(Inputs(autoRenewal: false, daysUntilRenewal: null));

        Assert.False(pack.WhenYouMustMove.PassedDeadline);
        Assert.Contains("does not auto-renew", pack.WhenYouMustMove.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    // ----- Targets: no band -> no targets, "insufficient market data" -----

    [Fact]
    public void No_benchmark_band_yields_no_targets_for_that_line()
    {
        var pack = StrategyPackBuilder.Build(Inputs(
            pricedLines: [new PricedLine(null, "Sales Cloud Enterprise", 100m, 2300m, "USD", 12, null, null)]));

        var target = Assert.Single(pack.Targets);
        Assert.Null(target.OpeningTarget);
        Assert.Null(target.AcceptableRangeLow);
        Assert.Null(target.AcceptableRangeHigh);
        Assert.Null(target.WalkAwayThreshold);
        Assert.Contains("insufficient market data", target.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_benchmark_band_yields_real_targets_for_that_line()
    {
        var distribution = new BenchmarkDistribution(1500m, 1800m, 2100m);
        var pack = StrategyPackBuilder.Build(Inputs(
            pricedLines: [new PricedLine(null, "Sales Cloud Enterprise", 100m, 2300m, "USD", 12, distribution, 40)]));

        var target = Assert.Single(pack.Targets);
        Assert.NotNull(target.OpeningTarget);
        Assert.Equal(1500m, target.AcceptableRangeLow);
        Assert.Equal(1800m, target.AcceptableRangeHigh);
    }

    // ----- Where you can push: levers still present without a band -----

    [Fact]
    public void Levers_are_still_present_when_no_priced_line_has_a_benchmark_band()
    {
        var pack = StrategyPackBuilder.Build(Inputs(
            pricedLines: [new PricedLine(null, "Sales Cloud Enterprise", 100m, 2300m, "USD", 12, null, null)]));

        Assert.Equal(7, pack.WhereYouCanPush.Count);
    }

    [Fact]
    public void Multi_line_contracts_prefix_lever_rationale_with_the_lines_own_description()
    {
        var pack = StrategyPackBuilder.Build(Inputs(pricedLines:
        [
            new PricedLine(null, "Sales Cloud Enterprise", 100m, 2300m, "USD", 12, null, null),
            new PricedLine(null, "Service Cloud", 50m, 1200m, "USD", 12, null, null),
        ]));

        Assert.Equal(14, pack.WhereYouCanPush.Count); // 7 levers per line, 2 lines
        Assert.Contains(pack.WhereYouCanPush, l => l.Rationale.StartsWith("Sales Cloud Enterprise: ", StringComparison.Ordinal));
        Assert.Contains(pack.WhereYouCanPush, l => l.Rationale.StartsWith("Service Cloud: ", StringComparison.Ordinal));
    }

    [Fact]
    public void Single_line_contracts_do_not_prefix_lever_rationale()
    {
        var pack = StrategyPackBuilder.Build(Inputs());

        Assert.DoesNotContain(pack.WhereYouCanPush, l => l.Rationale.StartsWith("Sales Cloud Enterprise:", StringComparison.Ordinal));
    }

    // ----- Next steps: the four tracker steps, contigo-v2/app.jsx stepDefs -----

    [Fact]
    public void Next_steps_are_the_four_tracker_steps_in_order()
    {
        var pack = StrategyPackBuilder.Build(Inputs(
            supplierName: "Salesforce", cancellationDeadline: new DateOnly(2026, 12, 1)));

        Assert.Collection(
            pack.NextSteps,
            s =>
            {
                Assert.Equal("Notify Salesforce of intent to renegotiate", s.Label);
                Assert.Equal("this week", s.DueHint);
            },
            s =>
            {
                Assert.Equal("Request revised pricing and licence mix", s.Label);
                Assert.Equal("+10 days", s.DueHint);
            },
            s =>
            {
                Assert.Equal("Counter with the market benchmark", s.Label);
                Assert.Equal("+20 days", s.DueHint);
            },
            s =>
            {
                Assert.Equal("Sign, or send non-renewal notice", s.Label);
                Assert.Equal("by 2026-12-01", s.DueHint);
            });
    }

    [Fact]
    public void Next_steps_fall_back_to_generic_phrasing_without_a_resolved_supplier_name()
    {
        var pack = StrategyPackBuilder.Build(Inputs(supplierName: null));

        Assert.Equal("Notify the supplier of intent to renegotiate", pack.NextSteps[0].Label);
    }

    [Fact]
    public void Next_steps_fall_back_to_generic_phrasing_without_a_known_cancellation_deadline()
    {
        var pack = StrategyPackBuilder.Build(Inputs(cancellationDeadline: null));

        Assert.Equal("by the cancellation deadline", pack.NextSteps[3].DueHint);
    }

    // ----- Open weak facts -----

    [Fact]
    public void Open_weak_facts_are_below_threshold_facts_as_fact_citation_keys()
    {
        var contractId = EntityId.New();
        var inputs = new StrategyInputs(
            contractId, "Salesforce", null, null, null, null, true,
            [new PricedLine(null, "Line", 1m, 100m, "USD", 12, null, null)],
            [new CriticalFactConfidence("annualSpend", 0.5), new CriticalFactConfidence("endDate", 0.95)],
            AsOfDate);

        var pack = StrategyPackBuilder.Build(inputs);

        var key = Assert.Single(pack.OpenWeakFacts);
        Assert.Equal($"fact:{contractId}:annualSpend", key);
    }

    [Fact]
    public void No_weak_facts_yields_an_empty_open_weak_facts_list()
    {
        var pack = StrategyPackBuilder.Build(Inputs(
            criticalFacts: [new CriticalFactConfidence("annualSpend", 0.95)]));

        Assert.Empty(pack.OpenWeakFacts);
    }

    [Fact]
    public void Rejects_a_null_inputs_argument()
    {
        Assert.Throws<ArgumentNullException>(() => StrategyPackBuilder.Build(null!));
    }

    // ----- Determinism -----

    [Fact]
    public void Same_inputs_produce_the_same_pack_every_time()
    {
        var inputs = Inputs();

        var first = StrategyPackBuilder.Build(inputs);
        var second = StrategyPackBuilder.Build(inputs);

        Assert.Equal(first.WhenYouMustMove, second.WhenYouMustMove);
        Assert.Equal(first.Targets, second.Targets);
        Assert.Equal(first.NextSteps, second.NextSteps);
        Assert.Equal(first.OpenWeakFacts, second.OpenWeakFacts);
    }
}
