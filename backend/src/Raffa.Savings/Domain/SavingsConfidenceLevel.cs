namespace Raffa.Savings.Domain;

/// <summary>
/// A qualitative confidence tier for a savings comparison's benchmark provenance — the product
/// spec's own UI convention of showing "Benchmark confidence: High / Medium / Low" rather than a
/// bare decimal a user cannot interpret unaided (spec §4.3 "Show benchmark confidence and
/// provenance"; §11.3 "Benchmark trust ... expose benchmark confidence and comparability"). Computed
/// deterministically from <c>Raffa.Benchmark.Contracts.BenchmarkResult.Confidence</c> by
/// <see cref="Raffa.Savings.Application.SavingsProvenanceClassifier"/> — see that type's own doc
/// comments for the exact thresholds and why they are this module's own documented, adjustable
/// heuristic (spec §10.3's confidence field is explicitly "Raffa's own score", not a
/// provider-defined one), the same "mirror a level enum locally rather than reference another
/// module's" shape decision <c>Raffa.Renewals.Domain.ContractRiskLevel</c> already made for this
/// codebase (ADR-002's dependency-direction rule — <c>Raffa.Savings</c> may reference only
/// <c>Raffa.SharedKernel</c> and <c>Raffa.Benchmark</c>).
/// </summary>
public enum SavingsConfidenceLevel
{
    Low,
    Medium,
    High,
}
