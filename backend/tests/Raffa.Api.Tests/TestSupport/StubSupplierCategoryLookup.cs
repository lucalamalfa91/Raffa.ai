using Raffa.SharedKernel;
using Raffa.Suppliers.Products.Application;

namespace Raffa.Api.Tests.TestSupport;

/// <summary>
/// In-memory <see cref="ISupplierCategoryLookup"/> for task E24/F01/US01/T01's host-join
/// assertions (story us-01-portfolio-category-backend AC-1/AC-3, closes NW-23/OQ-w17-007). Same
/// shape and reason as <see cref="StubSupplierNameLookup"/>: the real implementation
/// (<c>Raffa.Suppliers.Products.Application.SupplierCategoryLookup</c>) lives behind its own
/// Postgres <c>SuppliersDbContext</c>, which these tests never stand up -- substituting the port
/// itself keeps the assertion about what <c>PortfolioEndpointExtensions.FilterByCategoryAsync</c>
/// does with a resolved category, which is this task's own composition; the port's real,
/// RLS-scoped behaviour is proven by <c>Raffa.Suppliers.Products.Tests</c>.
///
/// <para>
/// Implements <see cref="ISupplierCategoryLookup"/> directly rather than a SharedKernel port:
/// unlike <see cref="Raffa.SharedKernel.Suppliers.ISupplierNameLookup"/>, that interface is itself a
/// <c>Raffa.Suppliers.Products.Application</c> type -- see its own doc comment for why (only
/// <c>Raffa.Api</c> ever calls it, so no SharedKernel indirection exists to implement instead).
/// </para>
///
/// <para>
/// Honours the port's own "an id that does not resolve, or carries no category, is simply absent
/// from the result -- never a thrown exception or a fabricated category" contract, so a test can
/// seed a contract pointing at a supplier with no stub entry and still assert the endpoint narrows
/// it away rather than inventing a match (AC-3).
/// </para>
/// </summary>
internal sealed class StubSupplierCategoryLookup(IReadOnlyDictionary<EntityId, string> categoriesById)
    : ISupplierCategoryLookup
{
    public Task<IReadOnlyDictionary<EntityId, string>> GetCategoriesAsync(
        TenantId tenantId, IReadOnlyCollection<EntityId> supplierIds, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<EntityId, string> categories = supplierIds
            .Where(categoriesById.ContainsKey)
            .Distinct()
            .ToDictionary(id => id, id => categoriesById[id]);

        return Task.FromResult(categories);
    }
}
