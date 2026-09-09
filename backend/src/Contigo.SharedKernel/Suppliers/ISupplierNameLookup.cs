namespace Contigo.SharedKernel.Suppliers;

/// <summary>
/// Cross-module port (ADR-002, same split as <see cref="ISupplierResolver"/>) that lets Documents,
/// Renewals and the API show a supplier's name next to a contract without referencing
/// <c>Contigo.Suppliers.Products</c> directly — "names, never guids, everywhere" (ADR-024, parent
/// story us-01-supplier-identity AC-4). Batched by design: a portfolio/renewals list page resolves
/// every row's supplier name in one call, not one query per row.
/// </summary>
public interface ISupplierNameLookup
{
    /// <summary>
    /// Returns the subset of <paramref name="supplierIds"/> that exist for
    /// <paramref name="tenantId"/>, mapped to their current <c>Name</c>. An id that does not
    /// resolve (wrong tenant, or no longer exists) is simply absent from the result — never a
    /// thrown exception or a fabricated placeholder name.
    /// </summary>
    Task<IReadOnlyDictionary<EntityId, string>> GetNamesAsync(
        TenantId tenantId,
        IReadOnlyCollection<EntityId> supplierIds,
        CancellationToken cancellationToken);
}
