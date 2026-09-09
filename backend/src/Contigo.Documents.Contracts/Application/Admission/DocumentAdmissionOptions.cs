namespace Contigo.Documents.Contracts.Application.Admission;

/// <summary>
/// Task E13/F04/US01/T01 (documents-admission): the three thresholds the admission gate and the
/// upload endpoint apply before anything is persisted (ADR-024 "gate before persistence";
/// <c>inputs/requirements.md</c> R-DOC-01/02/03; OQ-askv2-002 "thresholds are configuration").
/// Bound from the <c>Documents</c> configuration section by
/// <c>Infrastructure.ServiceCollectionExtensions.AddDocumentsContractsModule</c> — the same
/// "plain options type, bound once, registered as a singleton" shape
/// <c>Contigo.AiGateway.ServiceCollectionExtensions</c> uses for <c>AiGatewayOcrOptions</c>. The
/// property initializers are the requirements' own defaults, so a host with no <c>Documents</c>
/// section at all behaves exactly as R-DOC-01..03 specify.
/// </summary>
public sealed class DocumentAdmissionOptions
{
    /// <summary>Configuration section name: <c>Documents</c> (keys <c>Documents:MaxFileBytes</c>,
    /// <c>Documents:MinReadableChars</c>, <c>Documents:AdmissionThreshold</c>).</summary>
    public const string SectionName = "Documents";

    /// <summary>Largest upload accepted, in bytes. Anything larger is refused with HTTP 413 before
    /// any parse or model call (R-DOC-01 "≤ 50 MB per file"). Default 50 MiB.</summary>
    public long MaxFileBytes { get; init; } = 50L * 1024 * 1024;

    /// <summary>Minimum number of non-whitespace characters the parse/OCR must yield across every
    /// page for the document to be classified at all; below it the gate answers
    /// <c>no_readable_text</c> without calling the classify role (R-DOC-03 AC-2). Default 200.</summary>
    public int MinReadableChars { get; init; } = 200;

    /// <summary>Minimum classify-role confidence for an admitted type; a recognized contract kind
    /// reported below it is still rejected as <c>not_a_contract</c> (R-DOC-03). Default 0.6, the
    /// same value <c>DocumentProcessingPipeline</c>/<c>StagedExtractionService</c> already use as
    /// their "needs a human" low-confidence line.</summary>
    public double AdmissionThreshold { get; init; } = 0.6;
}
