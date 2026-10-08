using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Raffa.Chat.Application.Guards;

/// <summary>A currency amount found in text. <see cref="Tolerance"/> is a cent for an exact figure and
/// half the last shown step for a shorthand ("40k" is 40,000 give or take 500).</summary>
public sealed record MoneyToken(string Raw, decimal Value, string Currency, decimal Tolerance);

/// <summary>A percentage found in text.</summary>
public sealed record PercentToken(string Raw, decimal Value, decimal Tolerance);

/// <summary>A calendar date found in text; more than one candidate when the day/month order is
/// ambiguous (<c>03/04/2027</c> without a language).</summary>
public sealed record DateToken(string Raw, IReadOnlyList<DateOnly> Candidates);

/// <summary>
/// Finds the three shapes <see cref="NumericGuard"/> checks (currency amount, percentage, calendar date)
/// in the five supported languages (LANG-01, F1-T07): IT/ES/DE <c>1.234,56</c>, EN <c>1,234.56</c>, FR
/// <c>1 234,56</c> with a narrow no-break space (U+202F) or a no-break space (U+00A0); a currency as ISO
/// code (<c>EUR 667.000,00</c>), symbol (<c>€667.000</c>, <c>667.000 €</c>) or word (<c>667.000 euro</c>);
/// a magnitude shorthand (<c>40k</c>, <c>1,5M</c>, <c>2 Mio</c>, <c>3 millions</c>); a percentage as
/// <c>%</c> or word; a date as ISO, numeric (<c>15/01/2027</c>, <c>15.01.2027</c>) or with a month name
/// in any of the five languages (<c>15 janvier 2027</c>, <c>15 de enero de 2027</c>,
/// <c>15. Januar 2027</c>, <c>January 15, 2027</c>). Pure, no I/O.
/// </summary>
public static class NumericTokenExtractor
{
    // A number: grouped by a space/no-break space/narrow no-break space/thin space/apostrophe/dot/comma
    // (groups of exactly three digits, never a partial group), optional decimal part; or a plain run
    // of digits with an optional decimal part. Never starts in the middle of another number.
    private const string Number =
        @"(?:\d{1,3}(?:[    '’.,]\d{3})+(?!\d)(?:[.,]\d+)?|\d+(?:[.,]\d+)?)(?!\d)";

    private const string Magnitude =
        @"(?:[ \u00A0\u202F]?(?:(?i:millions?|milioni|milione|millones|millón|millionen|miliardi|miliardo|milliards?|milliarden?|billions?|thousand|tausend|mila|mille|mil)" +
        @"|Mio\.?|mio\.?|Mrd\.?|mrd\.?|Mld\.?|mld\.?|mln\.?|mn|bn|Tsd\.?|[kKM])(?![\p{L}\d]))";

    private const string CurrencyCode = @"(?:(?<![\p{L}\d])(?:CHF|EUR|USD|GBP)(?![\p{L}])|[€$£])";

    private const string CurrencyWord =
        @"(?:(?:CHF|EUR|USD|GBP)(?![\p{L}])|[€$£]|(?i:euros?|euri|dollars?|dollari|dollaro|dólares|dólar|pounds?|sterline|libras?|pfund|francs?|franchi|francos?|franken)(?![\p{L}]))";

    private static readonly Regex MoneyPattern = new(
        @"(?<cur1>" + CurrencyCode + @")[ \u00A0\u202F]?(?<amt1>" + Number + @")(?<mag1>" + Magnitude + @")?" +
        @"|(?<![\d.,])(?<amt2>" + Number + @")(?<mag2>" + Magnitude + @")?[ \u00A0\u202F]?(?<cur2>" + CurrencyWord + @")",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PercentPattern = new(
        @"(?<![\d.,])(?<amt>" + Number + @")[\s  ]?" +
        @"(?:%|(?i:percent|per\s?cent|per\s?cento|pour\s?cent|por\s?ciento|prozent)(?![\p{L}]))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex IsoDatePattern = new(
        @"(?<![\d-])(\d{4})-(\d{2})-(\d{2})(?!\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NumericDatePattern = new(
        @"(?<![\d.,/-])(?<a>\d{1,2})(?<s>[./-])(?<b>\d{1,2})\k<s>(?<y>\d{4})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // "15 January 2027", "15th of January 2027", "1er janvier 2027", "15 de enero de 2027",
    // "15. Januar 2027", "15 gen 2027".
    private static readonly Regex DayFirstDatePattern = new(
        @"(?<![\d.,/-])(?<d>\d{1,2})(?:st|nd|rd|th|er|º|°)?\.?\s+(?:(?:de|del|of)\s+)?(?<m>\p{L}{3,10})\.?,?\s+(?:de\s+)?(?<y>\d{4})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // "January 15, 2027", "Jan 15th 2027".
    private static readonly Regex MonthFirstDatePattern = new(
        @"(?<![\p{L}\d])(?<m>\p{L}{3,10})\.?\s+(?<d>\d{1,2})(?:st|nd|rd|th)?,?\s+(?<y>\d{4})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Dictionary<string, int> Months = BuildMonths();

    /// <summary>Every currency amount in <paramref name="text"/>.</summary>
    public static IReadOnlyList<MoneyToken> Money(string? text, DecimalConvention convention = DecimalConvention.Unknown)
    {
        var found = new List<MoneyToken>();
        if (string.IsNullOrEmpty(text))
        {
            return found;
        }

        foreach (Match match in MoneyPattern.Matches(text))
        {
            var prefix = match.Groups["cur1"].Success;
            var amountText = match.Groups[prefix ? "amt1" : "amt2"].Value;
            var magnitudeGroup = match.Groups[prefix ? "mag1" : "mag2"];
            var currency = CurrencyOf(match.Groups[prefix ? "cur1" : "cur2"].Value);
            if (currency is null)
            {
                continue;
            }

            var multiplier = magnitudeGroup.Success ? MultiplierOf(magnitudeGroup.Value) : 1m;
            if (!LocaleNumberParser.TryParse(amountText, convention, preferDecimal: multiplier != 1m, out var baseValue))
            {
                continue;
            }

            found.Add(new MoneyToken(
                match.Value.Trim(), baseValue * multiplier, currency, ToleranceOf(amountText, multiplier)));
        }

        return found;
    }

    /// <summary>Every percentage in <paramref name="text"/> (<c>7%</c>, <c>7 %</c> with a no-break space,
    /// <c>7,5 %</c>, <c>7 per cento</c>, <c>7 Prozent</c>).</summary>
    public static IReadOnlyList<PercentToken> Percentages(string? text, DecimalConvention convention = DecimalConvention.Unknown)
    {
        var found = new List<PercentToken>();
        if (string.IsNullOrEmpty(text))
        {
            return found;
        }

        foreach (Match match in PercentPattern.Matches(text))
        {
            if (LocaleNumberParser.TryParse(match.Groups["amt"].Value, convention, preferDecimal: true, out var value))
            {
                found.Add(new PercentToken(match.Value.Trim(), value, 0.01m));
            }
        }

        return found;
    }

    /// <summary>Every calendar date in <paramref name="text"/> that parses to a real date. A token that
    /// looks like a date but is not one (an unknown month word, 31/02/2027) is skipped, never
    /// reported, so prose this extractor cannot read is never a violation.</summary>
    public static IReadOnlyList<DateToken> Dates(string? text, DecimalConvention convention = DecimalConvention.Unknown)
    {
        var found = new List<DateToken>();
        if (string.IsNullOrEmpty(text))
        {
            return found;
        }

        foreach (Match match in IsoDatePattern.Matches(text))
        {
            if (TryDate(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value, out var iso))
            {
                found.Add(new DateToken(match.Value, [iso]));
            }
        }

        foreach (Match match in NumericDatePattern.Matches(text))
        {
            var a = int.Parse(match.Groups["a"].Value, CultureInfo.InvariantCulture);
            var b = int.Parse(match.Groups["b"].Value, CultureInfo.InvariantCulture);
            var year = int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture);
            var candidates = new List<DateOnly>();
            if (TryDate(year, b, a, out var dayFirst))
            {
                candidates.Add(dayFirst);
            }

            // A slash may also be the US month-first order; dot and dash are day-first everywhere.
            if (match.Groups["s"].Value == "/" && convention != DecimalConvention.Comma &&
                TryDate(year, a, b, out var monthFirst) && !candidates.Contains(monthFirst))
            {
                candidates.Add(monthFirst);
            }

            if (candidates.Count > 0)
            {
                found.Add(new DateToken(match.Value, candidates));
            }
        }

        foreach (Match match in DayFirstDatePattern.Matches(text))
        {
            if (MonthOf(match.Groups["m"].Value) is { } month &&
                TryDate(int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture), month,
                    int.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture), out var date))
            {
                found.Add(new DateToken(match.Value, [date]));
            }
        }

        foreach (Match match in MonthFirstDatePattern.Matches(text))
        {
            if (MonthOf(match.Groups["m"].Value) is { } month &&
                TryDate(int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture), month,
                    int.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture), out var date))
            {
                found.Add(new DateToken(match.Value, [date]));
            }
        }

        return found;
    }

    private static bool TryDate(string year, string month, string day, out DateOnly date) =>
        TryDate(
            int.Parse(year, CultureInfo.InvariantCulture),
            int.Parse(month, CultureInfo.InvariantCulture),
            int.Parse(day, CultureInfo.InvariantCulture),
            out date);

    private static bool TryDate(int year, int month, int day, out DateOnly date)
    {
        date = default;
        if (year is < 1 or > 9999 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        date = new DateOnly(year, month, day);
        return true;
    }

    private static string? CurrencyOf(string token)
    {
        var t = token.Trim();
        switch (t)
        {
            case "€": return "EUR";
            case "$": return "USD";
            case "£": return "GBP";
        }

        var upper = t.ToUpperInvariant();
        if (upper is "CHF" or "EUR" or "USD" or "GBP")
        {
            return upper;
        }

        var lower = RemoveDiacritics(t);
        if (lower.StartsWith("euro", StringComparison.Ordinal))
        {
            return "EUR";
        }

        if (lower.StartsWith("dollar", StringComparison.Ordinal))
        {
            return "USD";
        }

        if (lower.StartsWith("pound", StringComparison.Ordinal) || lower.StartsWith("sterlin", StringComparison.Ordinal) ||
            lower.StartsWith("libra", StringComparison.Ordinal) || lower.StartsWith("pfund", StringComparison.Ordinal))
        {
            return "GBP";
        }

        if (lower.StartsWith("franc", StringComparison.Ordinal) || lower.StartsWith("franken", StringComparison.Ordinal))
        {
            return "CHF";
        }

        return null;
    }

    private static decimal MultiplierOf(string magnitude)
    {
        var m = magnitude.Trim().TrimEnd('.');
        if (m == "M")
        {
            return 1_000_000m;
        }

        var lower = RemoveDiacritics(m);
        if (lower == "k" || lower is "thousand" or "tausend" or "tsd" or "mila" or "mille" or "mil")
        {
            return 1_000m;
        }

        if (lower is "bn" or "mrd" or "mld" || lower.StartsWith("miliard", StringComparison.Ordinal) ||
            lower.StartsWith("milliard", StringComparison.Ordinal) || lower.StartsWith("billion", StringComparison.Ordinal))
        {
            return 1_000_000_000m;
        }

        // million(s), milione/milioni, millon/millones, millionen, mio, mln, mn
        return 1_000_000m;
    }

    // Half of the last digit the shorthand shows: "40k" is exact to 1,000 (so 500 either way), "1,5M" to
    // 100,000 (so 50,000). A plain figure keeps the cent tolerance of the old guard.
    private static decimal ToleranceOf(string amountText, decimal multiplier)
    {
        if (multiplier == 1m)
        {
            return 0.01m;
        }

        // A shorthand reads a single separator as the decimal mark (see LocaleNumberParser's
        // preferDecimal); with several separators only a mix of both kinds has a decimal part.
        var decimals = 0;
        var cut = amountText.LastIndexOfAny(['.', ',']);
        if (cut >= 0)
        {
            var dots = amountText.Count(c => c == '.');
            var commas = amountText.Count(c => c == ',');
            if (dots + commas == 1 || (dots > 0 && commas > 0))
            {
                decimals = amountText.Length - cut - 1;
            }
        }

        var step = multiplier;
        for (var i = 0; i < decimals; i++)
        {
            step /= 10m;
        }

        return Math.Max(0.01m, step / 2m);
    }

    private static int? MonthOf(string word)
    {
        var key = RemoveDiacritics(word.Trim().TrimEnd('.'));
        return Months.TryGetValue(key, out var month) ? month : null;
    }

    private static string RemoveDiacritics(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    private static Dictionary<string, int> BuildMonths()
    {
        // Full names and common abbreviations in en, it, fr, es, de; diacritics stripped, lower case.
        string[][] names =
        [
            ["january", "jan", "gennaio", "gen", "janvier", "janv", "enero", "ene", "januar", "jaenner", "janner"],
            ["february", "feb", "febbraio", "fevrier", "fevr", "fev", "febrero", "februar"],
            ["march", "mar", "marzo", "mars", "marz", "maerz"],
            ["april", "apr", "aprile", "avril", "avr", "abril", "abr"],
            ["may", "maggio", "mag", "mai", "mayo"],
            ["june", "jun", "giugno", "giu", "juin", "junio", "juni"],
            ["july", "jul", "luglio", "lug", "juillet", "juil", "julio", "juli"],
            ["august", "aug", "agosto", "ago", "aout", "aou"],
            ["september", "sep", "sept", "settembre", "set", "septembre", "septiembre", "setiembre"],
            ["october", "oct", "ottobre", "ott", "octobre", "octubre", "oktober", "okt"],
            ["november", "nov", "novembre", "noviembre"],
            ["december", "dec", "dicembre", "dic", "decembre", "diciembre", "dezember", "dez"],
        ];

        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < names.Length; i++)
        {
            foreach (var name in names[i])
            {
                map[name] = i + 1;
            }
        }

        return map;
    }
}
