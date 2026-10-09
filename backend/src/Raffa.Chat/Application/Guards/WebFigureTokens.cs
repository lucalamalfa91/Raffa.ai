using System.Globalization;
using System.Text.RegularExpressions;
using Raffa.Chat.Application.Guards;

namespace Raffa.Chat.Application.Guards;

/// <summary>A month and a year with no day (<c>March 2027</c>, <c>marzo 2027</c>, <c>mars 2027</c>, <c>marzo de
/// 2027</c>, <c>März 2027</c>).</summary>
public sealed record MonthYearToken(string Raw, int Month, int Year) : NumericToken(Raw);

/// <summary>A range's bounds as text a normal extractor can read: <c>5-10%</c> gives <c>5% ; 10%</c>,
/// <c>$10-15</c> gives <c>$10 ; $15</c>. <see cref="Raw"/> is the range as written.</summary>
public sealed record RangeBound(string Raw, string Synthetic);

/// <summary>
/// The figure shapes only the web guard reads on top of <see cref="NumericTokenExtractor"/> (F3-D02):
/// a month and year without a day, a percentage the model spelled out, and the lower bound of a range
/// whose unit is written once (<c>5-10%</c>). They live apart from <see cref="NumericTokenExtractor"/>
/// so the answer-role <see cref="NumericGuard"/> keeps checking exactly what it checked before. Pure.
/// </summary>
public static class WebFigureTokens
{
    private const string Space = @"[   ]";

    private static readonly Regex MonthYearPattern = new(
        @"(?<![\p{L}\d])(?<m>\p{L}{3,10})\.?(?:\s+(?:de|del|of)\s+|\s+)(?<y>(?:19|20)\d{2})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // The text just before a "month year" that makes it the tail of a full date ("15 March 2027").
    private static readonly Regex DayBeforePattern = new(
        @"\d{1,2}(?:st|nd|rd|th|er|º|°)?\.?\s+(?:(?:de|del|of)\s+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex PercentWordPattern = new(
        @"(?<![\p{L}])(?:percent|per\s?cent|per\s?cento|pour\s?cent|por\s?ciento|prozent)(?![\p{L}])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex WordPattern = new(@"\S+", RegexOptions.Compiled | RegexOptions.RightToLeft);

    private const string RangePre = @"(?:[€$£]|(?:CHF|EUR|USD|GBP)" + Space + "?)";

    private const string RangePost =
        @"(?:" + Space + @"?(?:%|[€$£]|(?:CHF|EUR|USD|GBP)(?![\p{L}])|(?i:percent|per\s?cent|per\s?cento|pour\s?cent|por\s?ciento|prozent|" +
        @"euros?|euri|dollars?|dollari|dólares|pounds?|sterline|libras?|francs?|franchi|francos?|franken)(?![\p{L}])))";

    private static readonly Regex RangePattern = new(
        @"(?<![\d.,])(?<pre1>" + RangePre + @")?(?<lo>" + NumericTokenExtractor.Number + @")(?<lomag>" + NumericTokenExtractor.Magnitude + @")?" +
        @"(?:" + Space + @"?[-–—]" + Space + @"?|" + Space + @"+(?i:to|a|à|au|bis|and|et|und|e|y)" + Space + @"+)" +
        @"(?<pre2>" + RangePre + @")?(?<hi>" + NumericTokenExtractor.Number + @")(?<himag>" + NumericTokenExtractor.Magnitude + @")?" +
        @"(?<post>" + RangePost + @")?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Every month-and-year in <paramref name="text"/> that is not the tail of a full date.</summary>
    public static IReadOnlyList<MonthYearToken> MonthYears(string? text)
    {
        text ??= string.Empty;
        var found = new List<MonthYearToken>();
        foreach (Match match in MonthYearPattern.Matches(text))
        {
            if (NumericTokenExtractor.MonthNumber(match.Groups["m"].Value) is not { } month ||
                DayBeforePattern.IsMatch(text[..match.Index]))
            {
                continue;
            }

            found.Add(new MonthYearToken(
                match.Value.Trim(), month, int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture)));
        }

        return found;
    }

    /// <summary>Every percentage written with number words (<c>ten percent</c>, <c>venticinque per
    /// cento</c>, <c>vingt-cinq pour cent</c>, <c>treinta y cinco por ciento</c>, <c>fünfundzwanzig
    /// Prozent</c>) as a <see cref="PercentToken"/>. A phrase that is not wholly number words is skipped.</summary>
    public static IReadOnlyList<PercentToken> SpelledPercentages(string? text)
    {
        text ??= string.Empty;
        var found = new List<PercentToken>();
        foreach (Match match in PercentWordPattern.Matches(text))
        {
            var before = text[..match.Index];
            // Walk back over the words just before the percent word; stop at the first that is not a
            // number word, so "up to five and ten percent" reads ten, not fifteen.
            var tokens = WordPattern.Matches(before).Take(6).ToList();
            var run = new List<Match>();
            for (var i = 0; i < tokens.Count; i++)
            {
                // Words must be adjacent: only whitespace between them (and the percent word).
                var token = tokens[i];
                var follows = i == 0 ? match.Index : tokens[i - 1].Index;
                if (!string.IsNullOrWhiteSpace(text.Substring(token.Index + token.Length, follows - token.Index - token.Length)))
                {
                    break;
                }

                var word = token.Value;
                if (NumberWords.IsNumberWord(word))
                {
                    run.Insert(0, token);
                    continue;
                }

                if (NumberWords.IsConnector(word) && run.Count > 0 && i + 1 < tokens.Count &&
                    NumberWords.IsNumberWord(tokens[i + 1].Value) && ConnectorJoins(tokens[i + 1].Value, run[0].Value))
                {
                    run.Insert(0, token);
                    continue;
                }

                break;
            }

            if (run.Count == 0 || !NumberWords.TryEvaluate(run.Select(t => t.Value).ToList(), out var value))
            {
                continue;
            }

            var start = run[0].Index;
            found.Add(new PercentToken(text[start..(match.Index + match.Length)].Trim(), value, 0.01m));
        }

        return found;
    }

    /// <summary>The bounds of every range whose unit or currency is written once (<c>5-10%</c>,
    /// <c>$10-15</c>, <c>10 to 15 EUR</c>, <c>da 5 a 10 per cento</c>), as text carrying the unit on both
    /// bounds, so the lower bound is checked as strictly as the upper one.</summary>
    public static IReadOnlyList<RangeBound> RangeBounds(string? text)
    {
        text ??= string.Empty;
        var found = new List<RangeBound>();
        foreach (Match match in RangePattern.Matches(text))
        {
            var pre = match.Groups["pre2"].Success ? match.Groups["pre2"].Value : match.Groups["pre1"].Value;
            var post = match.Groups["post"].Value;
            if (pre.Length == 0 && post.Length == 0)
            {
                continue;
            }

            var loMag = match.Groups["lomag"].Success ? match.Groups["lomag"].Value : match.Groups["himag"].Value;
            var hiMag = match.Groups["himag"].Value;
            var lower = pre + match.Groups["lo"].Value + loMag + post;
            var upper = pre + match.Groups["hi"].Value + hiMag + post;
            found.Add(new RangeBound(match.Value.Trim(), lower + " ; " + upper));
        }

        return found;
    }

    // "hundred and fifty", "treinta y cinco", "vingt et un": the connector joins a hundred to what
    // follows or tens to units; anything else ("five and ten") is two numbers.
    private static bool ConnectorJoins(string left, string right)
    {
        var tail = NumberWords.TailValue(left);
        var lead = NumberWords.LeadValue(right);
        return (tail == 100 && lead is >= 1 and <= 99) || (tail is >= 20 and <= 90 && lead is >= 1 and <= 9);
    }
}
