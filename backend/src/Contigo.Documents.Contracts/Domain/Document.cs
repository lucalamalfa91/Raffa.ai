using Contigo.SharedKernel;

namespace Contigo.Documents.Contracts.Domain;

/// <summary>
/// An uploaded file (contract, amendment, quote, etc. — product spec §6 "Document" row and
/// §7.1 asynchronous ingestion pipeline). The byte content lives in tenant-prefixed object
/// storage (ADR-009); this row is the relational pointer plus processing state. Content history
/// is tracked via <see cref="DocumentVersion"/> — <see cref="StoragePath"/> points at the
/// current version only and is never silently repointed without a new version row
/// (Appendix C rule 5: never destructively overwrite contract history).
/// </summary>
public sealed class Document : TenantScopedEntity
{
    /// <summary>Contract this document belongs to, once classified/linked. Null while a freshly
    /// uploaded document is still being classified (spec §7.1 "uploaded" -&gt; "processing").</summary>
    public EntityId? ContractId { get; set; }

    public required string FileName { get; set; }
    public required string MimeType { get; set; }
    public ContractDocumentType DocumentType { get; set; } = ContractDocumentType.Other;

    /// <summary>Tenant-prefixed object storage path of the current version (ADR-009).</summary>
    public required string StoragePath { get; set; }
    public required string Checksum { get; set; }

    public DocumentProcessingStatus ProcessingStatus { get; set; } = DocumentProcessingStatus.Uploaded;

    /// <summary>
    /// Pages the hybrid parse actually produced (task E13/F04/US01/T02; R-DOC-06's <c>pageCount</c>
    /// column). <see langword="null"/> until a parse has run for this document — the list surface
    /// shows a blank cell rather than a guessed "1".
    /// </summary>
    public int? PageCount { get; set; }

    /// <summary>
    /// Tenant-prefixed storage path of the rendered first-page preview
    /// (<see cref="Contigo.SharedKernel.Storage.DocumentStoragePath.BuildPreview"/>, R-DOC-08), or
    /// <see langword="null"/> when no preview could be produced. Never surfaced to a client as a
    /// URL: <c>GET /api/documents/{id}/preview</c> streams the bytes under the caller's own tenant
    /// scope (ADR-009 "never a client-supplied raw blob URL").
    /// </summary>
    public string? PreviewPath { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
