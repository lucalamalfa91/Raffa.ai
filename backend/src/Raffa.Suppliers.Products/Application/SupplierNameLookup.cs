using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.Suppliers.Products.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Suppliers.Products.Application;

/// <summary>
/// <see cref="ISupplierNameLookup"/> implementation (task E13/F03/US01/T01). One query for the
/// whole batch — Documents/Renewals/the API call this once per list page, not once per row, to
/// turn a contract's <c>SupplierId</c> into the name ADR-024 requires everywhere ("names, never
/// guids").
/// </summary>
public sealed class SupplierNameLookup(SuppliersDbContext dbContext) : ISupplierNameLookup
{
    public async Task<IReadOnlyDictionary<EntityId, string>> GetNamesAsync(
        TenantId tenantId,
        IReadOnlyCollection<EntityId> supplierIds,
        CancellationToken cancellationToken)
    {
        if (supplierIds.Count == 0)
        {
            return new Dictionary<EntityId, string>();
        }

        return await dbContext.Suppliers
            .Where(s => s.TenantId == tenantId && supplierIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
    }
}
