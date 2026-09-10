namespace Raffa.Renewals.Application;

/// <summary>
/// One named component of a <see cref="PriorityScoreResult"/> — task E03/F01/US02/T01, parent
/// story us-02-priority-score AC-2: "Component scores are stored separately (explainable and
/// tunable)". <see cref="Score"/> is always on the same
/// 0-<see cref="PriorityScoreCalculator.MaxComponentScore"/> scale as every other component, so
/// <see cref="PriorityScoreResult.TotalScore"/> (their sum) is always on the same
/// 0-<see cref="PriorityScoreCalculator.MaxTotalScore"/> scale regardless of which raw inputs were
/// available for a given contract.
/// </summary>
/// <param name="Score">This component's contribution, always
/// 0-<see cref="PriorityScoreCalculator.MaxComponentScore"/> inclusive — never negative, never
/// above the max, so a caller can sum every component without a separate range check.</param>
/// <param name="Explanation">Human-readable trace of how <see cref="Score"/> was derived from the
/// raw input — including the no-data case, so "unknown, defaulted" and "known, computed" stay
/// distinguishable (Appendix C rule 10), the same role
/// <c>RenewalCalculationResult.Explanation</c> plays for <see cref="RenewalEngine"/>.</param>
public sealed record PriorityScoreComponent(decimal Score, string Explanation);
