using Raffa.SharedKernel;

namespace Raffa.Insights.Contracts;

/// <summary>
/// The outcome of <c>Raffa.Insights.Criticality.CriticalityScoreCalculator.Calculate</c> (task
/// E13/F07/US01/T01, insights-calculators; parent story us-01-insights AC-1: "returns 0-100 per
/// contract with components..., each with a score and an explanation; components sum to the total").
/// Mirrors <c>Raffa.Renewals.Application.PriorityScoreResult</c>'s own "never a bare number" shape
/// exactly, one bounded-context over: <see cref="TotalScore"/> is never trusted bare — every summand
/// is its own named, explained <see cref="CriticalityComponent"/> field, and
/// <see cref="TotalScore"/> literally equals their sum (AC-1's own "components sum to the total").
/// </summary>
/// <param name="ContractId">Which contract this scores.</param>
/// <param name="TotalScore"><see cref="RenewalUrgency"/> + <see cref="RiskSeverity"/> +
/// <see cref="SpendWeight"/> + <see cref="SavingsPotential"/> + <see cref="OpenCriticalFacts"/>'s own
/// <see cref="CriticalityComponent.Score"/> values — 0-100 when the configured weights
/// (<c>Raffa.Insights.InsightsOptions</c>) sum to 1.0, as validated at construction.</param>
/// <param name="RenewalUrgency">How soon this contract must be acted on (product spec §12.1's
/// "renewal urgency (existing priority score, normalized)").</param>
/// <param name="RiskSeverity">The highest recorded risk severity against this contract.</param>
/// <param name="SpendWeight">This contract's annual-spend share of the portfolio.</param>
/// <param name="SavingsPotential">How much room this contract has to improve pricing.</param>
/// <param name="OpenCriticalFacts">How many of this contract's critical facts are still weak
/// (below-threshold confidence) — action is blocked until they are validated (AC-2: "A contract
/// whose critical facts are all weak is flagged 'validate first'... and its criticality is raised,
/// not hidden").</param>
public sealed record CriticalityScore(
    EntityId ContractId,
    decimal TotalScore,
    CriticalityComponent RenewalUrgency,
    CriticalityComponent RiskSeverity,
    CriticalityComponent SpendWeight,
    CriticalityComponent SavingsPotential,
    CriticalityComponent OpenCriticalFacts);

/// <summary>
/// One named component of a <see cref="CriticalityScore"/> (task E13/F07/US01/T01's own coding
/// objective: "five CriticalityComponent(Score, Weight, Explanation)") — mirrors
/// <c>Raffa.Renewals.Application.PriorityScoreComponent</c>'s own shape, plus <see cref="Weight"/>
/// so a caller can see this component's configured share of the total without re-reading
/// <c>InsightsOptions</c> separately.
/// </summary>
/// <param name="Score">This component's contribution to <see cref="CriticalityScore.TotalScore"/> —
/// always 0-(<see cref="Weight"/> × 100) inclusive, so summing every component's <see cref="Score"/>
/// always lands on the same 0-100 scale regardless of which raw inputs were available.</param>
/// <param name="Weight">This component's configured weight (0-1, from <c>InsightsOptions</c>) — the
/// fraction of the total this component can contribute at most.</param>
/// <param name="Explanation">Human-readable, deterministic trace of how <see cref="Score"/> was
/// derived — including the no-data case, never a silent, unexplained default (Appendix C rule
/// 10).</param>
public sealed record CriticalityComponent(decimal Score, decimal Weight, string Explanation);
