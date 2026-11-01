using System.Globalization;

namespace Raffa.Quotes.Application.AssessmentV4;

/// <summary>The prototype's number/date helpers (<c>SQ.k / num / up / pct / pd</c>, savings-quote-v4.js), byte-equal.</summary>
public static class QuoteFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>JavaScript <c>Math.round</c> (half toward +∞).</summary>
    public static double JsRound(double x) => Math.Floor(x + 0.5);

    /// <summary><c>SQ.k</c>: "CHF 520k" / "CHF 1.2M".</summary>
    public static string K(decimal n) =>
        n >= 1_000_000m
            ? "CHF " + Math.Round((double)n / 1e6, 1, MidpointRounding.AwayFromZero).ToString("0.0", Inv) + "M"
            : "CHF " + JsRound((double)n / 1000).ToString("0", Inv) + "k";

    /// <summary><c>SQ.num</c>: rounded, en-US thousands ("600,000").</summary>
    public static string Num(decimal n) => JsRound((double)n).ToString("#,0", Inv);

    /// <summary><c>SQ.up</c>: a unit price — thousands-separated from 1,000, else JavaScript <c>String(n)</c> ("0.7").</summary>
    public static string Up(decimal n) => n >= 1000m ? Num(n) : ((double)n).ToString("R", Inv);

    /// <summary><c>SQ.pct</c>: "+3.4%", "−" kept as "-" like the prototype ("-10%").</summary>
    public static string Pct(double d) =>
        (d > 0 ? "+" : "") +
        (Math.Abs(d) < 10
            ? Math.Round(d, 1, MidpointRounding.AwayFromZero).ToString("0.0", Inv)
            : JsRound(d).ToString("0", Inv)) + "%";

    /// <summary><c>SQ.pd</c>: "30 Sep 2026" → date.</summary>
    public static DateOnly? Pd(string? s) =>
        DateOnly.TryParseExact(s, "dd MMM yyyy", Inv, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>The prototype's display date ("30 Sep 2026").</summary>
    public static string Display(DateOnly d) => d.ToString("dd MMM yyyy", Inv);
}
