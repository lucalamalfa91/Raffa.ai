namespace Raffa.Suppliers.Products.Domain;

/// <summary>
/// Tenant-scoped supplier identity row (task E13/F03/US01/T01, parent story
/// us-01-supplier-identity AC-1; ADR-024 "Supplier identity"). One row per distinct supplier a
/// tenant does business with; <c>Application.SupplierResolver</c> is the only writer of
/// <see cref="NormalizedName"/> — it exists purely so "Salesforce, Inc." and "salesforce" resolve
/// to this same row instead of a duplicate (AC-2).
/// </summary>
public sealed class Supplier : TenantScopedEntity
{
    public required string Name { get; set; }

    /// <summary>
    /// <c>Application.SupplierNameNormalizer</c>'s output for <see cref="Name"/> at the moment
    /// this row was created — lower-cased, legal-suffix- and punctuation-stripped. The unique
    /// index on (tenant_id, normalized_name) (see
    /// <see cref="Infrastructure.Configurations.SupplierConfiguration"/>) is what actually
    /// prevents two rows for the same tenant + normalized name from ever being created; this
    /// column is never recomputed from a later, corrected <see cref="Name"/> — a rename is a
    /// deliberate future write, not an automatic side effect.
    /// </summary>
    public required string NormalizedName { get; set; }

    /// <summary>
    /// Additional normalized names that should also resolve to this row (for example a former
    /// legal name, or a short form no legal-suffix strip alone would produce). Every entry is
    /// expected to already be in <c>Application.SupplierNameNormalizer</c>'s own output shape —
    /// <c>Application.SupplierResolver.ResolveAsync</c> compares a freshly normalized raw name
    /// against this collection as-is, it does not re-normalize each element. Nothing in this task
    /// populates this beyond the empty default; it exists so a future alias-management task has
    /// somewhere to write, and so the resolver's own alias-match path is provable today.
    /// </summary>
    public string[] Aliases { get; set; } = [];

    public string? Category { get; set; }

    public string? Country { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }
}
