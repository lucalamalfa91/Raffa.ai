using Microsoft.EntityFrameworkCore;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Storage;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Documents.Contracts.Application;

/// <summary><see cref="IContractDocumentFiles"/>: the newest non-rejected document of the contract with that file name.</summary>
public sealed class ContractDocumentFiles(
    DocumentsContractsDbContext dbContext,
    IDocumentStorage storage,
    ITenantContext tenantContext) : IContractDocumentFiles
{
    public async Task<ContractDocumentFile?> LoadAsync(TenantId tenantId, Guid contractId, string fileName, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);
        var id = new EntityId(contractId);
        var document = await dbContext.Documents.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.ContractId == id && d.FileName == fileName
                && d.ProcessingStatus != DocumentProcessingStatus.Rejected)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        var bytes = await storage.LoadAsync(tenantId, document.StoragePath, cancellationToken).ConfigureAwait(false);
        return bytes is null || bytes.Length == 0 ? null : new ContractDocumentFile(document.FileName, document.MimeType, bytes);
    }
}
