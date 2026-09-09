namespace Contigo.Insights;

/// <summary>
/// Configurable per-component weight for <c>Contigo.Insights.Criticality.CriticalityScoreCalculator</c>
/// (task E13/F07/US01/T01, insights-calculators; parent story us-01-insights AC-1: "weights come
/// from configuration"; story's own "Council decisions carried into this story": <c>Insights:Criticality:
/// {RenewalUrgency=0.30, RiskSeverity=0.20, SpendWeight=0.20, SavingsPotential=0.20,
/// OpenCriticalFacts=0.10}</c>, sum 1.0). Same "always-usable default, config only overlays what is
/// present" convention <c>Contigo.Renewals.Configuration.PriorityScoreWeightsOptions</c> already
/// establishes — a deployment with no <see cref="SectionName"/> section configured gets exactly the
/// council-decided defaults below, which already sum to 1.0.
/// </summary>
public sealed class InsightsOptions
{
    /// <summary>Conventional configuration section path for binding this options object.</summary>
    public const string SectionName = "Insights:Criticality";

    /// <summary>Weight of the renewal-urgency component (product spec §12.1 "renewal urgency").</summary>
    public decimal RenewalUrgency { get; init; } = 0.30m;

    /// <summary>Weight of the risk-severity component (highest recorded <c>RiskSeverity</c>).</summary>
    public decimal RiskSeverity { get; init; } = 0.20m;

    /// <summary>Weight of the spend-weight component (annual spend share of the portfolio).</summary>
    public decimal SpendWeight { get; init; } = 0.20m;

    /// <summary>Weight of the savings-potential component (above-band lines, or the savings
    /// opportunity range).</summary>
    public decimal SavingsPotential { get; init; } = 0.20m;

    /// <summary>Weight of the open-critical-facts component (weak fields on critical fields raise
    /// criticality — action is blocked until they are validated).</summary>
    public decimal OpenCriticalFacts { get; init; } = 0.10m;
}
