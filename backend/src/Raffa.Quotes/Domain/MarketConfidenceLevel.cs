namespace Raffa.Quotes.Domain;

/// <summary>
/// A qualitative confidence tier for a market assessment's benchmark provenance — the product
/// spec's own UI convention of showing "Benchmark confidence: High / Medium / Low" (spec §11.2
/// "Assessment output" table's own "Benchmark confidence | High / Medium / Low" row) rather than a
/// bare decimal a user cannot interpret unaided. Computed deterministically from
/// <c>Raffa.Benchmark.Contracts.BenchmarkResult.Confidence</c> by
/// <c>Raffa.Quotes.Application.Assessment.MarketAssessmentProvenanceClassifier</c> — see that
/// type's own doc comments for the exact thresholds. Mirrors
/// <c>Raffa.Savings.Domain.SavingsConfidenceLevel</c> exactly (same enum shape, same thresholds,
/// deliberately duplicated rather than shared: ADR-002 forbids <c>Raffa.Quotes</c> from
/// referencing <c>Raffa.Savings</c> — its own allowed Raffa references are exactly
/// <c>[SharedKernel, Benchmark]</c> — the same "each module owns its own copy of a small,
/// module-local classification" pattern <c>SavingsConfidenceLevel</c>'s own doc comment already
/// documents for <c>Raffa.Renewals.Domain.ContractRiskLevel</c>).
/// </summary>
public enum MarketConfidenceLevel
{
    Low,
    Medium,
    High,
}
