namespace Raffa.AiFlows.QuoteExtraction.Schemas;

/// <summary>Why <see cref="QuoteLinePayloadValidator"/> refused one extracted quote line.</summary>
internal enum QuoteLinePayloadIssue
{
    None = 0,

    /// <summary>A quantity or price is negative, a discount is outside 0-100, or a confidence is
    /// outside 0-1: not a value a correctly read line can have.</summary>
    OutOfRange,

    /// <summary>A number is too large for the column that stores it (or for the derived extended
    /// price), so saving the row would fail the whole run.</summary>
    NotRepresentable,

    /// <summary>A short text field (sku, edition, unit, term, description) is longer than its
    /// column; cutting it would store a wrong key or a wrong term, so the row is refused.</summary>
    TooLong,
}

/// <summary>
/// Task F6-T06 (payload validation, schema v2). The strict-mode JSON schema the extract stage is
/// given (<see cref="QuoteLineJsonSchema"/>) guarantees types, not ranges and not sizes. This is
/// the one place a model-produced line is checked against what the system can actually store and
/// trust, before any arithmetic or database write:
/// <list type="bullet">
/// <item>v1 (task F6-T04): quantity/prices not negative, discount 0-100, confidence 0-1.</item>
/// <item>v2: every number fits the column that stores it (and the derived extended price fits too),
/// and every short text fits its column. Without this a single oversized value made
/// <c>SaveChangesAsync</c> throw for the whole quote and the quote ended <c>Failed</c> although
/// every other line was fine; now only the bad row is dropped and counted.</item>
/// </list>
/// Text containing a NUL character is cleaned by <see cref="CleanText"/> (PostgreSQL text cannot
/// hold it) rather than refused: the rest of the line is intact.
/// A refused row is never repaired or clamped into a plausible number (Appendix C rule 10).
/// </summary>
internal static class QuoteLinePayloadValidator
{
    /// <summary>Version of the line contract this validator enforces.</summary>
    public const int SchemaVersion = 2;

    // Column limits of quote_line (QuoteLineConfiguration). Kept next to the checks that protect them;
    // QuoteLinePayloadValidatorTests asserts they match the EF model so they cannot drift apart.
    public const int MaxSkuLength = 100;
    public const int MaxEditionLength = 100;
    public const int MaxDescriptionLength = 1000;
    public const int MaxUnitLength = 50;
    public const int MaxTermLength = 100;
    public const int MaxSourceSpanLength = 2000;

    // numeric(18,4) quantity: 14 integer digits; numeric(18,2) prices: 16 integer digits.
    private const decimal QuantityCeiling = 100_000_000_000_000m;
    private const decimal MoneyCeiling = 10_000_000_000_000_000m;

    public static QuoteLinePayloadIssue Validate(ExtractedQuoteLineFact item)
    {
        if (!(item.Quantity is null or >= 0m
            && item.UnitPrice is null or >= 0m
            && item.ListPrice is null or >= 0m
            && item.DiscountPercent is null or (>= 0m and <= 100m)
            && item.Confidence is null or (>= 0d and <= 1d)))
        {
            return QuoteLinePayloadIssue.OutOfRange;
        }

        if (item.Quantity >= QuantityCeiling
            || item.UnitPrice >= MoneyCeiling
            || item.ListPrice >= MoneyCeiling
            || !ExtendedPriceFits(item))
        {
            return QuoteLinePayloadIssue.NotRepresentable;
        }

        if (Exceeds(item.Sku, MaxSkuLength)
            || Exceeds(item.Edition, MaxEditionLength)
            || Exceeds(item.Description, MaxDescriptionLength)
            || Exceeds(item.Unit, MaxUnitLength)
            || Exceeds(item.Term, MaxTermLength))
        {
            return QuoteLinePayloadIssue.TooLong;
        }

        return QuoteLinePayloadIssue.None;
    }

    /// <summary>Removes NUL characters and, for evidence text, cuts to <paramref name="maxLength"/>
    /// (a span is a citation, not a key, so a shortened one is still honest).</summary>
    public static string? CleanText(string? value, int? maxLength = null)
    {
        if (value is null)
        {
            return null;
        }

        var cleaned = value.Contains('\0', StringComparison.Ordinal)
            ? value.Replace("\0", string.Empty, StringComparison.Ordinal)
            : value;
        return maxLength is { } max && cleaned.Length > max ? cleaned[..max] : cleaned;
    }

    private static bool Exceeds(string? value, int maxLength) =>
        value is not null && CleanText(value)!.Length > maxLength;

    private static bool ExtendedPriceFits(ExtractedQuoteLineFact item)
    {
        try
        {
            var unitPrice = item.UnitPrice;
            if (unitPrice is null && item.ListPrice is not null && item.DiscountPercent is not null)
            {
                unitPrice = item.ListPrice.Value * (1m - (item.DiscountPercent.Value / 100m));
            }

            if (item.Quantity is null || unitPrice is null)
            {
                return true;
            }

            return item.Quantity.Value * unitPrice.Value < MoneyCeiling;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
