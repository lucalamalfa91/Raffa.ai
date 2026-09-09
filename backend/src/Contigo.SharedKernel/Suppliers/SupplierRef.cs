namespace Contigo.SharedKernel.Suppliers;

/// <summary>
/// Read-only reference to a resolved supplier: just enough for a caller to store the tenant-scoped
/// foreign key (<see cref="Id"/>) and show a human the supplier's name (<see cref="Name"/>) without
/// a second round trip through <see cref="ISupplierNameLookup"/>. Never the full
/// <c>Contigo.Suppliers.Products.Domain.Supplier</c> aggregate — ADR-002 forbids Documents,
/// Renewals and the API from referencing that module at all; this record, plus
/// <see cref="ISupplierResolver"/>/<see cref="ISupplierNameLookup"/>, is the entire cross-module
/// contract they get instead (parent story us-01-supplier-identity).
/// </summary>
public sealed record SupplierRef(EntityId Id, string Name);
