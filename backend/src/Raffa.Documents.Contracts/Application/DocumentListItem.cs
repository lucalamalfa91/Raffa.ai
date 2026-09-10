using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// One row of the server-side Documents list (task E13/F04/US01/T02;
/// <c>inputs/requirements.md</c> R-DOC-06/09; OpenAPI <c>listDocuments</c>).
/// </summary>
/// <param name="SupplierName">Resolved through <c>ISupplierNameLookup</c> when the Suppliers module
/// is registered in the host; <see langword="null"/> otherwise — never a raw supplier id.</param>
/// <param name="Stage">R-DOC-09's real stage name, present only while
/// <paramref name="ProcessingStatus"/> is <see cref="DocumentProcessingStatus.Processing"/>; the
/// list never reports a stage for a document that has reached a terminal state.</param>
/// <param name="PageCount">Pages the parse produced, <see langword="null"/> before any parse.</param>
/// <param name="WeakFactCount">Critical facts of this document's contract whose latest evidence is
/// missing or below the review threshold — what the row's "Review N fields" action counts.</param>
public sealed record DocumentListItem(
    EntityId DocumentId,
    EntityId? ContractId,
    string? SupplierName,
    string FileName,
    ContractDocumentType DocumentType,
    DocumentProcessingStatus ProcessingStatus,
    string? Stage,
    int? PageCount,
    DateTimeOffset CreatedAt,
    int WeakFactCount);

/// <summary>One page of <see cref="DocumentListItem"/> rows plus the paging envelope
/// <c>GET /api/contracts</c> already established for this API.</summary>
public sealed record DocumentListPage(
    IReadOnlyList<DocumentListItem> Items,
    int Page,
    int PageSize,
    int TotalCount);
