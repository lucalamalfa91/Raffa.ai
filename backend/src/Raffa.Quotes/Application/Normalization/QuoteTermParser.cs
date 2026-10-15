using System.Globalization;
using System.Text.RegularExpressions;

namespace Raffa.Quotes.Application.Normalization;

/// <summary>
/// Task F6-T08 (normalization used by the assessment): reads a commitment <b>duration</b> out of the
/// free-text <c>QuoteLine.Term</c> and gives the benchmark one canonical spelling of it,
/// <c>"{n} months"</c>. A sibling of <see cref="QuoteBillingCadence"/>, which answers a different
/// question (how often is it billed); this answers "how long is the commitment".
///
/// <para>
/// <b>Why it exists.</b> The query's <c>Term</c> used to be passed to the Benchmark Service as the
/// quote wrote it. The matchers behind it key on one spelling ("12 months") and the market-feed
/// matcher reads a term by concatenating every digit in the text, so
/// <c>"36 months (3 years)"</c> became 363 and matched nothing, and <c>"3 years"</c> became 3. A
/// duration stated in months or years, in any of the five languages the documents arrive in, now
/// reaches the benchmark as <c>"36 months"</c>.
/// </para>
///
/// <para>
/// <b>Deliberately narrow.</b> Only an explicit number followed by a month or year unit counts. A
/// cadence word ("Annual", "monthly") is not a duration and is left to <see cref="QuoteBillingCadence"/>;
/// "30 days", "net 30", "perpetual" and free text without a number-and-unit pair resolve to
/// <see langword="null"/>, and so does a text that states two <em>different</em> durations ("12 months,
/// option for 3 years"): a guess would be a fabricated term (Appendix C rule 10). The caller then keeps
/// the original text, which is exactly today's behaviour.
/// </para>
/// </summary>
internal static partial class QuoteTermParser
{
    private const int MinMonths = 1;
    private const int MaxMonths = 120;

    private static readonly IReadOnlyDictionary<string, int> NumberWords =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5,
            ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["twelve"] = 12,
        };

    // Month and year words in English, Italian, German, French and Spanish. Word-boundary matched, so
    // "mesi" is a unit and "messaggi" is not.
    private const string MonthUnits = "months?|mos?|mths?|mesi|mese|monate?|mois|meses|mes";
    private const string YearUnits = "years?|yrs?|anni|anno|jahre?|ans?|ann[ée]es?|a[ñn]os?";

    private const string NumberWordAlternation = "one|two|three|four|five|six|seven|eight|nine|ten|twelve";

    [GeneratedRegex(
        $@"(?<![\w.,])(?<n>\d+(?:[.,]\d+)?|{NumberWordAlternation})\s*-?\s*(?<u>{MonthUnits}|{YearUnits})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DurationPattern();

    [GeneratedRegex($@"^(?:{YearUnits})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex YearUnitPattern();

    /// <summary>The commitment length in months, or <see langword="null"/> when <paramref name="term"/>
    /// states none, or states two different ones.</summary>
    public static int? TryParseMonths(string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return null;
        }

        var found = new HashSet<int>();
        foreach (Match match in DurationPattern().Matches(term))
        {
            var number = match.Groups["n"].Value;
            decimal quantity;
            if (NumberWords.TryGetValue(number, out var word))
            {
                quantity = word;
            }
            else if (!decimal.TryParse(
                number.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out quantity))
            {
                continue;
            }

            var isYears = YearUnitPattern().IsMatch(match.Groups["u"].Value);
            var months = quantity * (isYears ? 12m : 1m);

            // "1.5 years" is 18 months; "1.5 months" is not a whole commitment length.
            if (months != decimal.Truncate(months))
            {
                return null;
            }

            found.Add((int)months);
        }

        if (found.Count != 1)
        {
            return null;
        }

        var result = found.Single();
        return result is >= MinMonths and <= MaxMonths ? result : null;
    }

    /// <summary>
    /// The canonical spelling the benchmark is queried with: <c>"{n} months"</c> when
    /// <paramref name="term"/> states a duration, otherwise <paramref name="term"/> exactly as it was
    /// written.
    /// </summary>
    public static string Canonicalize(string term) =>
        TryParseMonths(term) is { } months
            ? string.Create(CultureInfo.InvariantCulture, $"{months} months")
            : term;
}
