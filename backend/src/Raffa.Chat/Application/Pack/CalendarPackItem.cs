using System.Globalization;
using System.Text.RegularExpressions;

namespace Raffa.Chat.Application.Pack;

/// <summary>
/// Today's date and the calendar quarter around it, as one citable <see cref="PackCorpus.Calc"/>
/// item. A question about a period ("Where can I save the most this quarter?", "Qual è la data
/// esatta di fine quarter?") needs dates no contract carries — without this item the model could
/// only decline or compute a date itself, which <c>Guards.NumericGuard</c> rightly rejects. With it,
/// the quarter's start and end are pack values like any other: stated verbatim, cited, checked.
/// Pure — the caller supplies <c>today</c> from its own clock.
/// </summary>
public static class CalendarPackItem
{
    /// <summary>The item's citation key (never shown to a reader — R-ASK-08).</summary>
    public const string CitationKey = "calc:calendar";

    // A named period: a quarter in either language (quarter, quarterly, trimestre, Q3), today, the
    // current week/month/year, or the end of one.
    private static readonly Regex PeriodCue = new(
        @"\b(quarter\w*|trimestr\w*|q[1-4]|oggi|today|this\s+(?:week|month|year)|quest(?:[oa]\s+|['’])(?:settimana|mese|anno)|" +
        @"fine\s+(?:del(?:l')?\s*)?(?:anno|mese|trimestre)|end\s+of\s+(?:the\s+)?(?:year|month|quarter))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Whether <paramref name="question"/> names a period this item's dates answer.</summary>
    public static bool IsPeriodQuestion(string question) =>
        !string.IsNullOrWhiteSpace(question) && PeriodCue.IsMatch(question);

    /// <summary>The calendar item for <paramref name="today"/>: today, the current calendar
    /// quarter's first and last day and the days left in it, the next quarter, and the year end.</summary>
    public static PackItem Build(DateOnly today)
    {
        var quarter = (today.Month - 1) / 3 + 1;
        var quarterStart = new DateOnly(today.Year, (quarter - 1) * 3 + 1, 1);
        var quarterEnd = quarterStart.AddMonths(3).AddDays(-1);
        var nextQuarterStart = quarterEnd.AddDays(1);
        var nextQuarterEnd = nextQuarterStart.AddMonths(3).AddDays(-1);
        var nextQuarter = quarter % 4 + 1;
        var yearEnd = new DateOnly(today.Year, 12, 31);
        var daysLeft = quarterEnd.DayNumber - today.DayNumber;

        static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var snippet =
            $"Today is {Iso(today)}. The current calendar quarter is Q{quarter} {today.Year}, from " +
            $"{Iso(quarterStart)} to {Iso(quarterEnd)} ({daysLeft} days left). The next quarter is " +
            $"Q{nextQuarter} {nextQuarterStart.Year}, from {Iso(nextQuarterStart)} to {Iso(nextQuarterEnd)}. " +
            $"The calendar year ends {Iso(yearEnd)}.";

        return new PackItem(
            CitationKey,
            PackCorpus.Calc,
            "Calendar · today and quarter dates",
            null,
            null,
            null,
            snippet,
            null, // Href is always null for PackCorpus.Calc (PackItem.Href's own doc comment).
            null,
            null,
            "deterministic calculator",
            [
                new PackValue("today", Iso(today), PackValueKind.Date),
                new PackValue("quarterStart", Iso(quarterStart), PackValueKind.Date),
                new PackValue("quarterEnd", Iso(quarterEnd), PackValueKind.Date),
                new PackValue("daysLeftInQuarter", daysLeft.ToString(CultureInfo.InvariantCulture), PackValueKind.Number),
                new PackValue("nextQuarterStart", Iso(nextQuarterStart), PackValueKind.Date),
                new PackValue("nextQuarterEnd", Iso(nextQuarterEnd), PackValueKind.Date),
                new PackValue("yearEnd", Iso(yearEnd), PackValueKind.Date),
            ]);
    }
}
