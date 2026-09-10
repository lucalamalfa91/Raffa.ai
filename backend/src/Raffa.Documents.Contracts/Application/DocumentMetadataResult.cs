using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// Outcome of a successful <see cref="DocumentQueryService.GetByIdAsync"/> call — the metadata
/// and processing status <see cref="DocumentUploadService"/> persisted for a
/// <see cref="Document"/> (us-01-document-upload, AC-2/AC-3).
/// </summary>
public sealed record DocumentMetadataResult(
    EntityId DocumentId,
    EntityId? ContractId,
    string FileName,
    string MimeType,
    ContractDocumentType DocumentType,
    DocumentProcessingStatus ProcessingStatus,
    DateTimeOffset CreatedAt);
