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

/// <summary>A document file of a contract (name, type, bytes).</summary>
public sealed record ContractDocumentFile(string FileName, string MimeType, byte[] Content);

/// <summary>
/// Reads a contract's document by file name — the attachment a case email names (the contract the
/// renewal is about), so Raffa can attach it when it sends the email from the person's mailbox.
/// Implemented by <c>Raffa.Documents.Contracts</c>.
/// </summary>
public interface IContractDocumentFiles
{
    /// <returns>The file, or null when the contract has no readable document with that name.</returns>
    Task<ContractDocumentFile?> LoadAsync(TenantId tenantId, Guid contractId, string fileName, CancellationToken cancellationToken = default);
}
