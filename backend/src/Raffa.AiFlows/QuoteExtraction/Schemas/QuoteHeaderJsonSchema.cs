using System.Text.Json;
using System.Text.Json.Serialization;

namespace Raffa.AiFlows.QuoteExtraction.Schemas;

/// <summary>
/// The JSON Schema of the quote <b>header</b> stage (task F6-T07): who the quote is from, the
/// currency it is priced in, the market it applies to and its date: the four fields the benchmark
/// match is built from. A separate stage with its own schema, not extra properties on the line
/// schema, because the header is read once per document and the lines are many: asking for both in one
/// answer is how a model starts attributing a line's currency to the whole quote.
///
/// <para>
/// Every field carries its own evidence tail (<c>sourcePage</c>, <c>sourceSpan</c>,
/// <c>confidence</c>, Appendix C rule 2), so each one can be accepted or abstained on by itself. Written
/// for Azure OpenAI strict structured outputs like <see cref="QuoteLineJsonSchema"/>: every property
/// required, <c>additionalProperties: false</c>, nullability as a union, no bound keywords.
/// Payment terms and validity are deliberately not asked for: there is no column to keep them in, and
/// a fact that is extracted and not stored is cost with no use.
/// </para>
/// </summary>
public static class QuoteHeaderJsonSchema
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string Header()
    {
        var schema = new
        {
            type = "object",
            properties = new
            {
                supplier = Field("Legal or trading name of the company that issued the quote (the seller), as written, or null if the document does not say."),
                quoteDate = Field("The date of the quote as YYYY-MM-DD, or null. Not a validity date, a delivery date or a payment date."),
                currency = Field("ISO 4217 code of the currency the prices are in (EUR, USD, GBP ...), or null if it is not stated. Never inferred from a country."),
                geography = Field("Country or region the quote applies to as a 2-letter ISO 3166-1 code, or EU, or null if the document does not say."),
            },
            required = new[] { "supplier", "quoteDate", "currency", "geography" },
            additionalProperties = false,
        };

        return JsonSerializer.Serialize(schema, Options);
    }

    private static object Field(string description) => new
    {
        type = "object",
        description,
        properties = new
        {
            value = new { type = new[] { "string", "null" }, description = "The value as described above, or null." },
            sourcePage = new { type = new[] { "integer", "null" }, description = "The integer n of the [[PAGE n]] marker that precedes the evidence, or null." },
            sourceSpan = new { type = new[] { "string", "null" }, description = "A verbatim quote of at most 300 characters from that page that shows the value, or null." },
            confidence = new { type = "number", description = "Number from 0 to 1: your probability that the value is read correctly; 0 when value is null." },
        },
        required = new[] { "value", "sourcePage", "sourceSpan", "confidence" },
        additionalProperties = false,
    };
}

/// <summary>Raw shape of the header stage's answer. Every property is nullable: a model is not
/// guaranteed to honour the schema, and the fixture gateway answers <c>{}</c>.</summary>
internal sealed record ExtractedQuoteHeaderPayload(
    [property: JsonPropertyName("supplier")] ExtractedHeaderField? Supplier,
    [property: JsonPropertyName("quoteDate")] ExtractedHeaderField? QuoteDate,
    [property: JsonPropertyName("currency")] ExtractedHeaderField? Currency,
    [property: JsonPropertyName("geography")] ExtractedHeaderField? Geography);

internal sealed record ExtractedHeaderField(
    [property: JsonPropertyName("value")] string? Value,
    [property: JsonPropertyName("sourcePage")] int? SourcePage,
    [property: JsonPropertyName("sourceSpan")] string? SourceSpan,
    [property: JsonPropertyName("confidence")] double? Confidence);
