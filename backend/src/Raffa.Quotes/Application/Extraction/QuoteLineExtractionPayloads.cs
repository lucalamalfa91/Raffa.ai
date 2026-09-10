using System.Text.Json.Serialization;

namespace Raffa.Quotes.Application.Extraction;

/// <summary>
/// Raw structured-output shape <see cref="QuoteLineExtractionService"/> deserializes the AI
/// Gateway `extract` role's <c>AiExtractionResult.PayloadJson</c> into, mirroring
/// <see cref="QuoteLineJsonSchema"/>'s shape exactly. Every property is nullable (even ones the
/// JSON Schema marks <c>required</c>): a structured-output model is not guaranteed to honour the
/// schema perfectly, and <c>Raffa.AiGateway.Fixtures.FixtureAiGateway.ExtractAsync</c> today
/// always returns an empty <c>{}</c> object (no live model behind it yet) — deserializing that
/// into this type must produce nulls, not throw. <see cref="QuoteLineExtractionService"/> is
/// responsible for validating required fields are actually present before persisting, and for
/// skipping (not crashing on) an item that is missing one. Deliberately does <b>not</b> include a
/// computed total/extended-price field: AC-3 ("separate arithmetic from LLM language") means the
/// model is never asked for one in the first place (see <see cref="QuoteLineJsonSchema"/>'s own
/// doc comment) — mirrors, but does not reference,
/// <c>Raffa.Documents.Contracts.Application.Extraction.ExtractionPayloads</c>'s own
/// <c>ExtractedLineItemFact</c> shape (ADR-002 forbids referencing that module).
/// </summary>
internal sealed record ExtractedQuoteLinesPayload(
    [property: JsonPropertyName("items")] IReadOnlyList<ExtractedQuoteLineFact>? Items);

internal sealed record ExtractedQuoteLineFact(
    [property: JsonPropertyName("sku")] string? Sku,
    [property: JsonPropertyName("edition")] string? Edition,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("quantity")] decimal? Quantity,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("unitPrice")] decimal? UnitPrice,
    [property: JsonPropertyName("listPrice")] decimal? ListPrice,
    [property: JsonPropertyName("discountPercent")] decimal? DiscountPercent,
    [property: JsonPropertyName("term")] string? Term,
    [property: JsonPropertyName("sourcePage")] int? SourcePage,
    [property: JsonPropertyName("sourceSpan")] string? SourceSpan,
    [property: JsonPropertyName("confidence")] double? Confidence);
