using System.Reflection;
using Raffa.Renewals.Application;
using Raffa.Renewals.Configuration;
using Raffa.Renewals.Domain;
using Raffa.SharedKernel;

namespace Raffa.Renewals.Tests;

/// <summary>
/// Proves task E03/F01/US02/T01's execution step: <see cref="PriorityScoreCalculator"/> computes
/// an explainable priority score with a component breakdown from a
/// <see cref="RenewalCalculationResult"/> plus a <see cref="RenewalPriorityInputs"/> snapshot —
/// parent story us-02-priority-score AC-1 ("Score = spend weight + time urgency + benchmark
/// opportunity + uplift risk + contract risk"), AC-2 ("Component scores are stored separately —
/// explainable and tunable") and AC-3 ("Benchmark-opportunity component reads the R3 benchmark
/// only when available (else neutral)") — with no database, no HTTP call and no LLM call anywhere
/// in the path.
/// </summary>
public sealed class PriorityScoreCalculatorTests
{
    private readonly PriorityScoreCalculator _calculator = new();

    private static RenewalCalculationResult RenewalWithDays(int? daysUntilRenewal, EntityId? contractId = null) =>
        new(
            contractId ?? EntityId.New(),
            RenewalCalculationStatus.Determined,
            null,
            null,
            daysUntilRenewal,
            null,
            "fixture");

    private static RenewalPriorityInputs Inputs(
        int? spend = null,
        int? uplift = null,
        ContractRiskLevel? risk = null,
        int? benchmarkPosition = null) =>
        new(spend, uplift, risk, benchmarkPosition);

    // ----- AC-1: total = sum of the five named components -----

    [Fact]
    public void TotalScore_is_the_sum_of_every_component_using_the_spec_9_3_insight_card_example()
    {
        // Product spec §9.3's own renewal-insight-card example: 134 days to renewal, CHF 640k
        // annual spend, 7% annual uplift, 18% above benchmark. Contract risk is not part of that
        // example, so a fixed High is used here.
        var renewal = RenewalWithDays(134);
        var inputs = Inputs(spend: 640_000, uplift: 7, risk: ContractRiskLevel.High, benchmarkPosition: 18);

        var result = _calculator.Calculate(renewal, inputs);

        Assert.Equal(renewal.ContractId, result.ContractId);
        Assert.Equal(20, result.SpendWeight.Score);
        Assert.Equal(9, result.TimeUrgency.Score);
        Assert.Equal(15, result.BenchmarkOpportunity.Score);
        Assert.Equal(14, result.PriceIncreaseRisk.Score);
        Assert.Equal(15, result.ContractRisk.Score);
        Assert.Equal(73, result.TotalScore);
    }

    [Fact]
    public void TotalScore_reaches_the_maximum_when_every_component_is_maxed()
    {
        var renewal = RenewalWithDays(-5); // overdue
        var inputs = Inputs(spend: 1_000_000, uplift: 50, risk: ContractRiskLevel.Critical, benchmarkPosition: 100);

        var result = _calculator.Calculate(renewal, inputs);

        Assert.Equal(PriorityScoreCalculator.MaxTotalScore, result.TotalScore);
        Assert.Equal(100, result.TotalScore);
        Assert.Equal(PriorityScoreCalculator.MaxComponentScore, result.SpendWeight.Score);
        Assert.Equal(PriorityScoreCalculator.MaxComponentScore, result.TimeUrgency.Score);
        Assert.Equal(PriorityScoreCalculator.MaxComponentScore, result.BenchmarkOpportunity.Score);
        Assert.Equal(PriorityScoreCalculator.MaxComponentScore, result.PriceIncreaseRisk.Score);
        Assert.Equal(PriorityScoreCalculator.MaxComponentScore, result.ContractRisk.Score);
    }

    [Fact]
    public void TotalScore_is_only_the_neutral_benchmark_component_when_every_raw_input_is_unknown()
    {
        var renewal = RenewalWithDays(null);

        var result = _calculator.Calculate(renewal, RenewalPriorityInputs.Unknown);

        Assert.Equal(0, result.SpendWeight.Score);
        Assert.Equal(0, result.TimeUrgency.Score);
        Assert.Equal(PriorityScoreCalculator.NeutralComponentScore, result.BenchmarkOpportunity.Score);
        Assert.Equal(0, result.PriceIncreaseRisk.Score);
        Assert.Equal(0, result.ContractRisk.Score);
        Assert.Equal(PriorityScoreCalculator.NeutralComponentScore, result.TotalScore);

        Assert.Contains("Appendix C rule 10", result.SpendWeight.Explanation);
        Assert.Contains("Appendix C rule 10", result.TimeUrgency.Explanation);
        Assert.Contains("Appendix C rule 10", result.PriceIncreaseRisk.Explanation);
        Assert.Contains("Appendix C rule 10", result.ContractRisk.Explanation);
    }

    // ----- AC-3: benchmark-opportunity reads the R3 benchmark only when available, else neutral -----

    [Fact]
    public void BenchmarkOpportunity_is_neutral_and_explains_why_when_no_benchmark_data_is_available()
    {
        var result = _calculator.Calculate(RenewalWithDays(null), Inputs(benchmarkPosition: null));

        Assert.Equal(PriorityScoreCalculator.NeutralComponentScore, result.BenchmarkOpportunity.Score);
        Assert.Contains("AC-3", result.BenchmarkOpportunity.Explanation);
        Assert.Contains("not available", result.BenchmarkOpportunity.Explanation);
    }

    [Theory]
    [InlineData(null, 10)]
    [InlineData(20, 20)]
    [InlineData(19, 15)]
    [InlineData(5, 15)]
    [InlineData(4, 10)]
    [InlineData(0, 10)]
    [InlineData(-5, 10)]
    [InlineData(-6, 5)]
    [InlineData(-20, 5)]
    [InlineData(-21, 0)]
    [InlineData(-100, 0)]
    public void BenchmarkOpportunity_is_tiered_by_market_position(int? position, int expectedScore)
    {
        var result = _calculator.Calculate(RenewalWithDays(null), Inputs(benchmarkPosition: position));

        Assert.Equal(expectedScore, result.BenchmarkOpportunity.Score);
    }

    // ----- Spend weight -----

    [Theory]
    [InlineData(null, 0)]
    [InlineData(-100, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 4)]
    [InlineData(24_999, 4)]
    [InlineData(25_000, 8)]
    [InlineData(99_999, 8)]
    [InlineData(100_000, 12)]
    [InlineData(249_999, 12)]
    [InlineData(250_000, 16)]
    [InlineData(499_999, 16)]
    [InlineData(500_000, 20)]
    [InlineData(10_000_000, 20)]
    public void SpendWeight_is_tiered_by_annual_spend(int? annualSpend, int expectedScore)
    {
        var result = _calculator.Calculate(RenewalWithDays(null), Inputs(spend: annualSpend));

        Assert.Equal(expectedScore, result.SpendWeight.Score);
    }

    // ----- Time urgency -----

    [Theory]
    [InlineData(null, 0)]
    [InlineData(-1000, 20)]
    [InlineData(-1, 20)]
    [InlineData(0, 20)]
    [InlineData(30, 20)]
    [InlineData(31, 17)]
    [InlineData(60, 17)]
    [InlineData(61, 15)]
    [InlineData(90, 15)]
    [InlineData(91, 12)]
    [InlineData(120, 12)]
    [InlineData(121, 9)]
    [InlineData(180, 9)]
    [InlineData(181, 6)]
    [InlineData(270, 6)]
    [InlineData(271, 3)]
    [InlineData(365, 3)]
    [InlineData(366, 0)]
    [InlineData(10_000, 0)]
    public void TimeUrgency_is_tiered_by_days_until_renewal_including_overdue(int? daysUntilRenewal, int expectedScore)
    {
        var result = _calculator.Calculate(RenewalWithDays(daysUntilRenewal), RenewalPriorityInputs.Unknown);

        Assert.Equal(expectedScore, result.TimeUrgency.Score);
    }

    [Fact]
    public void TimeUrgency_explanation_states_overdue_days_when_the_renewal_date_already_passed()
    {
        var result = _calculator.Calculate(RenewalWithDays(-12), RenewalPriorityInputs.Unknown);

        Assert.Contains("12 day(s) overdue", result.TimeUrgency.Explanation);
    }

    // ----- Price increase risk (uplift) -----

    [Theory]
    [InlineData(null, 0)]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 4)]
    [InlineData(2, 4)]
    [InlineData(3, 9)]
    [InlineData(6, 9)]
    [InlineData(7, 14)]
    [InlineData(11, 14)]
    [InlineData(12, 18)]
    [InlineData(19, 18)]
    [InlineData(20, 20)]
    [InlineData(100, 20)]
    public void PriceIncreaseRisk_is_tiered_by_annual_uplift_percent(int? annualUpliftPercent, int expectedScore)
    {
        var result = _calculator.Calculate(RenewalWithDays(null), Inputs(uplift: annualUpliftPercent));

        Assert.Equal(expectedScore, result.PriceIncreaseRisk.Score);
    }

    // ----- Contract risk -----

    [Theory]
    [InlineData(null, 0)]
    [InlineData(ContractRiskLevel.Low, 3)]
    [InlineData(ContractRiskLevel.Medium, 9)]
    [InlineData(ContractRiskLevel.High, 15)]
    [InlineData(ContractRiskLevel.Critical, 20)]
    public void ContractRisk_is_tiered_by_risk_level(ContractRiskLevel? riskLevel, int expectedScore)
    {
        var result = _calculator.Calculate(RenewalWithDays(null), Inputs(risk: riskLevel));

        Assert.Equal(expectedScore, result.ContractRisk.Score);
    }

    // ----- Component scores never leave the documented range -----

    [Theory]
    [MemberData(nameof(EveryComponentResultOfAllBoundaryScenarios))]
    public void Every_component_score_stays_within_0_and_MaxComponentScore(PriorityScoreComponent component)
    {
        Assert.InRange(component.Score, 0m, PriorityScoreCalculator.MaxComponentScore);
    }

    public static TheoryData<PriorityScoreComponent> EveryComponentResultOfAllBoundaryScenarios()
    {
        var calculator = new PriorityScoreCalculator();
        var scenarios = new[]
        {
            calculator.Calculate(RenewalWithDays(null), RenewalPriorityInputs.Unknown),
            calculator.Calculate(RenewalWithDays(-5), Inputs(1_000_000, 50, ContractRiskLevel.Critical, 100)),
            calculator.Calculate(RenewalWithDays(134), Inputs(640_000, 7, ContractRiskLevel.High, 18)),
            calculator.Calculate(RenewalWithDays(400), Inputs(-1, -1, ContractRiskLevel.Low, -100)),
        };

        var data = new TheoryData<PriorityScoreComponent>();
        foreach (var result in scenarios)
        {
            data.Add(result.SpendWeight);
            data.Add(result.TimeUrgency);
            data.Add(result.BenchmarkOpportunity);
            data.Add(result.PriceIncreaseRisk);
            data.Add(result.ContractRisk);
        }

        return data;
    }

    // ----- Determinism (Appendix C rule 6) -----

    [Fact]
    public void Same_inputs_produce_the_same_result_every_time()
    {
        var renewal = RenewalWithDays(200);
        var inputs = Inputs(spend: 300_000, uplift: 9, risk: ContractRiskLevel.Medium, benchmarkPosition: -10);

        var first = _calculator.Calculate(renewal, inputs);
        var second = _calculator.Calculate(renewal, inputs);

        // PriorityScoreResult and PriorityScoreComponent are records: this is value equality
        // across every field, including every Explanation.
        Assert.Equal(first, second);
    }

    // ----- Null-argument handling -----

    [Fact]
    public void Rejects_a_null_renewal_argument()
    {
        Assert.Throws<ArgumentNullException>(() => _calculator.Calculate(null!, RenewalPriorityInputs.Unknown));
    }

    [Fact]
    public void Rejects_a_null_inputs_argument()
    {
        Assert.Throws<ArgumentNullException>(() => _calculator.Calculate(RenewalWithDays(null), null!));
    }

    [Fact]
    public void Rejects_a_null_contract_list_in_the_batch_form()
    {
        Assert.Throws<ArgumentNullException>(() => _calculator.CalculateMany(null!));
    }

    // ----- Batch form -----

    [Fact]
    public void CalculateMany_computes_one_result_per_contract_preserving_order_and_correlation()
    {
        var first = RenewalWithDays(10);
        var second = RenewalWithDays(400);
        var third = RenewalWithDays(null);

        var results = _calculator.CalculateMany(
        [
            (first, Inputs(spend: 600_000)),
            (second, RenewalPriorityInputs.Unknown),
            (third, Inputs(risk: ContractRiskLevel.Critical)),
        ]);

        Assert.Equal(3, results.Count);
        Assert.Equal(first.ContractId, results[0].ContractId);
        Assert.Equal(second.ContractId, results[1].ContractId);
        Assert.Equal(third.ContractId, results[2].ContractId);

        Assert.Equal(_calculator.Calculate(first, Inputs(spend: 600_000)), results[0]);
    }

    // ----- Appendix C rule 3 / parent story AC-3: no live dependency on the Benchmark Service -----
    //
    // Same structural proof Raffa.Renewals.Tests.RenewalEngineTests.Engine_has_no_dependency_on
    // _the_Benchmark_Service uses: benchmark data only ever reaches this calculator as an
    // already-known, plain nullable value on RenewalPriorityInputs — never through a live
    // IBenchmarkService call — so AC-3's "reads the R3 benchmark only when available" can never
    // become an accidental provider call from this class.
    [Fact]
    public void Calculator_has_no_dependency_on_the_Benchmark_Service()
    {
        var type = typeof(PriorityScoreCalculator);

        var constructorParamsFromBenchmark = type.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Where(p => p.ParameterType.Namespace == "Raffa.Benchmark")
            .ToList();

        var methodParamsFromBenchmark = type
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetParameters())
            .Where(p => p.ParameterType.Namespace == "Raffa.Benchmark")
            .ToList();

        Assert.Empty(constructorParamsFromBenchmark);
        Assert.Empty(methodParamsFromBenchmark);
    }

    // ----- Tunable weights (task E03/F01/US02/T02, parent story AC-2 "tunable") -----

    [Fact]
    public void No_weights_supplied_reproduces_the_spec_default_20_point_scale()
    {
        // The parameterless constructor (every test above uses it via the shared _calculator
        // field) must behave identically to one explicitly given a fresh, unconfigured
        // PriorityScoreWeightsOptions — proves the null-weights fallback documented on
        // PriorityScoreCalculator's own _weights field is really "the spec default", not some
        // other behaviour.
        var explicitDefault = new PriorityScoreCalculator(new PriorityScoreWeightsOptions());
        var renewal = RenewalWithDays(134);
        var inputs = Inputs(spend: 640_000, uplift: 7, risk: ContractRiskLevel.High, benchmarkPosition: 18);

        Assert.Equal(_calculator.Calculate(renewal, inputs), explicitDefault.Calculate(renewal, inputs));
    }

    [Fact]
    public void Custom_SpendWeightMax_rescales_every_spend_tier_proportionally_not_just_the_maximum()
    {
        var calculator = new PriorityScoreCalculator(new PriorityScoreWeightsOptions { SpendWeightMax = 40m });

        // 300,000 lands in the ">= 250,000" tier — spec-default fraction 0.8 of the maximum (16 of
        // 20) — so a doubled maximum must produce a doubled tier score (32 of 40), not just a
        // doubled *ceiling* that leaves the middle tiers untouched.
        var result = calculator.Calculate(RenewalWithDays(null), Inputs(spend: 300_000));

        Assert.Equal(32m, result.SpendWeight.Score);
        Assert.Contains("32", result.SpendWeight.Explanation);
    }

    [Fact]
    public void Custom_weights_change_each_components_maximum_contribution_and_the_total()
    {
        var weights = new PriorityScoreWeightsOptions
        {
            SpendWeightMax = 40m,
            TimeUrgencyMax = 10m,
            BenchmarkOpportunityMax = 8m,
            PriceIncreaseRiskMax = 100m,
            ContractRiskMax = 1m,
        };
        var calculator = new PriorityScoreCalculator(weights);
        var renewal = RenewalWithDays(-5); // overdue -> maximum time urgency
        var inputs = Inputs(spend: 1_000_000, uplift: 50, risk: ContractRiskLevel.Critical, benchmarkPosition: 100);

        var result = calculator.Calculate(renewal, inputs);

        // Every input saturates its component (mirrors
        // TotalScore_reaches_the_maximum_when_every_component_is_maxed, but with configured
        // weights instead of the spec default) — each component lands exactly on its own
        // configured maximum, and the total is their sum, not the spec-default 100.
        Assert.Equal(40m, result.SpendWeight.Score);
        Assert.Equal(10m, result.TimeUrgency.Score);
        Assert.Equal(8m, result.BenchmarkOpportunity.Score);
        Assert.Equal(100m, result.PriceIncreaseRisk.Score);
        Assert.Equal(1m, result.ContractRisk.Score);
        Assert.Equal(159m, result.TotalScore);
    }

    [Fact]
    public void Custom_BenchmarkOpportunityMax_rescales_the_neutral_no_data_value_too()
    {
        // Parent story AC-3's "neutral" value is half of the benchmark component's own maximum
        // (PriorityScoreCalculator.NeutralComponentScore's own doc comment) — proving it tracks a
        // configured BenchmarkOpportunityMax (not the spec-default const) is the whole point of
        // making that maximum tunable in the first place.
        var calculator = new PriorityScoreCalculator(new PriorityScoreWeightsOptions { BenchmarkOpportunityMax = 9m });

        var result = calculator.Calculate(RenewalWithDays(null), Inputs(benchmarkPosition: null));

        Assert.Equal(4.5m, result.BenchmarkOpportunity.Score);
        Assert.Contains("4.5", result.BenchmarkOpportunity.Explanation);
    }

    [Fact]
    public void Custom_weights_do_not_change_which_tier_a_contract_falls_into()
    {
        // The rescale is proportional, not a re-derivation of the tiering (this task's own scope
        // boundary, see PriorityScoreWeightsOptions' own doc comment): a spend just under the
        // 500,000 boundary must still land in the "250,000 or more" tier, not silently jump to
        // the maximum just because a larger weight is configured.
        var calculator = new PriorityScoreCalculator(new PriorityScoreWeightsOptions { SpendWeightMax = 1_000m });

        var result = calculator.Calculate(RenewalWithDays(null), Inputs(spend: 499_999));

        Assert.Equal(800m, result.SpendWeight.Score); // 0.8 * 1,000, the "250,000 or more" tier
        Assert.NotEqual(1_000m, result.SpendWeight.Score); // never the maximum for this input
    }
}
