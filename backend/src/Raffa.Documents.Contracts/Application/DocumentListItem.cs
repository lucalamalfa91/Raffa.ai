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
    int WeakFactCount,
    /// <summary>Task E16/F02/US03/T01 (ADR-027 §D6/§D7): the content gate's refusal <em>code</em>,
    /// set only on a <see cref="DocumentProcessingStatus.Rejected"/> row. A code, never a sentence
    /// -- the screen picks the words (ADR-020 w15 §6).</summary>
    Admission.AdmissionRejectionReason? RejectionReason = null);

/// <summary>
/// ADR-027 §D7 as amended by §C5 and §C9 (task E16/F02/US03/T01): tenant-wide, page-independent,
/// unfiltered by the <c>status</c> query. Five <em>overlapping</em> projections, not a partition
/// -- no sum holds, and none of them is "askable" (§C5): <c>All</c> is every row except
/// <c>Rejected</c> ("All documents · N"); <c>NeedsAttention</c> is <em>not Completed and not
/// Rejected</em> (<c>Uploaded</c> + <c>Processing</c> + <c>NeedsReview</c> + <c>Failed</c>, the
/// set the screen's default filter lists, §C9); <c>NeedsReview</c> is <c>NeedsReview</c> alone
/// (§C5, "K waiting for your review"); <c>Processing</c> is <c>Uploaded</c> + <c>Processing</c>;
/// <c>Rejected</c> is <c>Rejected</c>.
/// </summary>
public sealed record DocumentCounts(int All, int NeedsAttention, int NeedsReview, int Processing, int Rejected)
{
    public static readonly DocumentCounts Empty = new(0, 0, 0, 0, 0);
}

/// <summary>One page of <see cref="DocumentListItem"/> rows plus the paging envelope
/// <c>GET /api/contracts</c> already established for this API.</summary>
public sealed record DocumentListPage(
    IReadOnlyList<DocumentListItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    /// <summary>ADR-027 §D7 — see <see cref="DocumentCounts"/>. Always set by
    /// <see cref="DocumentQueryService.ListAsync"/>; the default exists for callers that build a
    /// page by hand (tests) and is rendered as zeros, never omitted.</summary>
    DocumentCounts? Counts = null);
