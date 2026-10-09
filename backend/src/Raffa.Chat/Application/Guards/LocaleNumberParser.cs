using System.Globalization;
using System.Text;

namespace Raffa.Chat.Application.Guards;

/// <summary>Which character a language writes the decimal mark with (LANG-01, F1-D03 locale matrix).</summary>
public enum DecimalConvention
{
    /// <summary>No language known: the parser reads an ambiguous token the way contracts mostly mean
    /// it ("667,000" and "667.000" are both six hundred sixty-seven thousand).</summary>
    Unknown = 0,

    /// <summary>English: <c>1,234.56</c>.</summary>
    Point = 1,

    /// <summary>Italian, Spanish, German, French: <c>1.234,56</c> (IT/ES/DE) and <c>1 234,56</c> (FR,
    /// with a narrow no-break space U+202F or a no-break space U+00A0 as the group separator).</summary>
    Comma = 2,
}

/// <summary>The locale matrix for numbers (F1-D03 / LANG-01): language to decimal convention.</summary>
public static class NumericLocale
{
    /// <summary>The five supported languages (D6).</summary>
    public static readonly IReadOnlyList<string> SupportedLanguages = ["it", "en", "fr", "es", "de"];

    /// <summary>The decimal convention of <paramref name="language"/> (an ISO 639-1 code or a BCP-47
    /// tag such as <c>fr-FR</c>); <see cref="DecimalConvention.Unknown"/> for blank or unsupported.</summary>
    public static DecimalConvention ConventionFor(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return DecimalConvention.Unknown;
        }

        var tag = language.Trim();
        var cut = tag.IndexOfAny(['-', '_']);
        if (cut > 0)
        {
            tag = tag[..cut];
        }

        return tag.ToLowerInvariant() switch
        {
            "en" => DecimalConvention.Point,
            "it" or "fr" or "es" or "de" => DecimalConvention.Comma,
            _ => DecimalConvention.Unknown,
        };
    }
}

/// <summary>
/// Locale-aware reading of one numeric token (LANG-01, F1-T07). Reads <c>1.234,56</c> (IT/ES/DE),
/// <c>1,234.56</c> (EN), <c>1 234,56</c> (FR, group separator a plain space, U+00A0 no-break space,
/// U+202F narrow no-break space or U+2009 thin space), the Swiss <c>1'234.56</c>, and the shapes with
/// a single separator: <c>132,50</c> and <c>1,5</c> are decimals, <c>667,000</c> and <c>667.000</c> are
/// thousands. The old reader treated every comma as a thousands separator and every dot as a decimal
/// point, so <c>667.000,00</c> was read as 667 and a correct answer was rejected.
///
/// <para>
/// The only truly ambiguous shape is a single separator followed by exactly three digits
/// (<c>667,000</c> versus <c>1,500</c> as 1.5 with three decimals). Rules, in order: a percentage or a
/// magnitude-suffixed number ("1,500M") reads it as a decimal; a leading zero ("0,500") reads it as a
/// decimal; a number that already has space or apostrophe groups reads the separator as the decimal
/// mark; a known <see cref="DecimalConvention"/> decides by whether the separator is that language's
/// decimal mark; with no language it is read as thousands.
/// </para>
/// </summary>
public static class LocaleNumberParser
{
    private static bool IsGroupingSpace(char c) => c is ' ' or ' ' or ' ' or ' ' or ' ';

    /// <summary>
    /// Parses <paramref name="text"/> (digits plus <c>. , ' ’</c> and group spaces only; no currency, no
    /// sign, no percent). Returns <see langword="false"/> for anything malformed, never throws.
    /// </summary>
    /// <param name="preferDecimal">Read a single separator followed by exactly three digits as a
    /// decimal mark (percentages, magnitude-suffixed numbers).</param>
    public static bool TryParse(string? text, DecimalConvention convention, bool preferDecimal, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var grouped = false;
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Trim())
        {
            if (char.IsAsciiDigit(c) || c is '.' or ',')
            {
                builder.Append(c);
            }
            else if (IsGroupingSpace(c) || c is '\'' or '’')
            {
                grouped = true;
            }
            else
            {
                return false;
            }
        }

        var token = builder.ToString();
        if (token.Length == 0 || !char.IsAsciiDigit(token[0]) || !char.IsAsciiDigit(token[^1]))
        {
            return false;
        }

        var dots = token.Count(c => c == '.');
        var commas = token.Count(c => c == ',');
        char? decimalMark = null;

        if (dots > 0 && commas > 0)
        {
            var lastDot = token.LastIndexOf('.');
            var lastComma = token.LastIndexOf(',');
            decimalMark = lastDot > lastComma ? '.' : ',';
            var other = decimalMark == '.' ? ',' : '.';
            if (token.Count(c => c == decimalMark) > 1 || token.LastIndexOf(other) > token.LastIndexOf(decimalMark.Value))
            {
                return false;
            }
        }
        else if (dots + commas > 0)
        {
            var separator = dots > 0 ? '.' : ',';
            if (dots + commas == 1)
            {
                var index = token.IndexOf(separator);
                var digitsAfter = token.Length - index - 1;
                if (digitsAfter != 3 || preferDecimal || grouped || token[0] == '0')
                {
                    decimalMark = separator;
                }
                else if (convention != DecimalConvention.Unknown)
                {
                    var conventional = convention == DecimalConvention.Point ? '.' : ',';
                    decimalMark = separator == conventional ? separator : null;
                }
            }
        }

        var groupingSeparator = decimalMark switch
        {
            '.' => ',',
            ',' => '.',
            _ => token.Contains('.') ? '.' : ',',
        };

        var integerPart = decimalMark is { } mark ? token[..token.IndexOf(mark)] : token;
        if (integerPart.Contains(groupingSeparator) && !HasValidGroups(integerPart, groupingSeparator))
        {
            return false;
        }

        var normalized = new StringBuilder(token.Length);
        foreach (var c in token)
        {
            if (c == decimalMark)
            {
                normalized.Append('.');
            }
            else if (char.IsAsciiDigit(c))
            {
                normalized.Append(c);
            }
        }

        return decimal.TryParse(
            normalized.ToString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Parses a value the pack itself stored (invariant <c>1234.5</c>, never grouped), with the
    /// locale-aware reader as a fallback for a value that carries separators.</summary>
    public static bool TryParsePackValue(string? text, out decimal value)
    {
        if (decimal.TryParse(
                text?.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return TryParse(text, DecimalConvention.Unknown, preferDecimal: false, out value);
    }

    // "1.234.567": a leading group of one to three digits, every following group exactly three.
    private static bool HasValidGroups(string integerPart, char separator)
    {
        var groups = integerPart.Split(separator);
        if (groups[0].Length is < 1 or > 3)
        {
            return false;
        }

        return groups.Skip(1).All(group => group.Length == 3);
    }
}
