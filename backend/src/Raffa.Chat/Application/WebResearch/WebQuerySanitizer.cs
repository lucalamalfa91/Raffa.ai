using System.Text.RegularExpressions;

namespace Raffa.Chat.Application.WebResearch;

/// <summary>
/// Builds the one string that leaves Raffa (ADR-030). The query is made only of the user's own
/// words: the explicit "search the web" phrase is dropped, and every currency amount, percentage,
/// calendar date, money shorthand ("40k"), e-mail address and URL is removed — those are the
/// shapes a tenant figure takes, and the same shapes <c>Guards.NumericGuard</c> polices on the way
/// back. A supplier name stays (the user typed it, and "Salesforce renewal uplift" is exactly what
/// a procurement person would search). Capped at a word boundary; at least two words must remain
/// or there is no query at all.
/// </summary>
public static class WebQuerySanitizer
{
    public const int MinWords = 2;

    private static readonly Regex UrlPattern = new(@"\b(?:https?://|www\.)\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EmailPattern = new(@"\b[\w.+-]+@[\w-]+(\.[\w-]+)+\b", RegexOptions.Compiled);

    // Same shapes NumericGuard checks, plus currency words and money shorthand.
    private static readonly Regex CurrencyAmountPattern = new(
        @"(?:\b(?:CHF|EUR|USD|GBP)|[€$£])\s?\d[\d,.'’]*|" +
        @"\b\d(?:[\d,.'’]*\d)?\s?(?:(?:CHF|EUR|USD|GBP|euro|euros|dollar[is]?|dollari|franchi|sterline|pounds?)\b|[€$£])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MoneyShorthandPattern = new(@"\b\d+(?:[.,]\d+)?\s?[kKmM]\b", RegexOptions.Compiled);
    private static readonly Regex PercentagePattern = new(@"\b\d+(?:[.,]\d+)?\s?%", RegexOptions.Compiled);
    private static readonly Regex IsoDatePattern = new(@"\b\d{4}-\d{2}-\d{2}\b", RegexOptions.Compiled);
    private static readonly Regex SlashDatePattern = new(@"\b\d{1,2}[/.]\d{1,2}[/.]\d{2,4}\b", RegexOptions.Compiled);
    private static readonly Regex LongDatePattern = new(@"\b\d{1,2}\s+\p{L}+\s+\d{4}\b", RegexOptions.Compiled);

    // Any remaining number with four or more digits (a contract number, an amount typed without
    // a currency) is not something a public search needs.
    private static readonly Regex LongNumberPattern = new(@"\b\d[\d,.'’]{3,}\b", RegexOptions.Compiled);

    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex DanglingPunctuationPattern = new(@"\s+([,;:.!?])", RegexOptions.Compiled);

    /// <summary>
    /// The sanitised query, or <see langword="null"/> when fewer than <see cref="MinWords"/> words
    /// survive.
    /// </summary>
    public static string? Sanitize(string question, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(question) || maxChars <= 0)
        {
            return null;
        }

        var text = WebResearchTopicLexicon.StripExplicitRequest(question);
        text = UrlPattern.Replace(text, " ");
        text = EmailPattern.Replace(text, " ");
        text = CurrencyAmountPattern.Replace(text, " ");
        text = MoneyShorthandPattern.Replace(text, " ");
        text = PercentagePattern.Replace(text, " ");
        text = IsoDatePattern.Replace(text, " ");
        text = SlashDatePattern.Replace(text, " ");
        text = LongDatePattern.Replace(text, " ");
        text = LongNumberPattern.Replace(text, " ");
        text = WhitespacePattern.Replace(text, " ");
        text = DanglingPunctuationPattern.Replace(text, "$1").Trim();
        text = text.Trim(' ', ',', ';', ':', '.', '!', '?', '-', '–', '—');

        if (text.Length > maxChars)
        {
            var cut = text.LastIndexOf(' ', Math.Min(maxChars, text.Length - 1));
            text = (cut > 0 ? text[..cut] : text[..maxChars]).Trim();
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= MinWords ? text : null;
    }
}
