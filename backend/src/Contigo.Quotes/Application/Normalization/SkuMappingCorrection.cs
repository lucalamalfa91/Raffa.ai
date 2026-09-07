namespace Contigo.Quotes.Application.Normalization;

/// <summary>
/// One caller-supplied manual product-mapping correction — task E05/F01/US02/T02 (sku-recalculate;
/// parent story us-02-sku-normalization AC-2's "...and allow manual product mapping" half). Input to
/// <see cref="SkuMappingService.RecalculateAsync"/>, one entry per <see cref="Contigo.Quotes.Domain
/// .SkuProductMapping"/> a person is creating or correcting.
///
/// <see cref="Sku"/> is the raw, as-extracted SKU text a person saw on an
/// <see cref="Contigo.Quotes.Domain.SkuMatchStatus.Unmatched"/> line (the same raw text
/// <see cref="Contigo.Quotes.Domain.QuoteLine.Sku"/> holds) — deliberately never pre-normalized by
/// the caller; <see cref="SkuMappingService"/> normalizes it the same way
/// <see cref="SkuNormalizationService"/> does, so a correction submitted with different
/// casing/whitespace than the original extraction still resolves the same mapping row (mirrors
/// <see cref="Contigo.Quotes.Domain.SkuProductMapping.NormalizedSku"/>'s own doc comment: "a lookup
/// never has to re-normalize a mapping's own key at read time" — the same reasoning applies at write
/// time here).
/// </summary>
/// <param name="Sku">Required — the raw SKU text this correction resolves.</param>
/// <param name="Edition">Optional raw edition text — normalized and stored as
/// <see cref="Contigo.Quotes.Domain.SkuProductMapping.NormalizedEdition"/> (informational only, see
/// that property's own doc comment: matching itself keys on the normalized SKU alone).</param>
/// <param name="CanonicalSku">Required — the confirmed canonical SKU code (often equal to the
/// normalized <see cref="Sku"/>, but recorded explicitly — see <see cref="Contigo.Quotes.Domain
/// .SkuProductMapping.CanonicalSku"/>'s own doc comment for why).</param>
/// <param name="CanonicalEdition">Optional confirmed canonical edition.</param>
/// <param name="CanonicalProductName">Optional human-readable product name — display only, never
/// matched against.</param>
public sealed record SkuMappingCorrection(
    string Sku,
    string? Edition,
    string CanonicalSku,
    string? CanonicalEdition,
    string? CanonicalProductName);
