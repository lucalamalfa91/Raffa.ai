using Raffa.Benchmark.Contracts;
using Raffa.Quotes.Domain;

namespace Raffa.Quotes.Application.Assessment;

/// <summary>
/// Confidence + provenance "on the match" (parent story us-01-market-assessment AC-3, "returns the
/// assessment with confidence/provenance") — mirrors
/// <c>Raffa.Savings.Application.SavingsProvenance</c>'s own shape exactly (same fields, same
/// purpose), duplicated rather than referenced: ADR-002 forbids <c>Raffa.Quotes</c> from
/// referencing <c>Raffa.Savings</c> (see <see cref="Domain.MarketConfidenceLevel"/>'s own doc
/// comment).
/// </summary>
/// <param name="ConfidenceLevel">The qualitative tier spec §11.2's "Benchmark confidence" row
/// shows a user (High/Medium/Low) — see <see cref="MarketAssessmentProvenanceClassifier"/> for the
/// thresholds.</param>
/// <param name="ConfidenceScore">Raffa's own <c>[0, 1]</c> score this tier was computed from —
/// echoes <see cref="BenchmarkResult.Confidence"/> unchanged.</param>
/// <param name="Source">Provider/source identifier (e.g. <c>"fixture"</c> for the first `demo` —
/// ADR-001) — echoes <see cref="BenchmarkResult.Source"/>.</param>
/// <param name="ComparisonDimensions">Which dimensions the match actually used — echoes
/// <see cref="BenchmarkResult.ComparisonDimensions"/>; never just <c>Supplier</c> alone (spec
/// §10.4).</param>
/// <param name="SampleSize">Comparable count behind the result, when the adapter can report one —
/// echoes <see cref="BenchmarkResult.SampleSize"/>.</param>
/// <param name="UpdatedAt">When the underlying comparable data was last refreshed — echoes
/// <see cref="BenchmarkResult.UpdatedAt"/>.</param>
/// <param name="Summary">Deterministic, human-readable one-line trace of every field above — not
/// meant to be shown to an end user as-is, but enough for a test/developer to see why a result has
/// the shape it does without re-deriving it.</param>
public sealed record MarketAssessmentProvenance(
    MarketConfidenceLevel ConfidenceLevel,
    double ConfidenceScore,
    string Source,
    IReadOnlyCollection<BenchmarkComparisonDimension> ComparisonDimensions,
    int? SampleSize,
    DateTimeOffset UpdatedAt,
    string Summary);
