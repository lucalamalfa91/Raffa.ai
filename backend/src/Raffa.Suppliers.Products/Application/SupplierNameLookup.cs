using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.Suppliers.Products.Domain;
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

    /// <summary>
    /// Task E19/F04/US01/T01 (outcome-resolves-the-opportunity; ADR-028 §D5 clause 2). Matches
    /// <see cref="Supplier.NormalizedName"/> only — never <see cref="Supplier.Aliases"/> — which is
    /// exactly the column <see cref="Infrastructure.Configurations.SupplierConfiguration"/>'s
    /// <c>ix_supplier_tenant_id_normalized_name</c> unique index covers, so at most one row can
    /// ever match per tenant; unlike <see cref="SupplierResolver.ResolveAsync"/> this never adds a
    /// row on a miss, and unlike that method's own alias-inclusive match, an alias-only hit here is
    /// deliberately <see langword="null"/> — the caller of this port (
    /// <c>Raffa.Api.NegotiationOutcomePropagationService.ResolveSavingsOpportunityIdAsync</c>)
    /// resolves the product's own strict identity rule, not a fuzzier one <see cref="SupplierResolver"/>
    /// only needs because it is also deciding whether to create.
    /// </summary>
    public async Task<EntityId?> FindByNormalizedNameAsync(
        TenantId tenantId,
        string normalizedName,
        CancellationToken cancellationToken)
    {
        // SingleOrDefaultAsync, not FirstOrDefaultAsync: the unique index this method matches
        // against (see this method's own doc comment) already guarantees at most one row, so this
        // also stands as a live check of that invariant -- a second row here would mean the index
        // itself is broken, worth throwing loudly for rather than silently picking one.
        var supplier = await dbContext.Suppliers
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.NormalizedName == normalizedName)
            .SingleOrDefaultAsync(cancellationToken);

        return supplier?.Id;
    }
}
