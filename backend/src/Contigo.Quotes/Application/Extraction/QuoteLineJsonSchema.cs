using System.Text.Json;

namespace Contigo.Quotes.Application.Extraction;

/// <summary>
/// Builds the JSON Schema text <c>Contigo.Api.QuoteExtractionPipeline</c> sends as
/// <c>AiExtractionRequest.JsonSchema</c> for the quote line-item extraction stage (product spec
/// §7.3/§4.4: "schema-constrained output... quantities, SKU/edition, prices, discounts and
/// terms"; ADR-004: "a structured-output-capable model... not free text"). Public (unlike
/// <c>Contigo.Documents.Contracts.Application.Extraction.StagedExtractionJsonSchemas</c>, which is
/// <c>internal</c> to its own module): the AI Gateway call site
/// (<c>Contigo.Api.QuoteExtractionPipeline</c>) lives in a different project from this schema
/// builder — ADR-002 allows <c>Contigo.Api</c> to reference every module, but this type must still
/// be visible across that assembly boundary.
///
/// <b>AC-3 / Appendix C rule 6</b> ("prefer deterministic arithmetic... to LLM reasoning"): this
/// schema deliberately has <b>no</b> computed-total/extended-price property. The model reports only
/// what a person could read directly off the page (quantity, sku, edition, unit price, list price,
/// discount percent, term); <see cref="QuoteLineExtractionService"/> computes
/// <c>QuoteLine.ExtendedPrice</c> (and, when needed, <c>QuoteLine.UnitPrice</c> itself from
/// <c>listPrice</c>/<c>discountPercent</c>) in plain C# arithmetic afterward — the model is
/// structurally incapable of supplying a fabricated total because there is nowhere in this schema
/// for one to go.
///
/// Written for Azure OpenAI strict structured outputs (ADR-004 amendment 2026-09-09): every
/// property required, <c>additionalProperties: false</c>, nullability as a <c>["type", "null"]</c>
/// union, no numeric bound keywords (the 0..1 confidence range lives in the description). Every item
/// ends in the same evidence tail — <c>sourcePage</c>, <c>sourceSpan</c>, <c>confidence</c> — as every
/// other extraction schema in this codebase, so AC-2 holds uniformly.
/// </summary>
public static class QuoteLineJsonSchema
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string LineItems()
    {
        var schema = new
        {
            type = "object",
            properties = new
            {
                items = new
                {
                    type = "array",
                    description = "Every priced line of the quote, in document order. At most 60 items.",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            sku = new { type = new[] { "string", "null" }, description = "Product code / SKU / article number as written, or null." },
                            edition = new { type = new[] { "string", "null" }, description = "Edition or tier as written (e.g. 'Enterprise', 'E3'), or null." },
                            description = new { type = "string", description = "The line as named in the document (original language)." },
                            quantity = new { type = new[] { "number", "null" }, description = "Quantity as a JSON number, or null." },
                            unit = new { type = new[] { "string", "null" }, description = "Unit of measure as written (seats, users, hours, licences ...), or null." },
                            unitPrice = new { type = new[] { "number", "null" }, description = "Quoted price per unit as a JSON number, no currency symbol, or null." },
                            listPrice = new { type = new[] { "number", "null" }, description = "List (undiscounted) price per unit as a JSON number, or null." },
                            discountPercent = new { type = new[] { "number", "null" }, description = "Discount percentage as a JSON number (15 for 15%), or null." },
                            term = new { type = new[] { "string", "null" }, description = "Subscription or service term as written (e.g. '12 months', '3 years'), or null." },
                            sourcePage = new { type = new[] { "integer", "null" }, description = "The integer n of the [[PAGE n]] marker that precedes the line, or null." },
                            sourceSpan = new { type = new[] { "string", "null" }, description = "A verbatim quote of at most 300 characters from that page that shows the line, or null." },
                            confidence = new { type = "number", description = "Number from 0 to 1: your probability that the line is read correctly; below 0.6 when the document is ambiguous." },
                        },
                        required = new[]
                        {
                            "sku", "edition", "description", "quantity", "unit", "unitPrice", "listPrice",
                            "discountPercent", "term", "sourcePage", "sourceSpan", "confidence",
                        },
                        additionalProperties = false,
                    },
                },
            },
            required = new[] { "items" },
            additionalProperties = false,
        };

        return JsonSerializer.Serialize(schema, Options);
    }
}
