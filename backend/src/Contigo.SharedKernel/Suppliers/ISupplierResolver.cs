namespace Contigo.SharedKernel.Suppliers;

/// <summary>
/// Cross-module port (ADR-002: the port lives in SharedKernel, the implementation in
/// <c>Contigo.Suppliers.Products</c>, composition in <c>Contigo.Api</c>/<c>Contigo.Worker</c>) that
/// turns a raw, as-extracted or as-typed supplier name into a stable, tenant-scoped
/// <see cref="SupplierRef"/> — matching an existing row by normalized name or alias before ever
/// creating a new one (parent story us-01-supplier-identity AC-2: "'Salesforce, Inc.' and
/// 'salesforce' resolve to one row"). The staged extraction pipeline (task E13/F03/US01/T02) is
/// the first real caller; nothing in this task wires it in yet.
/// </summary>
public interface ISupplierResolver
{
    Task<Result<SupplierRef>> ResolveAsync(
        TenantId tenantId, string rawName, CancellationToken cancellationToken);
}
