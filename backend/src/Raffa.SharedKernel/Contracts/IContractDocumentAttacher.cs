namespace Raffa.SharedKernel.Contracts;

/// <summary>A signed contract a renewal case received, to keep as a document of the contract it renews.</summary>
public sealed record SignedContractDocument(
    Guid ContractId,
    string FileName,
    string MimeType,
    byte[] Content,
    string Actor,
    Guid? CaseId = null);

/// <summary>
/// Files the signed contract of a renewal as a document of the existing contract (Documents, contract
/// page), so the new contract is in the archive and not only in the case. Implemented by
/// <c>Raffa.Documents.Contracts</c>; called by <c>Raffa.Renewals</c> when a case closes as renewed.
/// The case already checked the signed terms, so no extraction is queued: the document is linked as
/// <c>Completed</c> and never creates a second contract.
/// </summary>
public interface IContractDocumentAttacher
{
    /// <returns>The id of the document (an existing one when the same file is already in the workspace).</returns>
    Task<Guid> AttachSignedAsync(TenantId tenantId, SignedContractDocument document, CancellationToken cancellationToken = default);
}
