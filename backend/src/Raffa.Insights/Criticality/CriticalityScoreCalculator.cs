using System.Globalization;
using Raffa.Insights.Contracts;

namespace Raffa.Insights.Criticality;

/// <summary>
/// The deterministic, explainable portfolio-criticality calculator (task E13/F07/US01/T01,
/// insights-calculators; parent story us-01-insights AC-1/AC-2; product spec §12.1, R-PORT-01: "A
/// deterministic, explainable score 0-100 per validated contract"). Pure and synchronous: no
/// database call, no HTTP call, no LLM call anywhere in <see cref="Calculate"/> — the same
/// <see cref="ContractCriticalityInputs"/> always produces the same <see cref="CriticalityScore"/>
/// (Appendix C rule 6), the same determinism convention
/// <c>Raffa.Renewals.Application.PriorityScoreCalculator</c> already established for the
/// structurally identical "five weighted, explained components sum to a total" shape.
///
/// <para>
/// Every component first computes a raw ratio in [0, 1] (how urgent / how severe / how large a
/// share of spend / how much room to improve / how many weak facts), then scales it by that
/// component's own configured <see cref="InsightsOptions"/> weight × 100 — so
/// <see cref="CriticalityComponent.Score"/> is always 0-(weight × 100), and summing all five always
/// lands on the same 0-100 scale when the weights sum to 1.0 (AC-1's own "components sum to the
/// total"). A raw input this calculator cannot determine never fabricates a guess (Appendix C rule
/// 10): every component defaults to the conservative minimum (ratio 0, "not urgent"/"not risky"/
/// "no spend share"/"no improvement signal") <b>except</b> open-critical-facts, whose own "no facts
/// tracked" case is likewise 0 but whose "facts tracked and weak" case deliberately raises the score
/// (AC-2: "flagged... and its criticality is raised, not hidden" — an all-weak contract is exactly
/// the one a portfolio ranking must not quietly deprioritize).
/// </para>
/// </summary>
public sealed class CriticalityScoreCalculator
{
    /// <summary>Below this confidence, a tracked critical fact counts as "weak" for the
    /// open-critical-facts component (task E13/F07/US01/T01's own coding objective: "count of
    /// critical fields below 0.8").</summary>
    public const double WeakFactConfidenceThreshold = 0.8;

    /// <summary>Above this fraction of weak critical facts, the open-critical-facts explanation adds
    /// the "validate first" flag (task's own coding objective: "explanation 'validate first' when >
    /// 0.5"; AC-2).</summary>
    public const decimal ValidateFirstThreshold = 0.5m;

    /// <summary>Renewal-urgency's own <see cref="RenewalUrgencyInputs.TotalScore"/> is normalized
    /// against this spec-default maximum (<c>Raffa.Renewals.Application.PriorityScoreCalculator
    /// .MaxTotalScore</c> — five components at 20 each under the spec-default
    /// <c>PriorityScoreWeightsOptions</c>) — see <see cref="RenewalUrgencyInputs"/>'s own doc comment
    /// for why this calculator cannot instead read a tuned instance's true maximum.</summary>
    public const decimal RenewalUrgencyMaxTotalScore = 100m;

    /// <summary>Culture-invariant tolerance for the configured-weights sum check below — decimal
    /// arithmetic on the council-decided defaults (0.30 + 0.20 + 0.20 + 0.20 + 0.10) is exact, but a
    /// hand-edited configuration section might not sum to precisely 1; this is generous enough to
    /// accept a reasonable rounding while still catching a genuinely mis-configured set.</summary>
    private const decimal WeightSumTolerance = 0.0005m;

    private readonly InsightsOptions _options;

    /// <summary>
    /// <paramref name="options"/> defaults to the council-decided weights (0.30/0.20/0.20/0.20/0.10)
    /// when not supplied — the same "always-usable default" convention
    /// <c>PriorityScoreCalculator(PriorityScoreWeightsOptions?)</c> already follows. Validates that
    /// the five weights sum to 1.0 (within <see cref="WeightSumTolerance"/>) — task's own coding
    /// objective: "weights from InsightsOptions (..., sum validated)" — so a mis-configured
    /// deployment fails fast at startup rather than silently producing a <see cref="CriticalityScore.TotalScore"/>
    /// that is not actually 0-100.
    /// </summary>
    /// <exception cref="ArgumentException">The five configured weights do not sum to 1.0.</exception>
    public CriticalityScoreCalculator(InsightsOptions? options = null)
    {
        _options = options ?? new InsightsOptions();

        var sum = _options.RenewalUrgency + _options.RiskSeverity + _options.SpendWeight +
            _options.SavingsPotential + _options.OpenCriticalFacts;

        if (Math.Abs(sum - 1.0m) > WeightSumTolerance)
        {
            throw new ArgumentException(
                $"InsightsOptions weights must sum to 1.0 (got {Fmt(sum)}: " +
                $"RenewalUrgency={Fmt(_options.RenewalUrgency)}, RiskSeverity={Fmt(_options.RiskSeverity)}, " +
                $"SpendWeight={Fmt(_options.SpendWeight)}, SavingsPotential={Fmt(_options.SavingsPotential)}, " +
                $"OpenCriticalFacts={Fmt(_options.OpenCriticalFacts)}).",
                nameof(options));
        }
    }

    /// <summary>Computes <paramref name="inputs"/>'s five criticality components and their total.
    /// Every branch is covered by <c>Raffa.Insights.Tests.CriticalityScoreCalculatorTests</c>.</summary>
    public CriticalityScore Calculate(ContractCriticalityInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var renewalUrgency = ComputeRenewalUrgency(inputs.RenewalUrgency);
        var riskSeverity = ComputeRiskSeverity(inputs.HighestRiskSeverity);
        var (spendWeightRatio, spendWeight) = ComputeSpendWeight(inputs.AnnualSpend, inputs.PortfolioAnnualSpend);
        var savingsPotential = ComputeSavingsPotential(inputs.SavingsPotential, inputs.AnnualSpend, spendWeightRatio);
        var openCriticalFacts = ComputeOpenCriticalFacts(inputs.CriticalFacts);

        var total = renewalUrgency.Score + riskSeverity.Score + spendWeight.Score +
            savingsPotential.Score + openCriticalFacts.Score;

        return new CriticalityScore(
            inputs.ContractId, total, renewalUrgency, riskSeverity, spendWeight, savingsPotential, openCriticalFacts);
    }

    /// <summary>Convenience batch form of <see cref="Calculate"/> — the "portfolio criticality
    /// ranking" shape (task's own coding objective: "CalculateMany returns the ranked list";
    /// R-PORT-02's own "ranked lists (top 5)"). Most-critical first
    /// (<see cref="CriticalityScore.TotalScore"/> descending); <see cref="ContractCriticalityInputs.ContractId"/>
    /// is a deterministic tie-break (same identical-score inputs always sort the same way, mirroring
    /// <c>Raffa.Renewals.Application.RenewalPipelineBuilder.Build</c>'s own tie-break convention) —
    /// AC-1's own "same inputs → same ranking".</summary>
    public IReadOnlyList<CriticalityScore> CalculateMany(IEnumerable<ContractCriticalityInputs> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        return inputs
            .Select(Calculate)
            .OrderByDescending(score => score.TotalScore)
            .ThenBy(score => score.ContractId.Value)
            .ToList();
    }

    private CriticalityComponent ComputeRenewalUrgency(RenewalUrgencyInputs renewalUrgency)
    {
        var weight = _options.RenewalUrgency;

        if (renewalUrgency.TotalScore is not { } totalScore)
        {
            return new CriticalityComponent(0m, weight,
                "No renewal priority score is available for this contract: renewal urgency defaults " +
                "to the minimum (0) rather than guessing (Appendix C rule 10).");
        }

        var ratio = Clamp01(totalScore / RenewalUrgencyMaxTotalScore);
        var score = ratio * weight * 100m;

        return new CriticalityComponent(score, weight,
            $"Renewal priority score {Fmt(totalScore)} / {Fmt(RenewalUrgencyMaxTotalScore)} " +
            $"normalized to {Fmt(ratio)}, weighted at {Fmt(weight)}: renewal urgency {Fmt(score)}.");
    }

    private CriticalityComponent ComputeRiskSeverity(CriticalityRiskSeverity? severity)
    {
        var weight = _options.RiskSeverity;

        var ratio = severity switch
        {
            null or CriticalityRiskSeverity.None => 0m,
            CriticalityRiskSeverity.Low => 0.25m,
            CriticalityRiskSeverity.Medium => 0.5m,
            CriticalityRiskSeverity.High => 0.75m,
            CriticalityRiskSeverity.Critical => 1m,
            _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unknown CriticalityRiskSeverity."),
        };

        var score = ratio * weight * 100m;
        var label = severity is null ? "no risk assessed" : severity.Value.ToString();

        return new CriticalityComponent(score, weight,
            $"Highest recorded risk severity is {label} (ratio {Fmt(ratio)}), weighted at " +
            $"{Fmt(weight)}: risk severity {Fmt(score)}.");
    }

    private (decimal Ratio, CriticalityComponent Component) ComputeSpendWeight(
        decimal? annualSpend, decimal portfolioAnnualSpend)
    {
        var weight = _options.SpendWeight;

        if (annualSpend is not { } spend || spend <= 0m)
        {
            var component = new CriticalityComponent(0m, weight,
                "AnnualSpend is unknown or not positive: spend weight defaults to the minimum (0) " +
                "rather than guessing (Appendix C rule 10).");
            return (0m, component);
        }

        if (portfolioAnnualSpend <= 0m)
        {
            var component = new CriticalityComponent(0m, weight,
                "PortfolioAnnualSpend is zero (no comparable portfolio spend recorded): spend " +
                "weight defaults to the minimum (0) rather than dividing by zero.");
            return (0m, component);
        }

        var ratio = Clamp01(spend / portfolioAnnualSpend);
        var score = ratio * weight * 100m;

        var scoredComponent = new CriticalityComponent(score, weight,
            $"AnnualSpend {Fmt(spend)} / PortfolioAnnualSpend {Fmt(portfolioAnnualSpend)} = " +
            $"{Fmt(ratio)} portfolio share, weighted at {Fmt(weight)}: spend weight {Fmt(score)}.");
        return (ratio, scoredComponent);
    }

    private CriticalityComponent ComputeSavingsPotential(
        SavingsPotentialInputs savingsPotential, decimal? annualSpend, decimal spendWeightRatio)
    {
        var weight = _options.SavingsPotential;

        if (savingsPotential.AboveBandLineFraction is { } aboveBandFraction)
        {
            var ratio = Clamp01(aboveBandFraction * spendWeightRatio);
            var score = ratio * weight * 100m;

            return new CriticalityComponent(score, weight,
                $"{Fmt(aboveBandFraction)} of priced lines are above the market band, weighted by " +
                $"this contract's own {Fmt(spendWeightRatio)} portfolio spend share = {Fmt(ratio)}, " +
                $"weighted at {Fmt(weight)}: savings potential {Fmt(score)}.");
        }

        if (savingsPotential is { SavingsOpportunityRangeLow: { } low, SavingsOpportunityRangeHigh: { } high }
            && annualSpend is { } spend && spend > 0m)
        {
            var midpoint = (low + high) / 2m;
            var ratio = Clamp01(midpoint / spend);
            var score = ratio * weight * 100m;

            return new CriticalityComponent(score, weight,
                $"Savings opportunity range [{Fmt(low)}, {Fmt(high)}] midpoint {Fmt(midpoint)} / " +
                $"AnnualSpend {Fmt(spend)} = {Fmt(ratio)} (capped at 1), weighted at {Fmt(weight)}: " +
                $"savings potential {Fmt(score)}.");
        }

        return new CriticalityComponent(0m, weight,
            "No above-band priced-line data and no savings opportunity range are available for " +
            "this contract: savings potential defaults to the minimum (0) rather than guessing " +
            "(Appendix C rule 10).");
    }

    private CriticalityComponent ComputeOpenCriticalFacts(IReadOnlyList<CriticalFactConfidence> criticalFacts)
    {
        var weight = _options.OpenCriticalFacts;

        if (criticalFacts.Count == 0)
        {
            return new CriticalityComponent(0m, weight,
                "No critical facts are tracked for this contract yet: open critical facts defaults " +
                "to the minimum (0) — nothing to validate is not the same as everything validated, " +
                "but this component has no signal either way without a tracked fact.");
        }

        var weakCount = criticalFacts.Count(f => f.Confidence < WeakFactConfidenceThreshold);
        var ratio = (decimal)weakCount / criticalFacts.Count;
        var score = ratio * weight * 100m;

        var explanation =
            $"{weakCount} of {criticalFacts.Count} tracked critical facts are below the " +
            $"{WeakFactConfidenceThreshold:0.0} confidence threshold ({Fmt(ratio)}), weighted at " +
            $"{Fmt(weight)}: open critical facts {Fmt(score)}.";

        if (ratio > ValidateFirstThreshold)
        {
            explanation += " More than half of this contract's critical facts are weak — validate " +
                "first before acting on this contract's numbers.";
        }

        return new CriticalityComponent(score, weight, explanation);
    }

    private static decimal Clamp01(decimal value) => Math.Clamp(value, 0m, 1m);

    /// <summary>Culture-invariant, unpadded decimal formatting for explanation strings — same
    /// convention every other calculator in this codebase already establishes (see
    /// <c>Raffa.Renewals.Application.PriorityScoreCalculator.Fmt</c>).</summary>
    private static string Fmt(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
