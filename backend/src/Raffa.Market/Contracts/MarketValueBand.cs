using System.Globalization;
using System.Text.RegularExpressions;

namespace Raffa.Market.Contracts;

/// <summary>
/// A deal's <see cref="MarketDeal.AnnualValueBand"/> ("100k-250k", "&lt;100k", "5m+") as amounts —
/// parsed once here so the market notes the RAG serves and the Ask market data check read the same
/// band the same way.
/// </summary>
public static class MarketValueBand
{
    private static readonly Regex BandPattern = new(
        @"^\s*(?:(?<lt><)\s*(?<hi1>[\d.]+)\s*(?<u1>[km])|(?<lo>[\d.]+)\s*(?<u2>[km])\s*-\s*(?<hi2>[\d.]+)\s*(?<u3>[km])|(?<lo2>[\d.]+)\s*(?<u4>[km])\s*\+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>"100k-250k" → (100000, 250000); "&lt;100k" → (null, 100000); "5m+" → (5000000,
    /// null); anything else → <see langword="null"/>.</summary>
    public static (decimal? Low, decimal? High)? Parse(string? band)
    {
        if (string.IsNullOrWhiteSpace(band))
        {
            return null;
        }

        var match = BandPattern.Match(band);
        if (!match.Success)
        {
            return null;
        }

        static decimal Scale(string number, string unit) =>
            decimal.Parse(number, NumberStyles.Number, CultureInfo.InvariantCulture) *
            (unit.Equals("m", StringComparison.OrdinalIgnoreCase) ? 1_000_000m : 1_000m);

        if (match.Groups["lt"].Success)
        {
            return (null, Scale(match.Groups["hi1"].Value, match.Groups["u1"].Value));
        }

        if (match.Groups["lo"].Success)
        {
            return (Scale(match.Groups["lo"].Value, match.Groups["u2"].Value), Scale(match.Groups["hi2"].Value, match.Groups["u3"].Value));
        }

        return (Scale(match.Groups["lo2"].Value, match.Groups["u4"].Value), null);
    }

    /// <summary>The band in words with its currency — "EUR 250,000–500,000", "below EUR 100,000",
    /// "above EUR 5,000,000" — or <see langword="null"/> when it does not parse.</summary>
    public static string? Describe(string? band, string currency)
    {
        if (Parse(band) is not { } parsed)
        {
            return null;
        }

        static string Amount(decimal value) => value.ToString("N0", CultureInfo.InvariantCulture);

        return parsed switch
        {
            ({ } low, { } high) => $"{currency} {Amount(low)}–{Amount(high)}",
            (null, { } high) => $"below {currency} {Amount(high)}",
            ({ } low, null) => $"above {currency} {Amount(low)}",
            _ => null,
        };
    }
}
