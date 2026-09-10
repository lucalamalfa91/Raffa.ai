namespace Raffa.Renewals.Configuration;

/// <summary>
/// Configurable per-component maximum contribution ("weight") for
/// <c>Raffa.Renewals.Application.PriorityScoreCalculator</c> (task E03/F01/US02/T02, the
/// wave-spec's <c>renewal-priority-explain</c> artifact; parent story us-02-priority-score AC-2:
/// "Component scores are stored separately (explainable and tunable)"; product spec §9.2 "Store
/// both total score and component scores so the recommendation is explainable and tunable"). Task
/// E03/F01/US02/T01's own doc comment on <c>PriorityScoreCalculator</c> named this explicitly as
/// this follow-up task's job: "task-02 (priority-explainability) is where they become tunable
/// configuration, not a re-derivation of the formula."
///
/// <para>
/// Each property is one named component's own maximum score — product spec §9.2 names five
/// summands of a weighted sum ("Spend Weight", "Time Urgency", "Benchmark Opportunity", "Price
/// Increase Risk", "Contract Risk"), so "tunable weights" here means the relative importance
/// (maximum contribution) of each named term, not a re-derivation of the tiering logic that
/// decides which tier a contract falls into for that term (T01's own "not a re-derivation of the
/// formula" instruction) — the day-count/spend/uplift-percent/risk-level tier boundaries stay the
/// product-spec-cited defaults <c>PriorityScoreCalculator</c> already documents.
/// <c>PriorityScoreCalculator</c> rescales every tier's fixed contribution proportionally (the
/// tier's fraction of the spec-default maximum, 20, times the configured maximum here) — see that
/// class's own doc comment for the worked rationale and the exact fractions.
/// </para>
///
/// <para>
/// Every property defaults to <c>20</c> — <c>PriorityScoreCalculator.MaxComponentScore</c>, the
/// spec default — so an operator who configures none of this section gets exactly the numbers this
/// codebase always produced; every existing
/// <c>Raffa.Renewals.Tests.PriorityScoreCalculatorTests</c> assertion (written against the
/// 0-20 scale) still holds. A composition root binds this from configuration the same
/// "always-usable default, config only overlays what is present" convention
/// <see cref="ThresholdWindowOptions"/> and
/// <c>Raffa.AiGateway.Configuration.AiGatewayModelOptions</c> already use — see
/// <c>Raffa.Renewals.Infrastructure.ServiceCollectionExtensions.AddRenewalsModule</c>. Unlike
/// <see cref="ThresholdWindowOptions.DaysBeforeDeadline"/>, every property here is a scalar
/// <c>decimal</c>, not an array, so a plain <c>configuration.GetSection(SectionName).Bind(options)</c>
/// call is safe as-is — the array-merge binder footgun <see cref="ThresholdWindowOptions"/>'s own
/// doc comment documents does not apply to scalar properties.
/// </para>
/// </summary>
public sealed class PriorityScoreWeightsOptions
{
    /// <summary>Conventional configuration section path for binding this options object.</summary>
    public const string SectionName = "Renewals:PriorityWeights";

    /// <summary>Maximum contribution of the spend-weight component (product spec §9.2 "Spend
    /// Weight") — higher annual spend means more financial exposure is at stake.</summary>
    public decimal SpendWeightMax { get; init; } = 20m;

    /// <summary>Maximum contribution of the time-urgency component (product spec §9.2 "Time
    /// Urgency") — fewer days until renewal (or an overdue renewal) means higher priority.</summary>
    public decimal TimeUrgencyMax { get; init; } = 20m;

    /// <summary>Maximum contribution of the benchmark-opportunity component (product spec §9.2
    /// "Benchmark Opportunity") — how far above market the contract's spend sits. Also determines
    /// this component's neutral (no-benchmark-data) value: half of this maximum, the same
    /// relationship <c>PriorityScoreCalculator.NeutralComponentScore</c> documents for the spec
    /// default (parent story AC-3, "reads the R3 benchmark only when available (else
    /// neutral)").</summary>
    public decimal BenchmarkOpportunityMax { get; init; } = 20m;

    /// <summary>Maximum contribution of the price-increase-risk component (product spec §9.2
    /// "Price Increase Risk", parent story AC-1's "uplift risk" — the same fact) — higher
    /// renewal-time price increase means higher priority to review before it takes effect.</summary>
    public decimal PriceIncreaseRiskMax { get; init; } = 20m;

    /// <summary>Maximum contribution of the contract-risk component (product spec §9.2 "Contract
    /// Risk") — higher assessed contract risk (liability, compliance, etc.) means higher
    /// priority.</summary>
    public decimal ContractRiskMax { get; init; } = 20m;
}
