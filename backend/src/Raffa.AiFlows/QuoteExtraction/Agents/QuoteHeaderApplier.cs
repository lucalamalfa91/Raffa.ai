using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Raffa.AiFlows.QuoteExtraction.Schemas;
using Raffa.Quotes.Application.Header;
using Raffa.Quotes.Domain;

namespace Raffa.AiFlows.QuoteExtraction.Agents;

/// <summary>Which header fields the stage filled and which it declined (by name:
/// <c>supplier</c>, <c>currency</c>, <c>geography</c>, <c>purchaseDate</c>).</summary>
public sealed record QuoteHeaderOutcome(IReadOnlyList<string> Filled, IReadOnlyList<string> Abstained)
{
    public static QuoteHeaderOutcome None { get; } = new([], []);
}

/// <summary>
/// Task F6-T07: turns the header stage's answer into header fields on the <see cref="Quote"/>, under
/// three rules that make it safe to run on every upload:
/// <list type="number">
/// <item><b>Blank fields only.</b> A value a person gave at upload (or corrected since) is never
/// overwritten by a model; the stage can only fill what is missing. The quote date is "missing" when
/// it is still the upload-day default (<see cref="Quote.PurchaseDate"/> is filled with the day of the
/// upload when the caller sent none, which says nothing about the document).</item>
/// <item><b>Confident and valid only.</b> A field below the configured confidence, with no value, or
/// failing <see cref="QuoteHeaderRules"/> (a currency that is not a three-letter code, a country the
/// model wrote out as a name, an implausible date) is declined and stays blank. A wrong supplier or
/// currency would silently mis-match every line of the quote; a blank one is asked about.</item>
/// <item><b>Nothing computed.</b> No currency is inferred from a country, no country from a supplier:
/// only what the document states and the model reports with evidence.</item>
/// </list>
/// Only mutates the tracked quote; the caller owns <c>SaveChangesAsync</c>.
/// </summary>
internal static class QuoteHeaderApplier
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>True when at least one header field is still blank, so the stage has something to fill.</summary>
    public static bool IsIncomplete(Quote quote) =>
        string.IsNullOrWhiteSpace(quote.Supplier)
        || string.IsNullOrWhiteSpace(quote.Currency)
        || string.IsNullOrWhiteSpace(quote.Geography)
        || IsDefaultedDate(quote);

    /// <summary>The upload stores the upload day as the date when the caller sends none.</summary>
    public static bool IsDefaultedDate(Quote quote) =>
        quote.PurchaseDate is null || quote.PurchaseDate == DateOnly.FromDateTime(quote.CreatedAt.UtcDateTime);

    /// <exception cref="JsonException">The payload is not JSON.</exception>
    public static QuoteHeaderOutcome Apply(Quote quote, string payloadJson, DateTimeOffset now, double minConfidence)
    {
        ArgumentNullException.ThrowIfNull(quote);

        var payload = JsonSerializer.Deserialize<ExtractedQuoteHeaderPayload>(payloadJson, PayloadOptions);
        var filled = new List<string>();
        var abstained = new List<string>();

        if (string.IsNullOrWhiteSpace(quote.Supplier))
        {
            Record(
                "supplier", payload?.Supplier, minConfidence, QuoteHeaderRules.NormalizeSupplier,
                value => quote.Supplier = value, filled, abstained);
        }

        if (string.IsNullOrWhiteSpace(quote.Currency))
        {
            Record(
                "currency", payload?.Currency, minConfidence, QuoteHeaderRules.NormalizeCurrency,
                value => quote.Currency = value, filled, abstained);
        }

        if (string.IsNullOrWhiteSpace(quote.Geography))
        {
            Record(
                "geography", payload?.Geography, minConfidence, QuoteHeaderRules.NormalizeGeographyCode,
                value => quote.Geography = value, filled, abstained);
        }

        if (IsDefaultedDate(quote))
        {
            Record(
                "purchaseDate", payload?.QuoteDate, minConfidence,
                value => DateOnly.TryParseExact(
                    value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    && QuoteHeaderRules.IsPlausibleQuoteDate(date, now)
                    ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : null,
                value => quote.PurchaseDate = DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                filled, abstained);
        }

        return new QuoteHeaderOutcome(filled, abstained);
    }

    private static void Record(
        string name,
        ExtractedHeaderField? field,
        double minConfidence,
        Func<string?, string?> normalize,
        Action<string> assign,
        List<string> filled,
        List<string> abstained)
    {
        var value = field is { Confidence: { } confidence } && confidence >= minConfidence
            ? normalize(field.Value)
            : null;

        if (value is null)
        {
            abstained.Add(name);
            return;
        }

        assign(value);
        filled.Add(name);
    }
}
