using Raffa.SharedKernel;
using Raffa.Suppliers.Products.Domain;
using Raffa.Suppliers.Products.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Suppliers.Products.Application;

/// <summary>
/// Batched supplier-category lookup (task E24/F01/US01/T01, story
/// us-01-portfolio-category-backend AC-1/AC-3; closes NW-23/OQ-w17-007). Same shape and reason as
/// <see cref="Raffa.SharedKernel.Suppliers.ISupplierNameLookup.GetNamesAsync"/>: a portfolio
/// page's own distinct supplier ids are resolved to their <see cref="Supplier.Category"/> in one
/// batched call, never one query per row.
///
/// A separate type rather than a new member on <c>ISupplierNameLookup</c>: that interface's only
/// other implementer today is <c>Raffa.Api.Tests.TestSupport.StubSupplierNameLookup</c>, consumed
/// by three endpoints (<c>ContractsEndpointExtensions</c>, <c>RenewalsEndpointExtensions</c>,
/// <c>AskCopilotService</c>) that have nothing to do with category filtering — widening that port
/// would force every one of them, and every future implementer, to carry a method they never call.
///
/// <para>
/// Consumed directly as a <c>Raffa.Suppliers.Products.Application</c> type, not through a
/// <c>Raffa.SharedKernel</c> port: unlike <c>ISupplierNameLookup</c> — needed by modules ADR-002
/// forbids from referencing <c>Raffa.Suppliers.Products</c> at all — nothing outside the host needs
/// this lookup. <c>Raffa.Api</c> already references this project directly (it must, to call
/// <see cref="Infrastructure.ServiceCollectionExtensions.AddSuppliersProductsModule"/>) and is
/// "the one project allowed to reference every module" (ADR-002), so no SharedKernel indirection is
/// needed for a capability only the host ever calls.
/// </para>
/// </summary>
public interface ISupplierCategoryLookup
{
    /// <summary>
    /// Returns the subset of <paramref name="supplierIds"/> that exist for
    /// <paramref name="tenantId"/> and carry a non-null <see cref="Supplier.Category"/>, mapped to
    /// that category. A supplier id that does not resolve for this tenant, or resolves with no
    /// category on file, is simply absent from the result — never a thrown exception or a
    /// fabricated category (the same "never a fabricated value" contract
    /// <see cref="Raffa.SharedKernel.Suppliers.ISupplierNameLookup.GetNamesAsync"/> documents for
    /// names, applied here to categories — story us-01-portfolio-category-backend AC-3).
    /// </summary>
    Task<IReadOnlyDictionary<EntityId, string>> GetCategoriesAsync(
        TenantId tenantId,
        IReadOnlyCollection<EntityId> supplierIds,
        CancellationToken cancellationToken);
}

/// <inheritdoc cref="ISupplierCategoryLookup"/>
public sealed class SupplierCategoryLookup(SuppliersDbContext dbContext) : ISupplierCategoryLookup
{
    public async Task<IReadOnlyDictionary<EntityId, string>> GetCategoriesAsync(
        TenantId tenantId,
        IReadOnlyCollection<EntityId> supplierIds,
        CancellationToken cancellationToken)
    {
        if (supplierIds.Count == 0)
        {
            return new Dictionary<EntityId, string>();
        }

        return await dbContext.Suppliers
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && supplierIds.Contains(s.Id) && s.Category != null)
            .ToDictionaryAsync(s => s.Id, s => s.Category!, cancellationToken);
    }
}
