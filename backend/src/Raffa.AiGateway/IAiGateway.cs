using Raffa.AiGateway.Contracts;
using Raffa.SharedKernel;

namespace Raffa.AiGateway;

/// <summary>
/// AI Gateway interface consumed by domain modules (Documents/Contracts, Chat). Domain modules
/// depend on this abstraction; the implementation behind it is the only place that touches
/// Foundry / Document Intelligence SDKs (module-map "AI Gateway" row;
/// us-01-ai-gateway-classification AC-3). ADR-004: each role is bound to a
/// configuration-selected model id — see
/// <see cref="Raffa.AiGateway.Configuration.AiGatewayModelOptions"/> — so a model swap is a
/// config change, never a code change.
///
/// Task E02/F01/US01/T01 implemented four of ADR-004's five roles: <see cref="ClassifyAsync"/>,
/// <see cref="ExtractAsync"/>, <see cref="EmbedAsync"/>, <see cref="AnswerAsync"/> — exactly the
/// set named by that task's own "Coding objective". Task E02/F01/US02/T02 ("hybrid-ocr") adds the
/// fifth, <see cref="OcrAsync"/> (ADR-017), whose request/result shape is a full-document page map
/// (ADR-017 "Implications for the decomposition": "must persist a page map so evidence
/// source.page / section still resolve") rather than the free-text/JSON shapes the other four
/// roles use — <see cref="OcrAsync"/> is the one role whose entire job is turning raw bytes into
/// text, so it is also the one role whose input is bytes, not a string.
///
/// Every method returns <see cref="Result{T}"/> (this codebase's convention for expected
/// failures — e.g. <c>Raffa.Documents.Contracts.Application.DocumentUploadService</c>) rather
/// than throwing, and every successful result carries an
/// <see cref="Contracts.AiCallMetadata"/> record (model id/version, prompt version, timestamp,
/// input hash) so callers — and task E02/F01/US01/T02's logging — always have brief §8's
/// reproducibility fields available without recomputing them.
/// </summary>
public interface IAiGateway
{
    /// <summary>
    /// `classify` role (ADR-004): recognizes the document type from its text
    /// (us-01-ai-gateway-classification AC-1/AC-2). See <see cref="AiDocumentType"/> for the
    /// fixed taxonomy.
    /// </summary>
    Task<Result<AiClassificationResult>> ClassifyAsync(
        AiClassificationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// `extract` role (ADR-004): schema-constrained structured extraction for one bounded
    /// extraction stage (product spec §7.2/§7.3 — "avoid one giant prompt"). The gateway does not
    /// know or validate the domain schema — it returns whatever JSON the model produced against
    /// the caller-supplied <see cref="AiExtractionRequest.JsonSchema"/>, unparsed, so it stays
    /// reusable across every stage (metadata, commercial terms, dates, line items, clauses,
    /// obligations, risk) without a stage-specific overload.
    /// </summary>
    Task<Result<AiExtractionResult>> ExtractAsync(
        AiExtractionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// `embed` role (ADR-004): produces a fixed-dimension vector
    /// (<see cref="AiGatewayConstants.EmbeddingDimensions"/>) for pgvector semantic search / Ask
    /// Raffa RAG (spec §8.3).
    /// </summary>
    Task<Result<AiEmbeddingResult>> EmbedAsync(
        AiEmbeddingRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// `answer` role (ADR-004): grounded Q&amp;A over caller-supplied, already-authorized
    /// evidence (ADR-011 "authorization before retrieval" — this gateway never retrieves; the
    /// caller resolves tenant/role/object authz and runs retrieval first). Returns citations or
    /// an explicit "cannot determine" (<see cref="AiAnswerResult.CanDetermine"/>) rather than
    /// fabricating (spec §8.4 "no evidence, no claim"; Appendix C rule 10).
    /// </summary>
    Task<Result<AiAnswerResult>> AnswerAsync(
        AiAnswerRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// `ocr` role (ADR-017): the hybrid OCR pre-pass's provider call, added by task
    /// E02/F01/US02/T02 (hybrid-ocr). Turns raw document bytes into page-mapped text for
    /// scanned/image/low-text documents; native text extraction for born-digital pages happens in
    /// the caller (<c>Raffa.Documents.Contracts.Application.Extraction.HybridDocumentParsingService</c>)
    /// and never reaches this method — that step is not AI I/O (module-map: only Foundry/Document
    /// Intelligence calls go through this gateway). Always processes the full submitted document
    /// (ADR-017 "no 2-page cap"); a configured page-budget
    /// (<see cref="Configuration.AiGatewayOcrOptions.MaxPagesPerDocument"/>) fails the call
    /// visibly rather than truncating it. The returned page map
    /// (<see cref="Contracts.AiOcrResult.Pages"/>) feeds
    /// <c>Raffa.Documents.Contracts.Application.Extraction.DocumentPageText</c> so every
    /// downstream extraction stage can still cite a source page regardless of whether native
    /// parsing or OCR produced it.
    /// </summary>
    Task<Result<AiOcrResult>> OcrAsync(
        AiOcrRequest request, CancellationToken cancellationToken = default);
}
