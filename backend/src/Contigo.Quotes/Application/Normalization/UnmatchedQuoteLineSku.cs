using Contigo.SharedKernel;

namespace Contigo.Quotes.Application.Normalization;

/// <summary>
/// One <see cref="Contigo.Quotes.Domain.QuoteLine"/> whose SKU is present but does not (yet) resolve
/// to a <see cref="Contigo.Quotes.Domain.SkuProductMapping"/> for this tenant — parent story
/// us-02-sku-normalization AC-2's "Show unmatched SKUs" half, made queryable over HTTP by
/// <see cref="SkuMappingService.RecalculateAsync"/>'s own response.
///
/// Deliberately not an extension of <see cref="Contigo.Quotes.Application.Assessment
/// .LineMarketAssessment"/> (which would otherwise be a natural place for this same information):
/// that record is task E05/F02/US01/T01's own already-accepted file, and
/// <see cref="Contigo.Quotes.Application.Strategy.NegotiationStrategyService"/>'s own doc comment
/// already declined to extend it for the identical "do not touch unrelated wave artifacts" reason
/// (choosing a second, small query instead of a shared field). This type follows that same
/// precedent — a small, independent read over <see cref="Contigo.Quotes.Domain.QuoteLine"/>.
/// </summary>
public sealed record UnmatchedQuoteLineSku(
    EntityId QuoteLineId,
    string Sku,
    string NormalizedSku,
    string? Edition,
    string Description);
