using Raffa.Quotes.Domain;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.QuoteExtraction.Orchestration;

/// <summary>Outcome of one <see cref="QuoteExtractionPipeline.ProcessAsync"/> run — the response
/// shape `POST /api/quotes` (see <c>Raffa.Api.QuotesEndpointExtensions</c>) folds into its own JSON
/// reply.</summary>
/// <param name="NormalizedLineItemCount">Task E05/F01/US01/T02 (quote-normalization): how many of
/// this run's <see cref="LineItemCount"/> lines resolved to a real
/// <c>Raffa.Quotes.Domain.QuoteLine.NormalizedAnnualUnitPrice</c> — see
/// <c>Raffa.Quotes.Application.Normalization.QuoteLineNormalizationOutcome</c>'s own doc
/// comment.</param>
/// <param name="UnresolvedNormalizationCount">The complement of <paramref name="NormalizedLineItemCount"/>
/// within <see cref="LineItemCount"/> — spec §11.3's "line-item normalization is unresolved" outcome,
/// made visible over HTTP as well as in the database.</param>
/// <param name="UnmatchedSkuCount">Added by task E05/F01/US02/T01 (sku-normalization, AC-2's "show
/// unmatched SKUs" half).</param>
/// <param name="InvalidCount">Task F6-T04: lines discarded for an out-of-range value; already
/// included in <see cref="SkippedCount"/>.</param>
/// <param name="HeaderFilled">Task F6-T07: header fields (<c>supplier</c>, <c>currency</c>,
/// <c>geography</c>, <c>purchaseDate</c>) the header stage filled from the document, for the person to
/// confirm; <see langword="null"/> when the stage did not run (it is off by default).</param>
public sealed record QuoteProcessingSummary(
    EntityId QuoteId,
    QuoteProcessingStatus ProcessingStatus,
    int LineItemCount,
    int SkippedCount,
    int PageCount,
    int NormalizedLineItemCount,
    int UnresolvedNormalizationCount,
    int UnmatchedSkuCount,
    int InvalidCount = 0,
    IReadOnlyList<string>? HeaderFilled = null);
