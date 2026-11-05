using System.Globalization;
using System.Text.RegularExpressions;
using Raffa.AiFlows.Shared.Pack;

namespace Raffa.AiFlows.Ask.Agentic;

/// <summary>
/// Locale formatting for what the orchestrator writes and reads (ADR-024 amendment 2026-11-05):
/// the answer follows the interface language the user chose, whatever form the model or a tool
/// used. Two directions:
/// <list type="bullet">
/// <item><see cref="Display"/>: the <c>display</c> form of a pack value handed to the model with
/// each tool result ("CHF 281.000", "15 aprile 2026", "25,4%" in Italian; "CHF 281,000",
/// "15 April 2026", "25.4%" in English), so the model copies a form that is already right.</item>
/// <item><see cref="Format"/>: a conservative pass over the final markdown that rewrites only
/// unambiguous tokens -- ISO dates, currency-tagged amounts, decimal percentages -- into the
/// locale's form. Bare numbers, years, ids, <c>[n]</c> markers and words are never touched.</item>
/// </list>
/// Values keep their currency: the formatter never converts or infers one.
/// </summary>
public static partial class AnswerFormatter
{
    private static readonly string[] ItalianMonths =
        ["gennaio", "febbraio", "marzo", "aprile", "maggio", "giugno", "luglio", "agosto", "settembre", "ottobre", "novembre", "dicembre"];

    private static readonly string[] EnglishMonths =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    private static readonly HashSet<string> Currencies = new(StringComparer.OrdinalIgnoreCase) { "CHF", "EUR", "USD", "GBP" };

    [GeneratedRegex(@"(?<!\d)(\d{4})-(\d{2})-(\d{2})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDate();

    // "CHF 281000", "CHF 281000.50", "EUR 1,250,000", "281000 CHF", "€ 281000", "281.000,50 EUR".
    [GeneratedRegex(@"(?<cur>\b(?:CHF|EUR|USD|GBP)\b|[€$£])\s?(?<num>\d{1,3}(?:[.,]\d{3})*(?:[.,]\d{1,2})?|\d+(?:[.,]\d{1,2})?)(?![\d%])", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyFirst();

    [GeneratedRegex(@"(?<![\d.,])(?<num>\d{1,3}(?:[.,]\d{3})*(?:[.,]\d{1,2})?|\d+(?:[.,]\d{1,2})?)\s?(?<cur>\b(?:CHF|EUR|USD|GBP)\b|[€$£])", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyLast();

    [GeneratedRegex(@"(?<![\d.,])(?<int>\d{1,3})[.,](?<dec>\d{1,2})\s?%", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalPercent();

    /// <summary>The locale form of one pack value, for the model to copy.</summary>
    public static string Display(PackValue value, string language)
    {
        ArgumentNullException.ThrowIfNull(value);
        var italian = IsItalian(language);
        switch (value.Kind)
        {
            case PackValueKind.Date when DateOnly.TryParse(value.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date):
                return LongDate(date, italian);
            case PackValueKind.Amount when TryParseInvariant(value.Value, out var amount):
                return (string.IsNullOrWhiteSpace(value.Currency) ? string.Empty : value.Currency.ToUpperInvariant() + " ") + Number(amount, italian, 0, 2);
            case PackValueKind.Percentage when TryParseInvariant(value.Value.TrimEnd('%', ' '), out var percent):
                return Number(percent, italian, 0, 2) + "%";
            default:
                return value.Value;
        }
    }

    /// <summary>The final markdown with its ISO dates, currency amounts and decimal percentages in
    /// the locale's form; everything else as written.</summary>
    public static string Format(string? markdown, string language)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return markdown ?? string.Empty;
        }

        var italian = IsItalian(language);
        var text = IsoDate().Replace(markdown, m =>
            DateOnly.TryParseExact(m.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? LongDate(d, italian) : m.Value);

        text = CurrencyFirst().Replace(text, m => FormatMoney(m.Groups["cur"].Value, m.Groups["num"].Value, italian, currencyFirst: true) ?? m.Value);
        text = CurrencyLast().Replace(text, m => FormatMoney(m.Groups["cur"].Value, m.Groups["num"].Value, italian, currencyFirst: true) ?? m.Value);
        text = DecimalPercent().Replace(text, m =>
            TryParseInvariant(m.Groups["int"].Value + "." + m.Groups["dec"].Value, out var p) ? Number(p, italian, 0, 2) + "%" : m.Value);
        return text;
    }

    private static string? FormatMoney(string currency, string number, bool italian, bool currencyFirst)
    {
        if (!TryParseLoose(number, out var amount))
        {
            return null;
        }

        var code = currency switch { "€" => "EUR", "$" => "USD", "£" => "GBP", _ => currency.ToUpperInvariant() };
        if (!Currencies.Contains(code))
        {
            return null;
        }

        var decimals = amount == decimal.Truncate(amount) ? 0 : 2;
        var formatted = Number(amount, italian, decimals, 2);
        return currencyFirst ? code + " " + formatted : formatted + " " + code;
    }

    private static string LongDate(DateOnly date, bool italian) =>
        italian
            ? $"{date.Day} {ItalianMonths[date.Month - 1]} {date.Year}"
            : $"{date.Day} {EnglishMonths[date.Month - 1]} {date.Year}";

    private static string Number(decimal value, bool italian, int minDecimals, int maxDecimals)
    {
        var decimals = value == decimal.Truncate(value) ? minDecimals : Math.Min(maxDecimals, Math.Max(minDecimals, Scale(value)));
        var culture = italian ? CultureInfo.GetCultureInfo("it-IT") : CultureInfo.GetCultureInfo("en-GB");
        return value.ToString("N" + decimals, culture);
    }

    private static int Scale(decimal value) => BitConverter.GetBytes(decimal.GetBits(value)[3])[2];

    private static bool TryParseInvariant(string text, out decimal value) =>
        decimal.TryParse(text.Replace(",", string.Empty), NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    /// <summary>"281000", "281,000", "281.000", "281000.50", "281.000,50", "1,250,000.75" -- the
    /// grouping/decimal convention read from the token itself.</summary>
    private static bool TryParseLoose(string text, out decimal value)
    {
        var t = text.Trim();
        var lastDot = t.LastIndexOf('.');
        var lastComma = t.LastIndexOf(',');
        string normalized;
        if (lastDot >= 0 && lastComma >= 0)
        {
            // Both present: the last one is the decimal separator.
            normalized = lastDot > lastComma
                ? t.Replace(",", string.Empty)
                : t.Replace(".", string.Empty).Replace(',', '.');
        }
        else if (lastComma >= 0)
        {
            // One comma: thousands when followed by exactly three digits at the end ("281,000"), decimal otherwise.
            var after = t.Length - lastComma - 1;
            normalized = after == 3 && t.Count(c => c == ',') >= 1 && !t.Contains('.') && (lastComma == 0 || t.IndexOf(',') == lastComma || true)
                ? (after == 3 ? t.Replace(",", string.Empty) : t.Replace(',', '.'))
                : t.Replace(',', '.');
        }
        else if (lastDot >= 0)
        {
            var after = t.Length - lastDot - 1;
            normalized = after == 3 && t.Count(c => c == '.') >= 1 ? t.Replace(".", string.Empty) : t;
        }
        else
        {
            normalized = t;
        }

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsItalian(string? language) => !string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);
}
