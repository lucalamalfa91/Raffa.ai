using System.Globalization;
using Raffa.AiFlows.Shared.Pack;

namespace Raffa.AiFlows.CapabilityGaps.Drafting;

/// <summary>
/// The reply-by calculator (plan F4-D14, decision D4: the calculator only; no sequence of emails, no
/// reminder, nothing sent): the date by which a supplier is asked to answer a communication, a number of
/// business days after it is sent. Business days exclude Saturdays, Sundays and the <em>national</em>
/// public holidays of the jurisdiction (Italy, France, Spain, Germany); regional and local holidays are
/// not known, so the date is a suggestion for the draft, never a legal deadline (the contract's own
/// notice clause decides those). A jurisdiction it does not know excludes weekends only. Pure and
/// deterministic: the same inputs give the same date.
/// </summary>
public static class ReplyByCalculator
{
    /// <summary>The citation key of <see cref="BuildItem"/>.</summary>
    public const string CitationKey = "calc:reply-by";

    /// <summary>The longest span accepted, in business days.</summary>
    public const int MaxBusinessDays = 260;

    /// <summary>The jurisdictions whose national holidays are known.</summary>
    public static IReadOnlyList<string> Jurisdictions { get; } = ["IT", "FR", "ES", "DE"];

    /// <summary>The upper-case country code <paramref name="jurisdiction"/> names (a bare code or a tag such
    /// as <c>fr-FR</c>), or <see langword="null"/> when it is blank or not one of <see cref="Jurisdictions"/>.</summary>
    public static string? Normalize(string? jurisdiction)
    {
        var code = jurisdiction?.Trim().Split('-', '_')[^1];
        return Jurisdictions.FirstOrDefault(j => string.Equals(j, code, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The national public holidays of <paramref name="jurisdiction"/> in <paramref name="year"/>
    /// (empty for an unknown jurisdiction).</summary>
    public static IReadOnlySet<DateOnly> Holidays(int year, string? jurisdiction)
    {
        var easter = EasterSunday(year);
        var days = new HashSet<DateOnly>();

        void Fixed(int month, int day) => days.Add(new DateOnly(year, month, day));
        void Moving(int offsetFromEaster) => days.Add(easter.AddDays(offsetFromEaster));

        switch (Normalize(jurisdiction))
        {
            case "IT":
                Fixed(1, 1);
                Fixed(1, 6);
                Moving(1);
                Fixed(4, 25);
                Fixed(5, 1);
                Fixed(6, 2);
                Fixed(8, 15);
                Fixed(11, 1);
                Fixed(12, 8);
                Fixed(12, 25);
                Fixed(12, 26);
                break;
            case "FR":
                Fixed(1, 1);
                Moving(1);
                Fixed(5, 1);
                Fixed(5, 8);
                Moving(39);
                Moving(50);
                Fixed(7, 14);
                Fixed(8, 15);
                Fixed(11, 1);
                Fixed(11, 11);
                Fixed(12, 25);
                break;
            case "ES":
                Fixed(1, 1);
                Fixed(1, 6);
                Moving(-2);
                Fixed(5, 1);
                Fixed(8, 15);
                Fixed(10, 12);
                Fixed(11, 1);
                Fixed(12, 6);
                Fixed(12, 8);
                Fixed(12, 25);
                break;
            case "DE":
                Fixed(1, 1);
                Moving(-2);
                Moving(1);
                Fixed(5, 1);
                Moving(39);
                Moving(50);
                Fixed(10, 3);
                Fixed(12, 25);
                Fixed(12, 26);
                break;
        }

        return days;
    }

    /// <summary>Whether <paramref name="date"/> is neither a weekend day nor a national holiday.</summary>
    public static bool IsBusinessDay(DateOnly date, string? jurisdiction) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
        && !Holidays(date.Year, jurisdiction).Contains(date);

    /// <summary>
    /// The date <paramref name="businessDays"/> business days after <paramref name="sentOn"/> (the day it is
    /// sent is not counted). With zero days it is the first business day on or after <paramref name="sentOn"/>.
    /// </summary>
    public static DateOnly Calculate(DateOnly sentOn, int businessDays, string? jurisdiction)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(businessDays);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(businessDays, MaxBusinessDays);

        var date = sentOn;
        if (businessDays == 0)
        {
            while (!IsBusinessDay(date, jurisdiction))
            {
                date = date.AddDays(1);
            }

            return date;
        }

        var remaining = businessDays;
        while (remaining > 0)
        {
            date = date.AddDays(1);
            if (IsBusinessDay(date, jurisdiction))
            {
                remaining--;
            }
        }

        return date;
    }

    /// <summary>The calculator's result as a pack item (<see cref="CitationKey"/>): the reply-by date, the
    /// day it counts from and the number of business days, so a draft that quotes them passes the
    /// numeric guard.</summary>
    public static PackItem BuildItem(DateOnly sentOn, int businessDays, string? jurisdiction)
    {
        var replyBy = Calculate(sentOn, businessDays, jurisdiction);
        var known = Normalize(jurisdiction);
        var from = sentOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var by = replyBy.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var days = businessDays.ToString(CultureInfo.InvariantCulture);

        var snippet = known is null
            ? $"Reply by {by}: {days} business days after {from}, weekends excluded."
            : $"Reply by {by}: {days} business days after {from}, weekends and {known} national public holidays excluded.";

        return new PackItem(
            CitationKey,
            PackCorpus.Calc,
            "Suggested reply-by date",
            "deterministic calculator",
            null,
            null,
            snippet,
            null,
            null,
            null,
            "deterministic calculator",
            [
                new PackValue("replyBy", by, PackValueKind.Date),
                new PackValue("sentOn", from, PackValueKind.Date),
                new PackValue("businessDays", days, PackValueKind.Number),
            ]);
    }

    /// <summary>Easter Sunday of <paramref name="year"/> (Gregorian calendar; Meeus/Jones/Butcher).</summary>
    internal static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var month = (h + l - (7 * m) + 114) / 31;
        var day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}
