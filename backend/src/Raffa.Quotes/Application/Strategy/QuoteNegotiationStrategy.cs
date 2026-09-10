using Raffa.SharedKernel;

namespace Raffa.Quotes.Application.Strategy;

/// <summary>
/// The full negotiation-strategy result for one quote (task E05/F03/US01/T01,
/// negotiation-strategy; parent story us-01-negotiation-strategy AC-1) — one
/// <see cref="LineNegotiationStrategy"/> per <see cref="Raffa.Quotes.Domain.QuoteLine"/> on the
/// quote, in the same order <see cref="NegotiationStrategyService.GenerateAsync"/> read them.
/// Mirrors <c>Raffa.Quotes.Application.Assessment.QuoteMarketAssessment</c>'s own exact shape —
/// see that type's own doc comment for why there is no quote-level rollup.
/// </summary>
public sealed record QuoteNegotiationStrategy(EntityId QuoteId, IReadOnlyList<LineNegotiationStrategy> Lines);
