using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Application;

/// <summary>Outcome of a successful <see cref="DocumentUploadService.UploadAsync"/> call.
/// F5-D01: <see cref="AlreadyUploaded"/> is true when the same file (same SHA-256) was already
/// uploaded by this tenant; every other member then describes the <em>existing</em> document, and
/// nothing new was stored, queued or created.</summary>
public sealed record DocumentUploadResult(
    EntityId DocumentId,
    string FileName,
    string MimeType,
    DocumentProcessingStatus ProcessingStatus,
    DateTimeOffset CreatedAt,
    bool AlreadyUploaded = false,
    EntityId? ContractId = null);
