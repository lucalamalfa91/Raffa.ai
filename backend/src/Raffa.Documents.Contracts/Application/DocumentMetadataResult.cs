using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// Outcome of a successful <see cref="DocumentQueryService.GetByIdAsync"/> call — the metadata
/// and processing status <see cref="DocumentUploadService"/> persisted for a
/// <see cref="Document"/> (us-01-document-upload, AC-2/AC-3).
///
/// <para>
/// Task E22/F02/US01/T01 (ADR-029 clauses 6-7): <see cref="PageCount"/> is the persisted
/// <c>document.page_count</c> value (null until the Worker has run); <see cref="IsPageCountLimited"/>
/// signals that the document exceeds the OCR budget and only <c>MaxPagesPerDocument</c> pages were
/// rendered. Without these two fields the viewer can only show one page.
/// </para>
/// </summary>
public sealed record DocumentMetadataResult(
    EntityId DocumentId,
    EntityId? ContractId,
    string FileName,
    string MimeType,
    ContractDocumentType DocumentType,
    DocumentProcessingStatus ProcessingStatus,
    DateTimeOffset CreatedAt,
    int? PageCount = null,
    bool IsPageCountLimited = false);
