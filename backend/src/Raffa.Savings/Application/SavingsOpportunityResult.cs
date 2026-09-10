using Raffa.Savings.Domain;
using Raffa.SharedKernel;

namespace Raffa.Savings.Application;

/// <summary>Outcome of <see cref="SavingsOpportunityService.CreateAsync"/>,
/// <see cref="SavingsOpportunityService.ListAsync"/> or
/// <see cref="SavingsOpportunityService.UpdateAsync"/> — the current, persisted state of one
/// <see cref="SavingsOpportunity"/> (task E04/F02/US02/T01, savings-opportunity).</summary>
/// <param name="RealizedAmount">Task E04/F02/US02/T02 (realized-savings): non-<see langword="null"/>
/// only on the <see cref="SavingsOpportunityService.UpdateAsync"/> call that just recorded it —
/// reflects the <see cref="Domain.RealizedSavings"/> row just written by <em>this</em> call, in the
/// same <see cref="Currency"/> above, never a re-query of this opportunity's full realized-value
/// history (an append-only ledger — see <see cref="Domain.RealizedSavings"/>'s own doc comment);
/// <see langword="null"/> from <see cref="SavingsOpportunityService.CreateAsync"/>/
/// <see cref="SavingsOpportunityService.ListAsync"/> and from any <c>UpdateAsync</c> call that did
/// not itself supply one, even if an earlier call already recorded a realized value for this same
/// opportunity — querying that full history is a follow-up, the same "wiring lands with the first
/// real caller" gap this codebase's other modules already document.</param>
public sealed record SavingsOpportunityResult(
    EntityId Id,
    EntityId? SupplierId,
    EntityId? ContractId,
    string Type,
    decimal CurrentSpend,
    string Currency,
    decimal EstimatedSavingsLow,
    decimal EstimatedSavingsHigh,
    double Confidence,
    SavingsOpportunityStatus Status,
    string? Owner,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    decimal? RealizedAmount = null)
{
    /// <summary>
    /// Task E04/F03/US01/T02 (savings-list, the wave-spec's own artifact of that name) — parent
    /// story us-01-savings-kpis AC-3 ("Returns provenance + confidence, never fabricated
    /// precision"): the qualitative tier <see cref="SavingsProvenanceClassifier.Classify"/>
    /// deterministically derives from <see cref="Confidence"/>, the same High/Medium/Low vocabulary
    /// spec §4.3/§11.3 use so a caller never has to interpret a bare <c>[0, 1]</c> decimal unaided.
    /// Computed fresh on every access, not a stored field or a constructor argument — the same
    /// "can never drift from its one source of truth" shape
    /// <see cref="PriceComparisonResult.Provenance"/> already established for this module's
    /// price-comparison half: <see cref="Confidence"/> is this result's only input, so this
    /// property is always consistent with it and every existing caller/test of this record's
    /// constructor is unaffected.
    ///
    /// <para>
    /// Deliberately does not attempt the fuller <see cref="SavingsProvenance"/> shape (source,
    /// comparison dimensions, sample size, benchmark updated-at): those fields describe a specific
    /// <c>Raffa.Benchmark.Contracts.BenchmarkResult</c> comparison (task E04/F02/US01/T02, the
    /// wave-spec's <c>savings-provenance</c> artifact), and <see cref="Domain.SavingsOpportunity"/>
    /// does not itself capture or persist one today — <see cref="CreateSavingsOpportunityRequest"/>
    /// only ever receives the already-reduced <see cref="Confidence"/> score, never the benchmark it
    /// came from (see that request's own doc comment on why nothing wires a real caller yet).
    /// Reporting a source/sample-size/updated-at this type does not actually have on file would be
    /// exactly the fabricated precision AC-3 forbids (Appendix C rule 10) — the same "never fabricate
    /// a dimension or sample size that is not actually present" discipline
    /// <see cref="SavingsProvenanceClassifier"/>'s own summary builder already follows. A caller that
    /// needs the full <see cref="SavingsProvenance"/> for a live comparison still has it via
    /// <see cref="PriceComparisonResult.Provenance"/> at comparison time; this tier is the one
    /// provenance signal that is always honestly derivable from what a persisted
    /// <see cref="Domain.SavingsOpportunity"/> actually stores.
    /// </para>
    /// </summary>
    public SavingsConfidenceLevel ConfidenceLevel => SavingsProvenanceClassifier.Classify(Confidence);
}
