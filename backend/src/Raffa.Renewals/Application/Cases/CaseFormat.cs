using System.Globalization;
using System.Text.RegularExpressions;
using Raffa.SharedKernel.Text;

namespace Raffa.Renewals.Application.Cases;

/// <summary>
/// Formatting and calendar helpers with the prototype's exact semantics (<c>renewal-case-v4.js</c>
/// <c>fd / addD / addM / daysTo / num / chf</c>): dates print "08 Sep 2026", months overflow like
/// JavaScript's <c>Date.setMonth</c> (31 Jan + 1 month = 03 Mar), money rounds half up.
/// </summary>
public static partial class CaseFormat
{
    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>"08 Sep 2026".</summary>
    public static string Date(DateOnly d) =>
        (d.Day < 10 ? "0" : string.Empty) + d.Day.ToString(CultureInfo.InvariantCulture) + " " + Months[d.Month - 1] + " " +
        d.Year.ToString(CultureInfo.InvariantCulture);

    /// <summary>Parses "8 Sep 2026" / "08 Sep 2026".</summary>
    public static DateOnly ParseDate(string s)
    {
        var m = DateRegex().Match(s.Trim());
        if (!m.Success)
        {
            throw new FormatException($"Unrecognised case date: \"{s}\"");
        }

        var month = Array.IndexOf(Months, m.Groups[2].Value) + 1;
        if (month == 0)
        {
            throw new FormatException($"Unrecognised month in case date: \"{s}\"");
        }

        return new DateOnly(int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), month, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    public static DateOnly AddDays(DateOnly d, int n) => d.AddDays(n);

    /// <summary>JavaScript <c>setMonth(getMonth()+n)</c>: the day overflows into the next month.</summary>
    public static DateOnly AddMonths(DateOnly d, int n)
    {
        var first = new DateOnly(d.Year, d.Month, 1).AddMonths(n);
        return first.AddDays(d.Day - 1);
    }

    public static int DaysBetween(DateOnly from, DateOnly to) => to.DayNumber - from.DayNumber;

    /// <summary>JavaScript <c>Math.round</c> (half up, toward +∞).</summary>
    public static double Round(double x) => Math.Floor(x + 0.5);

    /// <summary><c>Math.round(n).toLocaleString('en-US')</c>: "640,000".</summary>
    public static string Num(double n) => Round(n).ToString("#,##0", CultureInfo.InvariantCulture);

    public static string Chf(double n) => "CHF " + Num(n);

    /// <summary>JavaScript <c>String(n)</c>.</summary>
    public static string Js(double n) => CaseText.JsNumber(n);

    public static string Js(int n) => n.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(\d+) (\w+) (\d+)$")]
    private static partial Regex DateRegex();
}
