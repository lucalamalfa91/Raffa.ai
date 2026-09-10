using Raffa.SharedKernel;

namespace Raffa.Quotes.Application.Assessment;

/// <summary>
/// The full <c>GET /api/quotes/{id}/assessment</c> result (parent story us-01-market-assessment
/// AC-3) — one <see cref="LineMarketAssessment"/> per <c>Raffa.Quotes.Domain.QuoteLine</c> on the
/// quote, in the same order <see cref="MarketAssessmentService.AssessAsync"/> read them. No
/// quote-level rollup (e.g. "overall position"): spec §11.2's own "Assessment output" table is a
/// per-line concept (a single quote can legitimately mix above/in-line/below lines), and no ADR/
/// spec names a deterministic way to collapse several lines' positions into one — inventing one
/// here would be exactly the fabricated-precision Appendix C rule 10 warns against.
/// </summary>
public sealed record QuoteMarketAssessment(EntityId QuoteId, IReadOnlyList<LineMarketAssessment> Lines);
