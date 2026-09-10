using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Application;

/// <summary>Outcome of a successful <see cref="DocumentUploadService.UploadAsync"/> call.</summary>
public sealed record DocumentUploadResult(
    EntityId DocumentId,
    string FileName,
    string MimeType,
    DocumentProcessingStatus ProcessingStatus,
    DateTimeOffset CreatedAt);
