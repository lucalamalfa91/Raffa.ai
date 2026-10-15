using System.Text.RegularExpressions;

namespace Raffa.Quotes.Application.Header;

/// <summary>
/// Task F6-T07: the one set of rules for what may be recorded in a quote's header fields (supplier,
/// currency, geography, quote date), used both when the header stage proposes a value read from the
/// document and when a person sets or corrects one (<c>PUT /api/quotes/{id}/header</c>). A value that
/// fails a rule is refused, never repaired into something plausible (Appendix C rule 10).
/// </summary>
public static partial class QuoteHeaderRules
{
    public const int MaxSupplierLength = 200;
    public const int MaxGeographyLength = 100;

    /// <summary>Earliest quote date accepted; a model that reads "1900" from a footer is wrong.</summary>
    public static readonly DateOnly EarliestQuoteDate = new(2000, 1, 1);

    /// <summary>How far into the future a quote date may be (a quote dated for next week is fine).</summary>
    public const int MaxDaysAhead = 30;

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCode();

    [GeneratedRegex("^[A-Z]{2,3}$")]
    private static partial Regex GeographyCode();

    /// <summary>Trimmed, inner whitespace collapsed; null when blank, too short or too long.</summary>
    public static string? NormalizeSupplier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var supplier = WhitespaceRun().Replace(value.Trim(), " ");
        return supplier.Length is >= 2 and <= MaxSupplierLength ? supplier : null;
    }

    /// <summary>Upper-cased three-letter code (ISO 4217 shape, the width of the column); null otherwise.</summary>
    public static string? NormalizeCurrency(string? value)
    {
        var code = value?.Trim().ToUpperInvariant();
        return code is not null && CurrencyCode().IsMatch(code) ? code : null;
    }

    /// <summary>
    /// A person's free-text geography (a country or region name or code), trimmed; null when blank or
    /// longer than the column.
    /// </summary>
    public static string? NormalizeGeography(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var geography = value.Trim();
        return geography.Length <= MaxGeographyLength ? geography : null;
    }

    /// <summary>
    /// A value <em>read by a model</em> must be a country or region code (the benchmark keys on codes:
    /// "IT", "US", "EU"): two or three letters, upper-cased. A country name the model wrote out is
    /// not turned into a code here (no table to do it deterministically), so it abstains.
    /// </summary>
    public static string? NormalizeGeographyCode(string? value)
    {
        var code = value?.Trim().ToUpperInvariant();
        return code is not null && GeographyCode().IsMatch(code) ? code : null;
    }

    public static bool IsPlausibleQuoteDate(DateOnly date, DateTimeOffset now) =>
        date >= EarliestQuoteDate && date <= DateOnly.FromDateTime(now.UtcDateTime).AddDays(MaxDaysAhead);
}
