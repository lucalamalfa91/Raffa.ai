namespace Raffa.SharedKernel.Suppliers;

/// <summary>
/// Cross-module port (ADR-002, same split as <see cref="ISupplierNameLookup"/>) that reads the other
/// names a tenant's supplier is known by, so a matcher that compares names (the benchmark behind a
/// quote's market assessment, task F6-T09) can retry a supplier that did not match under the name
/// the quote wrote ("Salesforce, Inc.") with the names the tenant has recorded for it ("Salesforce").
/// <c>Raffa.Suppliers.Products</c> owns the supplier records and their aliases; nothing else does.
///
/// <para>
/// <b>Read-only, and silent about what it does not know.</b> Never creates a supplier (unlike
/// <see cref="ISupplierResolver"/>) and never guesses: an unknown name returns an empty list.
/// </para>
/// </summary>
public interface ISupplierAliasLookup
{
    /// <summary>
    /// The names <paramref name="tenantId"/> has recorded for the supplier that
    /// <paramref name="rawName"/> identifies (by its normalised name or one of its aliases): the
    /// supplier's own name first, then its aliases, without <paramref name="rawName"/> itself and
    /// without duplicates. Empty when no supplier of this tenant matches or the name is blank.
    /// </summary>
    Task<IReadOnlyList<string>> GetAlternateNamesAsync(
        TenantId tenantId, string rawName, CancellationToken cancellationToken);
}
