using System.Globalization;

namespace Raffa.Savings.Application.Results;

/// <summary>Number formats of <c>savings-quote-v4.js</c> (language-neutral).</summary>
public static class ResultsFormat
{
    /// <summary><c>SQ.k(n)</c>: "CHF 128k", "CHF 2.4M".</summary>
    public static string K(decimal n, string currency = "CHF") =>
        n >= 1_000_000m
            ? currency + " " + (n / 1_000_000m).ToString("0.0", CultureInfo.InvariantCulture) + "M"
            : currency + " " + Math.Round(n / 1000m, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "k";

    /// <summary>A number as JavaScript prints it: 20 → "20", 12.5 → "12.5".</summary>
    public static string Js(decimal n) => n.ToString("0.##########", CultureInfo.InvariantCulture);
}
