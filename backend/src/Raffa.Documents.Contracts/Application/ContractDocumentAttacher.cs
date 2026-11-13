using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Storage;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// <see cref="IContractDocumentAttacher"/>: stores a renewal's signed contract as a
/// <see cref="Document"/> linked to the contract it renews. Unlike <see cref="DocumentUploadService"/> it
/// creates no contract shell and queues no extraction — the renewal case already verified the signed
/// terms and writes them as the contract's current term version — so the contract is never doubled in
/// Portfolio. The same file already held by the tenant (same SHA-256) is reused, and linked to the
/// contract when it was not linked to any.
/// </summary>
public sealed class ContractDocumentAttacher(
    DocumentsContractsDbContext dbContext,
    IDocumentStorage storage,
    ITenantContext tenantContext,
    IClock clock,
    IAuditWriter auditWriter) : IContractDocumentAttacher
{
    public async Task<Guid> AttachSignedAsync(
        TenantId tenantId, SignedContractDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Content.Length == 0)
        {
            throw new ArgumentException("The signed contract is empty.", nameof(document));
        }

        using var _ = tenantContext.BeginScope(tenantId);
        var contractId = new EntityId(document.ContractId);
        var contractExists = await dbContext.Contracts
            .AnyAsync(c => c.TenantId == tenantId && c.Id == contractId, cancellationToken)
            .ConfigureAwait(false);
        if (!contractExists)
        {
            throw new InvalidOperationException($"Contract {document.ContractId} does not exist in this tenant.");
        }

        var checksum = Convert.ToHexString(SHA256.HashData(document.Content));
        var existing = await dbContext.Documents
            .Where(d => d.TenantId == tenantId
                && d.Checksum == checksum
                && d.ProcessingStatus != DocumentProcessingStatus.Rejected)
            .OrderBy(d => d.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.ContractId is null)
            {
                existing.ContractId = contractId;
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            return existing.Id.Value;
        }

        var documentId = EntityId.New();
        var now = clock.UtcNow;
        var fileName = string.IsNullOrWhiteSpace(document.FileName) ? "signed-contract.pdf" : document.FileName;
        string storagePath;
        using (var content = new MemoryStream(document.Content, writable: false))
        {
            storagePath = await storage.SaveAsync(tenantId, documentId, 1, fileName, content, cancellationToken).ConfigureAwait(false);
        }

        dbContext.Documents.Add(new Document
        {
            Id = documentId,
            TenantId = tenantId,
            ContractId = contractId,
            FileName = fileName,
            MimeType = string.IsNullOrWhiteSpace(document.MimeType) ? "application/octet-stream" : document.MimeType,
            DocumentType = ContractDocumentType.OrderForm,
            StoragePath = storagePath,
            Checksum = checksum,
            ProcessingStatus = DocumentProcessingStatus.Completed,
            CreatedAt = now,
        });
        dbContext.DocumentVersions.Add(new DocumentVersion
        {
            TenantId = tenantId,
            DocumentId = documentId,
            VersionNumber = 1,
            StoragePath = storagePath,
            Checksum = checksum,
            CreatedBy = document.Actor,
            CreatedAt = now,
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            await storage.DeleteAsync(tenantId, storagePath, cancellationToken).ConfigureAwait(false);
            throw;
        }

        await auditWriter.WriteAsync(
            new AuditEntry(tenantId, document.Actor, "document.renewal_signed_attached", "document", documentId.Value.ToString(), now),
            cancellationToken).ConfigureAwait(false);
        return documentId.Value;
    }
}
