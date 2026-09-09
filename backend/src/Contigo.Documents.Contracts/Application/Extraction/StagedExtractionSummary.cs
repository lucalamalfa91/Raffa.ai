using Contigo.Documents.Contracts.Domain;
using Contigo.SharedKernel;

namespace Contigo.Documents.Contracts.Application.Extraction;

/// <summary>Outcome of one <see cref="StagedExtractionService.RunAsync"/> call: the
/// <see cref="Domain.Contract"/> facts were staged into, the document's resulting
/// <see cref="DocumentProcessingStatus"/>, and each stage's own result (AC-1: every stage runs
/// and reports independently — one stage failing does not hide the others' outcomes).</summary>
/// <param name="AcceptedSupplierName">
/// The supplier's legal name, exactly as the `metadata` stage's <c>supplier</c> fact reported it,
/// when that fact cleared the critical-field bar (requirements R-SUP-01, spec §7.3) —
/// <see langword="null"/> when no supplier fact was returned or when the one returned was too weak
/// to trust. Reported here rather than left for the caller to dig out of
/// <see cref="Domain.ExtractionEvidence"/>, because "was this critical fact accepted?" is a
/// decision <see cref="StagedExtractionService"/> makes and must not be re-derived (differently) by
/// each consumer. <see cref="DocumentProcessingPipeline"/> is the one caller that acts on it: it
/// turns the name into <see cref="Domain.Contract.SupplierId"/> through
/// <see cref="Contigo.SharedKernel.Suppliers.ISupplierResolver"/> (ADR-002 — this module may not
/// reference <c>Contigo.Suppliers.Products</c> itself). A rejected supplier fact still lands in the
/// review list with its evidence, where a human correction re-resolves it
/// (<c>ContractCorrectionService</c>, requirements R-SUP-03).
/// </param>
public sealed record StagedExtractionSummary(
    EntityId ContractId,
    DocumentProcessingStatus DocumentProcessingStatus,
    IReadOnlyList<StagedExtractionStageResult> Stages,
    string? AcceptedSupplierName = null);

/// <summary>Result of running a single <see cref="ExtractionStage"/> (one <see cref="ExtractionJob"/>
/// row). <see cref="ExtractedCount"/> is how many facts/items were persisted;
/// <see cref="SkippedCount"/> is how many the model returned but could not be persisted (missing
/// a required field, an unparseable enum/date) — a non-zero <see cref="SkippedCount"/> or a
/// <see cref="Status"/> of <see cref="ExtractionJobStatus.NeedsReview"/>/
/// <see cref="ExtractionJobStatus.Failed"/> means a human should look at this stage
/// (product principle: "Human-in-the-loop for consequential decisions... low-confidence
/// extraction... must be reviewable").</summary>
public sealed record StagedExtractionStageResult(
    ExtractionStage Stage,
    ExtractionJobStatus Status,
    int ExtractedCount,
    int SkippedCount,
    string? ErrorDetail);
