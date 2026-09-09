using Contigo.Insights.Contracts;
using Contigo.Insights.Criticality;
using Contigo.SharedKernel;

namespace Contigo.Insights.Tests;

/// <summary>
/// Proves task E13/F07/US01/T01's <see cref="CriticalityScoreCalculator"/> — parent story
/// us-01-insights AC-1 ("components sum to the total; same inputs → same ranking; weights come from
/// configuration") and AC-2 ("A contract whose critical facts are all weak is flagged 'validate
/// first'... and its criticality is raised, not hidden"). Mirrors
/// <c>Contigo.Renewals.Tests.PriorityScoreCalculatorTests</c>' own shape/style (this codebase's
/// other "five weighted, explained components sum to a total" calculator).
/// </summary>
public sealed class CriticalityScoreCalculatorTests
{
    private static ContractCriticalityInputs Inputs(
        decimal? renewalTotalScore = 50m,
        CriticalityRiskSeverity? risk = CriticalityRiskSeverity.Medium,
        decimal? annualSpend = 100_000m,
        decimal portfolioAnnualSpend = 1_000_000m,
        decimal? aboveBandLineFraction = null,
        decimal? savingsLow = null,
        decimal? savingsHigh = null,
        IReadOnlyList<CriticalFactConfidence>? criticalFacts = null) =>
        new(
            EntityId.New(),
            new RenewalUrgencyInputs(renewalTotalScore),
            risk,
            annualSpend,
            portfolioAnnualSpend,
            new SavingsPotentialInputs(aboveBandLineFraction, savingsLow, savingsHigh),
            criticalFacts ?? []);

    // ----- AC-1: components sum to the total -----

    [Fact]
    public void Components_sum_to_the_total()
    {
        var calculator = new CriticalityScoreCalculator();

        var result = calculator.Calculate(Inputs());

        var sum = result.RenewalUrgency.Score + result.RiskSeverity.Score + result.SpendWeight.Score +
            result.SavingsPotential.Score + result.OpenCriticalFacts.Score;
        Assert.Equal(sum, result.TotalScore);
    }

    [Fact]
    public void Total_score_never_exceeds_100_under_the_council_default_weights()
    {
        var calculator = new CriticalityScoreCalculator();

        // Every component maxed out: full renewal urgency, critical risk, 100% spend share, full
        // savings potential, every critical fact weak.
        var result = calculator.Calculate(new ContractCriticalityInputs(
            EntityId.New(),
            new RenewalUrgencyInputs(100m),
            CriticalityRiskSeverity.Critical,
            1_000_000m,
            1_000_000m,
            new SavingsPotentialInputs(1m, null, null),
            [new CriticalFactConfidence("f1", 0.1), new CriticalFactConfidence("f2", 0.1)]));

        Assert.Equal(100m, result.TotalScore);
    }

    [Fact]
    public void Each_component_echoes_its_own_configured_weight()
    {
        var calculator = new CriticalityScoreCalculator();

        var result = calculator.Calculate(Inputs());

        Assert.Equal(0.30m, result.RenewalUrgency.Weight);
        Assert.Equal(0.20m, result.RiskSeverity.Weight);
        Assert.Equal(0.20m, result.SpendWeight.Weight);
        Assert.Equal(0.20m, result.SavingsPotential.Weight);
        Assert.Equal(0.10m, result.OpenCriticalFacts.Weight);
    }

    // ----- AC-1: weights from configuration, sum validated -----

    [Fact]
    public void Constructor_accepts_custom_weights_that_sum_to_one()
    {
        var options = new InsightsOptions
        {
            RenewalUrgency = 0.5m,
            RiskSeverity = 0.2m,
            SpendWeight = 0.1m,
            SavingsPotential = 0.1m,
            OpenCriticalFacts = 0.1m,
        };

        var calculator = new CriticalityScoreCalculator(options);
        var result = calculator.Calculate(Inputs());

        Assert.Equal(0.5m, result.RenewalUrgency.Weight);
    }

    [Fact]
    public void Constructor_rejects_weights_that_do_not_sum_to_one()
    {
        var options = new InsightsOptions { RenewalUrgency = 0.9m };

        Assert.Throws<ArgumentException>(() => new CriticalityScoreCalculator(options));
    }

    // ----- AC-1: same inputs -> same ranking -----

    [Fact]
    public void Same_inputs_produce_the_same_result_every_time()
    {
        var calculator = new CriticalityScoreCalculator();
        var inputs = Inputs();

        var first = calculator.Calculate(inputs);
        var second = calculator.Calculate(inputs);

        Assert.Equal(first.TotalScore, second.TotalScore);
        Assert.Equal(first.RenewalUrgency, second.RenewalUrgency);
        Assert.Equal(first.RiskSeverity, second.RiskSeverity);
        Assert.Equal(first.SpendWeight, second.SpendWeight);
        Assert.Equal(first.SavingsPotential, second.SavingsPotential);
        Assert.Equal(first.OpenCriticalFacts, second.OpenCriticalFacts);
    }

    [Fact]
    public void CalculateMany_ranks_most_critical_first_and_is_stable_across_runs()
    {
        var calculator = new CriticalityScoreCalculator();
        var low = Inputs(renewalTotalScore: 0m, risk: CriticalityRiskSeverity.Low, annualSpend: 1_000m);
        var high = Inputs(renewalTotalScore: 100m, risk: CriticalityRiskSeverity.Critical, annualSpend: 900_000m);
        var medium = Inputs(renewalTotalScore: 50m, risk: CriticalityRiskSeverity.Medium, annualSpend: 100_000m);

        var firstRun = calculator.CalculateMany([low, high, medium]);
        var secondRun = calculator.CalculateMany([medium, low, high]);

        Assert.Equal([high.ContractId, medium.ContractId, low.ContractId], firstRun.Select(r => r.ContractId));
        Assert.Equal(firstRun.Select(r => r.ContractId), secondRun.Select(r => r.ContractId));
    }

    // ----- AC-2: all-weak critical facts -----

    [Fact]
    public void All_weak_critical_facts_are_flagged_validate_first()
    {
        var calculator = new CriticalityScoreCalculator();
        var allWeak = Inputs(criticalFacts:
        [
            new CriticalFactConfidence("annualSpend", 0.4),
            new CriticalFactConfidence("endDate", 0.5),
        ]);

        var result = calculator.Calculate(allWeak);

        Assert.Contains("validate first", result.OpenCriticalFacts.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void All_weak_critical_facts_raise_criticality_rather_than_lower_it()
    {
        var calculator = new CriticalityScoreCalculator();
        var strongFacts = Inputs(criticalFacts: [new CriticalFactConfidence("annualSpend", 0.95)]);
        var weakFacts = Inputs(criticalFacts: [new CriticalFactConfidence("annualSpend", 0.1)]);

        var strongResult = calculator.Calculate(strongFacts);
        var weakResult = calculator.Calculate(weakFacts);

        Assert.True(weakResult.OpenCriticalFacts.Score > strongResult.OpenCriticalFacts.Score);
        Assert.True(weakResult.TotalScore > strongResult.TotalScore);
    }

    [Fact]
    public void No_tracked_critical_facts_defaults_to_the_minimum_not_validate_first()
    {
        var calculator = new CriticalityScoreCalculator();

        var result = calculator.Calculate(Inputs(criticalFacts: []));

        Assert.Equal(0m, result.OpenCriticalFacts.Score);
        Assert.DoesNotContain("validate first", result.OpenCriticalFacts.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Half_or_fewer_weak_facts_does_not_add_validate_first()
    {
        var calculator = new CriticalityScoreCalculator();

        var result = calculator.Calculate(Inputs(criticalFacts:
        [
            new CriticalFactConfidence("a", 0.4),
            new CriticalFactConfidence("b", 0.95),
        ]));

        Assert.DoesNotContain("validate first", result.OpenCriticalFacts.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    // ----- Renewal urgency -----

    [Fact]
    public void Renewal_urgency_normalizes_the_priority_total_score_to_0_1()
    {
        var calculator = new CriticalityScoreCalculator();

        var fullyUrgent = calculator.Calculate(Inputs(renewalTotalScore: 100m));
        var notUrgent = calculator.Calculate(Inputs(renewalTotalScore: 0m));

        Assert.Equal(30m, fullyUrgent.RenewalUrgency.Score); // 1.0 ratio * 0.30 weight * 100
        Assert.Equal(0m, notUrgent.RenewalUrgency.Score);
    }

    [Fact]
    public void Renewal_urgency_defaults_to_the_minimum_when_no_priority_score_exists()
    {
        var calculator = new CriticalityScoreCalculator();

        var result = calculator.Calculate(Inputs(renewalTotalScore: null));

        Assert.Equal(0m, result.RenewalUrgency.Score);
        Assert.Contains("No renewal priority score", result.RenewalUrgency.Explanation);
    }

    // ----- Risk severity -----

    [Theory]
    [InlineData(null, 0)]
    [InlineData(CriticalityRiskSeverity.None, 0)]
    [InlineData(CriticalityRiskSeverity.Low, 5)]
    [InlineData(CriticalityRiskSeverity.Medium, 10)]
    [InlineData(CriticalityRiskSeverity.High, 15)]
    [InlineData(CriticalityRiskSeverity.Critical, 20)]
    public void Risk_severity_scores_each_level(CriticalityRiskSeverity? severity, decimal expectedScore)
    {
        var calculator = new CriticalityScoreCalculator();

        var result = calculator.Calculate(Inputs(risk: severity));

        // ratio (0/0.25/0.5/0.75/1) * 0.20 weight * 100
        Assert.Equal(expectedScore, result.RiskSeverity.Score);
    }

    // ----- Spend weight -----

    [Fact]
    public void Spend_weight_is_the_portfolio_share()
    {
        var calculator = new CriticalityScoreCalculator();

        var result = calculator.Calculate(Inputs(annualSpend: 250_000m, portfolioAnnualSpend: 1_000_000m));

        // 0.25 ratio * 0.20 weight * 100
        Assert.Equal(5m, result.SpendWeight.Score);
    }

    [Fact]
    public void Spend_weight_defaults_to_the_minimum_when_annual_spend_is_unknown()
    {
        var calculator = new CriticalityScoreCalculator();

        var result = calculator.Calculate(Inputs(annualSpend: null));

        Assert.Equal(0m, result.SpendWeight.Score);
    }

    [Fact]
    public void Spend_weight_defaults_to_the_minimum_when_portfolio_spend_is_zero()
    {
        var calculator = new CriticalityScoreCalculator();

        var result = calculator.Calculate(Inputs(annualSpend: 100_000m, portfolioAnnualSpend: 0m));

        Assert.Equal(0m, result.SpendWeight.Score);
    }

    [Fact]
    public void Spend_weight_caps_at_one_even_if_a_single_contract_exceeds_the_recorded_portfolio_total()
    {
        var calculator = new CriticalityScoreCalculator();

        var result = calculator.Calculate(Inputs(annualSpend: 2_000_000m, portfolioAnnualSpend: 1_000_000m));

        Assert.Equal(20m, result.SpendWeight.Score); // capped at 1.0 ratio * 0.20 weight * 100
    }

    // ----- Savings potential -----

    [Fact]
    public void Savings_potential_prefers_above_band_line_fraction_when_present()
    {
        var calculator = new CriticalityScoreCalculator();

        // aboveBandLineFraction 0.5 weighted by this contract's own 0.1 spend-weight ratio (100k/1M)
        var result = calculator.Calculate(
            Inputs(annualSpend: 100_000m, portfolioAnnualSpend: 1_000_000m, aboveBandLineFraction: 0.5m,
                savingsLow: 999_999m, savingsHigh: 999_999m)); // present but must be ignored

        Assert.Equal(1m, result.SavingsPotential.Score); // 0.5 * 0.1 = 0.05 ratio * 0.20 weight * 100
    }

    [Fact]
    public void Savings_potential_falls_back_to_the_savings_opportunity_midpoint_over_spend()
    {
        var calculator = new CriticalityScoreCalculator();

        var result = calculator.Calculate(
            Inputs(annualSpend: 100_000m, savingsLow: 10_000m, savingsHigh: 30_000m));

        // midpoint 20,000 / 100,000 = 0.2 ratio * 0.20 weight * 100
        Assert.Equal(4m, result.SavingsPotential.Score);
    }

    [Fact]
    public void Savings_potential_caps_at_one_when_the_opportunity_exceeds_spend()
    {
        var calculator = new CriticalityScoreCalculator();

        var result = calculator.Calculate(
            Inputs(annualSpend: 10_000m, savingsLow: 50_000m, savingsHigh: 90_000m));

        Assert.Equal(20m, result.SavingsPotential.Score); // capped at 1.0 ratio * 0.20 weight * 100
    }

    [Fact]
    public void Savings_potential_defaults_to_the_minimum_when_no_signal_is_available()
    {
        var calculator = new CriticalityScoreCalculator();

        var result = calculator.Calculate(Inputs());

        Assert.Equal(0m, result.SavingsPotential.Score);
        Assert.Contains("No above-band", result.SavingsPotential.Explanation);
    }

    [Fact]
    public void Rejects_a_null_inputs_argument()
    {
        var calculator = new CriticalityScoreCalculator();

        Assert.Throws<ArgumentNullException>(() => calculator.Calculate(null!));
    }
}
