using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Outcome of one <see cref="DocumentProcessingPipeline.ProcessAsync"/> call: what the classify
/// stage decided, what the hybrid parse produced, what staged extraction persisted, and how much
/// of it is now retrievable for Ask Raffa. Deliberately mirrors <see cref="StagedExtractionSummary"/>'s
/// own "one record proves the whole call" shape — task E02/F06/US01/T01 (r1-integration) is the
/// caller that finally threads <see cref="StagedExtractionSummary"/> together with the classify and
/// indexing steps neither <c>HybridDocumentParsingService</c> nor <c>StagedExtractionService</c> own
/// (see <see cref="DocumentProcessingPipeline"/>'s own doc comment).
/// </summary>
/// <param name="DocumentId">The processed <see cref="Document"/>.</param>
/// <param name="ContractId">The <see cref="Contract"/> staged extraction extracted into (created if none existed yet).</param>
/// <param name="DocumentType">The classify stage's resulting <see cref="ContractDocumentType"/> (unchanged, i.e. still the pre-classification default, when classification itself failed).</param>
/// <param name="ClassificationConfidence">The classify role's own confidence, or <see langword="null"/> when classification failed and nothing ran.</param>
/// <param name="ProcessingStatus">The document's final <see cref="DocumentProcessingStatus"/> after every staged-extraction stage ran (<see cref="StagedExtractionSummary.DocumentProcessingStatus"/>, passed through unchanged).</param>
/// <param name="PagesParsed">How many pages the hybrid parse step (native or OCR) produced.</param>
/// <param name="ChunksIndexed">How many of those pages were successfully embedded into the `embedding` table for Ask Raffa retrieval (spec §8.3) — may be less than <paramref name="PagesParsed"/> when a page was blank or one embed call failed; see <see cref="DocumentProcessingPipeline"/>'s own remarks on why that degrades this count, not the whole call.</param>
public sealed record DocumentProcessingSummary(
    EntityId DocumentId,
    EntityId ContractId,
    ContractDocumentType DocumentType,
    double? ClassificationConfidence,
    DocumentProcessingStatus ProcessingStatus,
    int PagesParsed,
    int ChunksIndexed);
