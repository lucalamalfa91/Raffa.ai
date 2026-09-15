namespace Raffa.SharedKernel.Suppliers;

/// <summary>
/// Cross-module port (ADR-002, same split as <see cref="ISupplierResolver"/>) that lets Documents,
/// Renewals and the API show a supplier's name next to a contract without referencing
/// <c>Raffa.Suppliers.Products</c> directly — "names, never guids, everywhere" (ADR-024, parent
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

    /// <summary>
    /// Task E19/F04/US01/T01 (outcome-resolves-the-opportunity; ADR-028 §D5 clause 2). The
    /// reverse of <see cref="GetNamesAsync"/>: resolves an already-normalized name (
    /// <c>Raffa.Suppliers.Products.Application.SupplierNameNormalizer.Normalize</c>'s own output)
    /// to the single <c>Raffa.Suppliers.Products.Domain.Supplier</c> id it names for
    /// <paramref name="tenantId"/>, via the <c>(tenant_id, normalized_name)</c> unique index
    /// (<c>Raffa.Suppliers.Products.Infrastructure.Configurations.SupplierConfiguration</c>) —
    /// never <c>Supplier.Aliases</c>, so this is a strict, deterministic lookup against the one
    /// column the index actually enforces uniqueness on, not the broader alias-matching
    /// <see cref="ISupplierResolver.ResolveAsync"/> performs.
    ///
    /// <para>
    /// <b>Read-only, on purpose</b>: unlike <see cref="ISupplierResolver.ResolveAsync"/> (which
    /// resolves *or creates*), this method never inserts a row. A name that matches no known
    /// supplier returns <see langword="null"/> — never a fabricated id and never a new
    /// <c>Supplier</c> row — so a caller resolving a negotiation outcome's link (never a guess,
    /// ADR-001 w16 clause 4) cannot mint a supplier as a side effect of recording an outcome.
    /// </para>
    /// </summary>
    Task<EntityId?> FindByNormalizedNameAsync(
        TenantId tenantId,
        string normalizedName,
        CancellationToken cancellationToken);
}
