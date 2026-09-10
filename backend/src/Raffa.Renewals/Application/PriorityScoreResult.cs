using Raffa.SharedKernel;

namespace Raffa.Renewals.Application;

/// <summary>
/// The outcome of <see cref="PriorityScoreCalculator.Calculate"/> — task E03/F01/US02/T01, parent
/// story us-02-priority-score AC-1 ("Score = spend weight + time urgency + benchmark opportunity +
/// uplift risk + contract risk") and AC-2 ("Component scores are stored separately") made concrete
/// and testable: <see cref="TotalScore"/> is never a bare number a caller has to trust — every
/// summand is its own named, explained <see cref="PriorityScoreComponent"/> field.
/// </summary>
/// <param name="ContractId">Echoes the source <c>RenewalCalculationResult.ContractId</c>
/// unchanged — this result is always computed from one <c>RenewalCalculationResult</c> plus one
/// <see cref="RenewalPriorityInputs"/> for the same contract.</param>
/// <param name="TotalScore"><see cref="SpendWeight"/> + <see cref="TimeUrgency"/> +
/// <see cref="BenchmarkOpportunity"/> + <see cref="PriceIncreaseRisk"/> + <see cref="ContractRisk"/>'s
/// <see cref="PriorityScoreComponent.Score"/> values (product spec §9.2's formula, AC-1) — always
/// 0-<see cref="PriorityScoreCalculator.MaxTotalScore"/> inclusive.</param>
/// <param name="SpendWeight">Higher annual spend -&gt; higher priority to manage the renewal well
/// (more financial exposure is at stake).</param>
/// <param name="TimeUrgency">Fewer days until renewal (or an overdue renewal) -&gt; higher
/// priority, tiered at product spec §9.1's own default threshold windows
/// (365/270/180/120/90/60/30 days).</param>
/// <param name="BenchmarkOpportunity">How far above market the contract's spend sits -&gt; higher
/// priority to negotiate; neutral when the R3 Benchmark Service has not produced a comparison for
/// this contract yet (parent story AC-3).</param>
/// <param name="PriceIncreaseRisk">Higher renewal-time price increase ("uplift", spec §9.2's
/// "Price Increase Risk") -&gt; higher priority to review before the increase takes effect.</param>
/// <param name="ContractRisk">Higher assessed contract risk (liability, compliance, etc.) -&gt;
/// higher priority.</param>
public sealed record PriorityScoreResult(
    EntityId ContractId,
    decimal TotalScore,
    PriorityScoreComponent SpendWeight,
    PriorityScoreComponent TimeUrgency,
    PriorityScoreComponent BenchmarkOpportunity,
    PriorityScoreComponent PriceIncreaseRisk,
    PriorityScoreComponent ContractRisk);
