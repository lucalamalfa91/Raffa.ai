using System.Globalization;
using Raffa.Renewals.Configuration;
using Raffa.Renewals.Domain;

namespace Raffa.Renewals.Application;

/// <summary>
/// The explainable, tunable renewal-priority calculator (task E03/F01/US02/T01 added the formula
/// and its component breakdown; task E03/F01/US02/T02 — the wave-spec's
/// <c>renewal-priority-explain</c> artifact — made every component's weight configurable, see
/// <see cref="PriorityScoreWeightsOptions"/>). Parent story us-02-priority-score depends on this
/// module's own <c>renewal-engine</c> artifact — <see cref="RenewalEngine"/> — for "days until
/// renewal". Pure and synchronous, the same determinism convention as <see cref="RenewalEngine"/>
/// (Appendix C rule 6): the same <c>RenewalCalculationResult</c> plus the same
/// <see cref="RenewalPriorityInputs"/> plus the same configured
/// <see cref="PriorityScoreWeightsOptions"/> always produce the same
/// <see cref="PriorityScoreResult"/> — no database call, no HTTP call, no LLM call anywhere in
/// <see cref="Calculate"/>.
///
/// <para>
/// Product spec §9.2: <c>"Priority Score = Spend Weight + Time Urgency + Benchmark Opportunity +
/// Price Increase Risk + Contract Risk"</c> (parent story AC-1 names "Price Increase Risk" as
/// "uplift risk" — both name the same spec §9.3 "Annual uplift" fact). Spec §9.2 also requires:
/// "Store both total score and component scores so the recommendation is explainable and tunable"
/// — AC-2, made concrete by <see cref="PriorityScoreComponent"/> being its own named field per
/// component rather than a single opaque number (explainable), and every component's maximum
/// contribution being an injectable <see cref="PriorityScoreWeightsOptions"/> value rather than a
/// compile-time literal (tunable).
/// </para>
///
/// <para>
/// Every component is scored 0-its own configured maximum (<see cref="PriorityScoreWeightsOptions"/>;
/// <see cref="MaxComponentScore"/>, 20, is every property's own spec-default value, so an
/// unconfigured deployment reproduces the exact numbers this class always produced);
/// <see cref="PriorityScoreResult.TotalScore"/> is their sum, 0-<see cref="MaxTotalScore"/> under
/// the spec defaults (a tuned instance's true achievable maximum is instead the sum of its five
/// configured weights). A component whose raw input is unknown never fabricates a guess (Appendix
/// C rule 10): it defaults to the documented no-data value — the conservative minimum (0) for
/// spend/time-urgency/price-increase-risk/contract-risk (an unknown fact must not inflate
/// priority), or the explicit midpoint of its own configured maximum for benchmark opportunity
/// specifically (<see cref="NeutralComponentScore"/> under the spec default), because parent story
/// AC-3 names that exact rule ("reads the R3 benchmark only when available (else neutral)") —
/// "neutral" is deliberately different from "minimum": an unknown market position is not evidence
/// of a bad deal (which would argue for the minimum) any more than it is evidence of a great one
/// (which would argue for the maximum).
/// </para>
///
/// <para>
/// "Tunable" (AC-2) means each of the five named terms' own <em>maximum contribution</em> to the
/// weighted sum is configurable — product spec §9.2 literally calls the first term a "Weight". It
/// deliberately does <em>not</em> mean every tier boundary (spend/uplift-percent/benchmark-percent
/// cut points, the time-urgency day windows) is independently configurable — those stay this
/// class's own product-spec-cited defaults (task E03/F01/US02/T01's own "not a re-derivation of the
/// formula itself" instruction for this follow-up task). Every tier's fixed contribution is instead
/// rescaled <em>proportionally</em>: each literal this class used to hard-code was already some
/// exact fraction of the spec-default maximum (20) — 100%/85%/75%/60%/45%/30%/15%/0% for time
/// urgency, for example — and that same fraction, times the configured maximum, reproduces the
/// original literal exactly when nothing is configured (decimal arithmetic on these fractions is
/// exact: every one has only 2 and 5 as prime factors in its denominator, so it terminates in base
/// 10 with no rounding) and scales proportionally when something is.
/// </para>
/// </summary>
public sealed class PriorityScoreCalculator(PriorityScoreWeightsOptions? weights = null)
{
    /// <summary>Spec-default maximum score any single <see cref="PriorityScoreComponent"/> can
    /// contribute — also every <see cref="PriorityScoreWeightsOptions"/> property's own default
    /// value (see that class's own doc comment), so an unconfigured deployment reproduces this
    /// exact number.</summary>
    public const decimal MaxComponentScore = 20m;

    /// <summary>Spec-default maximum possible <see cref="PriorityScoreResult.TotalScore"/> — five
    /// components at <see cref="MaxComponentScore"/> each (product spec §9.2's five named terms)
    /// under the spec-default weights. A tuned instance's true achievable maximum is instead the
    /// sum of its own <see cref="PriorityScoreWeightsOptions"/> properties.</summary>
    public const decimal MaxTotalScore = MaxComponentScore * 5m;

    /// <summary>The spec-default "no data" value for the benchmark-opportunity component only
    /// (parent story AC-3) — the midpoint of 0-<see cref="MaxComponentScore"/>, deliberately
    /// distinct from every other component's no-data value (the minimum, 0 — see this class's own
    /// doc comment for why). A tuned instance instead uses half of its own configured
    /// <see cref="PriorityScoreWeightsOptions.BenchmarkOpportunityMax"/> — see
    /// <see cref="ComputeBenchmarkOpportunity"/>.</summary>
    public const decimal NeutralComponentScore = MaxComponentScore / 2m;

    /// <summary>This instance's configured per-component weights (task E03/F01/US02/T02).
    /// <paramref name="weights"/> is only ever <see langword="null"/> when a caller constructs this
    /// class directly without DI and without supplying its own options (every existing
    /// <c>Raffa.Renewals.Tests.PriorityScoreCalculatorTests</c> case) — resolved to the
    /// spec-default <see cref="PriorityScoreWeightsOptions"/> right here so every
    /// <c>Compute*</c> method below can read this field unconditionally, the same "always-usable
    /// default" convention <see cref="PriorityScoreWeightsOptions"/>'s own doc comment describes
    /// for its own properties. DI never actually relies on this fallback in a real host — it
    /// resolves the singleton <see cref="PriorityScoreWeightsOptions"/>
    /// <c>Raffa.Renewals.Infrastructure.ServiceCollectionExtensions.AddRenewalsModule</c>
    /// registers (spec defaults unless a <c>Renewals:PriorityWeights</c> configuration section
    /// overrides them) — the parameter default only matters for a caller that never goes through
    /// the container at all.</summary>
    private readonly PriorityScoreWeightsOptions _weights = weights ?? new PriorityScoreWeightsOptions();

    /// <summary>
    /// Computes <paramref name="inputs"/>'s five priority-score components and their total for the
    /// contract <paramref name="renewal"/> already describes (relative to
    /// <see cref="RenewalCalculationResult.DaysUntilRenewal"/> — no second "days until renewal"
    /// computation happens here; see this class's own doc comment for why), each component scaled
    /// by this instance's <see cref="PriorityScoreWeightsOptions"/>.
    /// </summary>
    public PriorityScoreResult Calculate(RenewalCalculationResult renewal, RenewalPriorityInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(renewal);
        ArgumentNullException.ThrowIfNull(inputs);

        var spendWeight = ComputeSpendWeight(inputs.AnnualSpend);
        var timeUrgency = ComputeTimeUrgency(renewal.DaysUntilRenewal);
        var benchmarkOpportunity = ComputeBenchmarkOpportunity(inputs.BenchmarkMarketPositionPercent);
        var priceIncreaseRisk = ComputePriceIncreaseRisk(inputs.AnnualUpliftPercent);
        var contractRisk = ComputeContractRisk(inputs.ContractRisk);

        var total = spendWeight.Score + timeUrgency.Score + benchmarkOpportunity.Score +
            priceIncreaseRisk.Score + contractRisk.Score;

        return new PriorityScoreResult(
            renewal.ContractId,
            total,
            spendWeight,
            timeUrgency,
            benchmarkOpportunity,
            priceIncreaseRisk,
            contractRisk);
    }

    /// <summary>Convenience batch form of <see cref="Calculate"/>, mirroring
    /// <see cref="RenewalEngine.CalculateMany"/> for product spec §9.1's "daily scheduler for each
    /// active contract" shape — one result per <paramref name="contracts"/> entry, in the same
    /// order, no aggregation.</summary>
    public IReadOnlyList<PriorityScoreResult> CalculateMany(
        IEnumerable<(RenewalCalculationResult Renewal, RenewalPriorityInputs Inputs)> contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        return contracts.Select(c => Calculate(c.Renewal, c.Inputs)).ToList();
    }

    private PriorityScoreComponent ComputeSpendWeight(decimal? annualSpend)
    {
        var max = _weights.SpendWeightMax;

        if (annualSpend is null)
        {
            return new PriorityScoreComponent(0m,
                "AnnualSpend is unknown: spend weight defaults to the minimum (0) rather than " +
                "guessing (Appendix C rule 10).");
        }

        var spend = annualSpend.Value;
        if (spend <= 0m)
        {
            return new PriorityScoreComponent(0m,
                $"AnnualSpend ({Fmt(spend)}) is not a positive amount: spend weight defaults to " +
                "the minimum (0).");
        }

        // Every fraction below is the exact original spec-default literal (20/16/12/8/4) divided
        // by 20 — see the class doc comment for why that reproduces today's numbers exactly when
        // max is the spec default, and scales proportionally otherwise.
        return spend switch
        {
            >= 500_000m => new PriorityScoreComponent(max,
                $"AnnualSpend ({Fmt(spend)}) is 500,000 or more: maximum spend weight ({Fmt(max)})."),
            >= 250_000m => new PriorityScoreComponent(max * 0.8m,
                $"AnnualSpend ({Fmt(spend)}) is 250,000 or more: spend weight ({Fmt(max * 0.8m)})."),
            >= 100_000m => new PriorityScoreComponent(max * 0.6m,
                $"AnnualSpend ({Fmt(spend)}) is 100,000 or more: spend weight ({Fmt(max * 0.6m)})."),
            >= 25_000m => new PriorityScoreComponent(max * 0.4m,
                $"AnnualSpend ({Fmt(spend)}) is 25,000 or more: spend weight ({Fmt(max * 0.4m)})."),
            _ => new PriorityScoreComponent(max * 0.2m,
                $"AnnualSpend ({Fmt(spend)}) is below 25,000: spend weight ({Fmt(max * 0.2m)})."),
        };
    }

    private PriorityScoreComponent ComputeTimeUrgency(int? daysUntilRenewal)
    {
        var max = _weights.TimeUrgencyMax;

        if (daysUntilRenewal is null)
        {
            return new PriorityScoreComponent(0m,
                "No determined renewal date (RenewalCalculationResult.DaysUntilRenewal is null): " +
                "time urgency defaults to the minimum (0) rather than guessing (Appendix C rule 10).");
        }

        var days = daysUntilRenewal.Value;

        // Tiered at product spec §9.1's own default renewal threshold windows
        // (365/270/180/120/90/60/30 days) so this component's explanation lines up with the same
        // windows the cancellation-alerts threshold-scheduler (task E03/F02/US01/T01) alerts on.
        // Day-count boundaries are the product-spec-cited default and stay fixed (this task's own
        // "not a re-derivation of the formula" scope — see the class doc comment); only each
        // tier's contribution — a fraction of TimeUrgencyMax — is configurable.
        return days switch
        {
            < 0 => new PriorityScoreComponent(max,
                $"Renewal is {-days} day(s) overdue: maximum time urgency ({Fmt(max)})."),
            <= 30 => new PriorityScoreComponent(max,
                $"{days} day(s) until renewal, within the 30-day window: maximum time urgency ({Fmt(max)})."),
            <= 60 => new PriorityScoreComponent(max * 0.85m,
                $"{days} day(s) until renewal, within the 60-day window: time urgency ({Fmt(max * 0.85m)})."),
            <= 90 => new PriorityScoreComponent(max * 0.75m,
                $"{days} day(s) until renewal, within the 90-day window: time urgency ({Fmt(max * 0.75m)})."),
            <= 120 => new PriorityScoreComponent(max * 0.6m,
                $"{days} day(s) until renewal, within the 120-day window: time urgency ({Fmt(max * 0.6m)})."),
            <= 180 => new PriorityScoreComponent(max * 0.45m,
                $"{days} day(s) until renewal, within the 180-day window: time urgency ({Fmt(max * 0.45m)})."),
            <= 270 => new PriorityScoreComponent(max * 0.3m,
                $"{days} day(s) until renewal, within the 270-day window: time urgency ({Fmt(max * 0.3m)})."),
            <= 365 => new PriorityScoreComponent(max * 0.15m,
                $"{days} day(s) until renewal, within the 365-day window: time urgency ({Fmt(max * 0.15m)})."),
            _ => new PriorityScoreComponent(0m,
                $"{days} day(s) until renewal, beyond the 365-day window: minimum time urgency (0)."),
        };
    }

    private PriorityScoreComponent ComputeBenchmarkOpportunity(decimal? marketPositionPercent)
    {
        var max = _weights.BenchmarkOpportunityMax;
        var neutral = max / 2m;

        if (marketPositionPercent is null)
        {
            return new PriorityScoreComponent(neutral,
                "R3 benchmark data is not available for this contract (Raffa.Benchmark." +
                "IBenchmarkService has no query operations yet): benchmark opportunity defaults " +
                $"to neutral ({Fmt(neutral)}) rather than assuming an opportunity " +
                "or its absence (parent story AC-3).");
        }

        var position = marketPositionPercent.Value;

        // >= ladder: "position" is how far ABOVE market the contract's spend sits (spec §9.3's own
        // "18% above benchmark" example) — the higher above market, the bigger the opportunity to
        // negotiate a saving. A position at or below -20% (well below market) is the smallest
        // opportunity, not a data gap, so it still lands at the minimum rather than neutral.
        return position switch
        {
            >= 20m => new PriorityScoreComponent(max,
                $"Spend is {Fmt(position)}% above the R3 benchmark market rate: maximum benchmark " +
                $"opportunity ({Fmt(max)})."),
            >= 5m => new PriorityScoreComponent(max * 0.75m,
                $"Spend is {Fmt(position)}% above the R3 benchmark market rate: benchmark " +
                $"opportunity ({Fmt(max * 0.75m)})."),
            >= -5m => new PriorityScoreComponent(neutral,
                $"Spend is within 5% of the R3 benchmark market rate ({Fmt(position)}%): neutral " +
                $"benchmark opportunity ({Fmt(neutral)})."),
            >= -20m => new PriorityScoreComponent(max * 0.25m,
                $"Spend is {Fmt(-position)}% below the R3 benchmark market rate: benchmark " +
                $"opportunity ({Fmt(max * 0.25m)})."),
            _ => new PriorityScoreComponent(0m,
                $"Spend is {Fmt(-position)}% below the R3 benchmark market rate: minimum " +
                "benchmark opportunity (0)."),
        };
    }

    private PriorityScoreComponent ComputePriceIncreaseRisk(decimal? annualUpliftPercent)
    {
        var max = _weights.PriceIncreaseRiskMax;

        if (annualUpliftPercent is null)
        {
            return new PriorityScoreComponent(0m,
                "AnnualUpliftPercent is unknown: price-increase risk defaults to the minimum (0) " +
                "rather than guessing (Appendix C rule 10).");
        }

        var uplift = annualUpliftPercent.Value;
        if (uplift <= 0m)
        {
            return new PriorityScoreComponent(0m,
                $"AnnualUpliftPercent ({Fmt(uplift)}%) is zero or a decrease: no price-increase " +
                "risk (0).");
        }

        return uplift switch
        {
            >= 20m => new PriorityScoreComponent(max,
                $"AnnualUpliftPercent ({Fmt(uplift)}%) is 20% or more: maximum price-increase " +
                $"risk ({Fmt(max)})."),
            >= 12m => new PriorityScoreComponent(max * 0.9m,
                $"AnnualUpliftPercent ({Fmt(uplift)}%) is 12% or more: price-increase risk " +
                $"({Fmt(max * 0.9m)})."),
            >= 7m => new PriorityScoreComponent(max * 0.7m,
                $"AnnualUpliftPercent ({Fmt(uplift)}%) is 7% or more: price-increase risk " +
                $"({Fmt(max * 0.7m)})."),
            >= 3m => new PriorityScoreComponent(max * 0.45m,
                $"AnnualUpliftPercent ({Fmt(uplift)}%) is 3% or more: price-increase risk " +
                $"({Fmt(max * 0.45m)})."),
            _ => new PriorityScoreComponent(max * 0.2m,
                $"AnnualUpliftPercent ({Fmt(uplift)}%) is above 0% but below 3%: price-increase " +
                $"risk ({Fmt(max * 0.2m)})."),
        };
    }

    private PriorityScoreComponent ComputeContractRisk(ContractRiskLevel? riskLevel)
    {
        var max = _weights.ContractRiskMax;

        if (riskLevel is null)
        {
            return new PriorityScoreComponent(0m,
                "No ContractRiskLevel has been assessed for this contract: contract risk " +
                "defaults to the minimum (0) rather than assuming risk (Appendix C rule 10).");
        }

        return riskLevel.Value switch
        {
            ContractRiskLevel.Low => new PriorityScoreComponent(max * 0.15m,
                $"ContractRisk is Low: contract risk ({Fmt(max * 0.15m)})."),
            ContractRiskLevel.Medium => new PriorityScoreComponent(max * 0.45m,
                $"ContractRisk is Medium: contract risk ({Fmt(max * 0.45m)})."),
            ContractRiskLevel.High => new PriorityScoreComponent(max * 0.75m,
                $"ContractRisk is High: contract risk ({Fmt(max * 0.75m)})."),
            ContractRiskLevel.Critical => new PriorityScoreComponent(max,
                $"ContractRisk is Critical: maximum contract risk ({Fmt(max)})."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(riskLevel), riskLevel.Value, "Unknown ContractRiskLevel."),
        };
    }

    /// <summary>Culture-invariant, unpadded decimal formatting for explanation strings — so
    /// <see cref="PriorityScoreComponent.Explanation"/> text (and any test asserting against it) is
    /// stable regardless of the running culture (no thousands separator or currency symbol, which
    /// this codebase does not model on <c>Contract.AnnualSpend</c> anywhere).</summary>
    private static string Fmt(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
