using System.Globalization;
using System.Text.RegularExpressions;

namespace Raffa.Chat.Application.Planning;

/// <summary>
/// Deterministic parser for the quantified goal of a savings question (see
/// <see cref="SavingsGoal"/>). Regex only, IT + EN, no model call: the number the user typed is
/// the number the calculators receive, exactly.
/// </summary>
public static class SavingsGoalParser
{
    // "20k", "20 k", "40K", "1.5M", "€20k", "20.000", "20,000", "20000", "EUR 20000", "20k€".
    private static readonly Regex AmountPattern = new(
        @"(?<cur1>€|eur|usd|chf|gbp|\$|£)?\s?(?<num>\d{1,3}(?:[.,]\d{3})+|\d+(?:[.,]\d+)?)\s?(?<mult>[kKmM])?(?![\d%])\s?(?<cur2>€|eur|usd|chf|gbp|\$|£)?(?![a-zA-Z])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PercentPattern = new(
        @"(?<num>\d+(?:[.,]\d+)?)\s?%",
        RegexOptions.Compiled);

    private static readonly (Regex Pattern, int Days)[] WindowPatterns =
    [
        (new Regex(@"\b(quarter(ly)?|trimestr\w*|q[1-4]\b|prossimi\s+tre\s+mesi|next\s+three\s+months|next\s+3\s+months|prossimi\s+3\s+mesi)", RegexOptions.IgnoreCase | RegexOptions.Compiled), 90),
        (new Regex(@"\b(questo\s+mese|prossimo\s+mese|this\s+month|next\s+month|entro\s+un\s+mese|within\s+a\s+month|30\s+giorni|30\s+days)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 30),
        (new Regex(@"\b(semestr\w*|sei\s+mesi|six\s+months|6\s+mesi|6\s+months|h[12]\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled), 180),
        (new Regex(@"\b(quest'?anno|prossimo\s+anno|this\s+year|next\s+year|entro\s+l'?anno|annual\w*|12\s+mesi|12\s+months|fy\s?\d{2,4})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 365),
    ];

    private static readonly Regex ExplicitDaysPattern = new(
        @"\b(?:entro|within|in|nei\s+prossimi|next)\s+(?<days>\d{1,3})\s+(?:giorni|days)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Parses <paramref name="question"/>; never throws, never returns null — a question
    /// with nothing quantified returns a goal whose <see cref="SavingsGoal.HasTarget"/> is false.</summary>
    public static SavingsGoal Parse(string question)
    {
        ArgumentNullException.ThrowIfNull(question);

        var text = question.Trim();

        decimal? percent = null;
        var percentMatch = PercentPattern.Match(text);
        if (percentMatch.Success && TryParseNumber(percentMatch.Groups["num"].Value, out var pct) && pct is > 0 and <= 100)
        {
            percent = pct;
        }

        // Percentages are never amounts: blank them out before the amount scan so "15%" cannot be
        // read as "15".
        var withoutPercents = PercentPattern.Replace(text, " ");

        decimal? amount = null;
        string? currency = null;
        foreach (Match match in AmountPattern.Matches(withoutPercents))
        {
            if (!TryParseNumber(match.Groups["num"].Value, out var value))
            {
                continue;
            }

            var multiplier = match.Groups["mult"].Value.ToUpperInvariant() switch
            {
                "K" => 1_000m,
                "M" => 1_000_000m,
                _ => 1m,
            };

            var cur = match.Groups["cur1"].Success ? match.Groups["cur1"].Value
                : match.Groups["cur2"].Success ? match.Groups["cur2"].Value
                : null;

            var candidate = value * multiplier;

            // A bare small integer with neither a multiplier nor a currency ("i 3 contratti",
            // "Q2", "2027") is not a saving target; a k/M suffix or a currency sign always is.
            var isPlausibleTarget = multiplier > 1m || cur is not null || candidate >= 1_000m && !LooksLikeYear(match.Groups["num"].Value);
            if (!isPlausibleTarget)
            {
                continue;
            }

            amount = candidate;
            currency = NormalizeCurrency(cur);
            break;
        }

        int? windowDays = null;
        string? windowLabel = null;
        var explicitDays = ExplicitDaysPattern.Match(text);
        if (explicitDays.Success && int.TryParse(explicitDays.Groups["days"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var days) && days > 0)
        {
            windowDays = days;
            windowLabel = explicitDays.Value.Trim();
        }
        else
        {
            foreach (var (pattern, daysForPattern) in WindowPatterns)
            {
                var windowMatch = pattern.Match(text);
                if (windowMatch.Success)
                {
                    windowDays = daysForPattern;
                    windowLabel = windowMatch.Value.Trim();
                    break;
                }
            }
        }

        return new SavingsGoal(amount, percent, currency, windowDays, windowLabel);
    }

    private static bool LooksLikeYear(string digits) =>
        digits.Length == 4 && !digits.Contains('.') && !digits.Contains(',') &&
        int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var year) && year is >= 1990 and <= 2100;

    private static bool TryParseNumber(string raw, out decimal value)
    {
        var s = raw.Trim();

        // Thousands groups: "20.000", "1,250,000" → strip the separators.
        if (Regex.IsMatch(s, @"^\d{1,3}(?:[.,]\d{3})+$"))
        {
            s = s.Replace(".", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else
        {
            // A decimal comma ("1,5M", "8,7") becomes a decimal point.
            s = s.Replace(',', '.');
        }

        return decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    private static string? NormalizeCurrency(string? symbol) => symbol?.ToUpperInvariant() switch
    {
        "€" or "EUR" => "EUR",
        "$" or "USD" => "USD",
        "£" or "GBP" => "GBP",
        "CHF" => "CHF",
        _ => null,
    };
}
