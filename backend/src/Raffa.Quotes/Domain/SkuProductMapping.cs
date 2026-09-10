using Raffa.SharedKernel;

namespace Raffa.Quotes.Domain;

/// <summary>
/// This tenant's own record of "raw, as-extracted SKU normalizes to this canonical product" (task
/// E05/F01/US02/T01, sku-normalization; parent story us-02-sku-normalization AC-1 "Normalize
/// SKU/edition to the canonical product mapping"). Deliberately a self-contained table inside
/// <c>Raffa.Quotes</c> rather than a reference into <c>Raffa.Suppliers.Products</c>: that
/// module is still an empty scaffold (see backend/README.md's own "Suppliers / Products |
/// scaffold" note), and ADR-002's dependency-direction rule forbids <c>Raffa.Quotes</c> from
/// referencing it — or any other domain module's internals — at all
/// (<c>Raffa.ArchitectureTests.DependencyDirectionTests</c>'s allow-list for this module is
/// exactly <c>[SharedKernel, Benchmark]</c>). Mirrors the same "cross-module reference by id/name
/// only, no physical FK, invented ahead of need only as far as this task's own scope requires"
/// restraint <c>Raffa.Documents.Contracts.Domain.ContractLineItem.ProductId</c>'s own doc comment
/// already documents for the identical, still-missing catalog.
///
/// Nothing in this task writes a row here yet — <c>SkuNormalizationService</c> only reads this
/// table — so, honestly, every tenant starts with zero mappings and every line with a present SKU
/// is <see cref="SkuMatchStatus.Unmatched"/> today. Task E05/F01/US02/T02 ("Manual product mapping
/// + recalculate trigger") is this table's intended first writer: a person resolving an unmatched
/// line creates or updates the row here, so every other line — this quote or a future one — sharing
/// the same <see cref="NormalizedSku"/> auto-resolves the next time normalization runs, per that
/// story's AC-3 "Re-run assessment after mapping correction".
/// </summary>
public sealed class SkuProductMapping : TenantScopedEntity
{
    /// <summary>The lookup key — <c>Raffa.Quotes.Application.Normalization.SkuNormalizer
    /// .Normalize</c>'s output, so a lookup never has to re-normalize a mapping's own key at read
    /// time. Unique per tenant (<see cref="Infrastructure.Configurations
    /// .SkuProductMappingConfiguration"/>'s own index).</summary>
    public required string NormalizedSku { get; set; }

    /// <summary>Normalized edition this mapping was confirmed against, when the correction was
    /// edition-specific. Informational only — matching itself keys on <see cref="NormalizedSku"/>
    /// alone (see <c>SkuNormalizationService</c>'s own doc comment for why edition is not part of
    /// the match key).</summary>
    public string? NormalizedEdition { get; set; }

    /// <summary>The confirmed canonical SKU code — often equal to <see cref="NormalizedSku"/>, but
    /// recorded explicitly rather than assumed identical: a person correcting a mapping may also be
    /// renaming a messy extracted code to the supplier's real published SKU.</summary>
    public required string CanonicalSku { get; set; }

    public string? CanonicalEdition { get; set; }

    /// <summary>Human-readable product name, when known — display only, never matched against.</summary>
    public string? CanonicalProductName { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
