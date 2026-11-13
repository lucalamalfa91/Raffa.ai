using System.Globalization;
using System.Xml.Linq;

namespace Raffa.Api.Infrastructure;

/// <summary>The rates one publication carries: units of each currency per one euro, and their day.</summary>
internal sealed record EcbReferenceRates(DateOnly RateDate, IReadOnlyDictionary<string, decimal> PerEur);

/// <summary>
/// Downloads the European Central Bank's daily euro foreign exchange reference rates
/// (<c>eurofxref-daily.xml</c>: public, free, no key; published on TARGET working days around 16:00 CET).
/// </summary>
internal sealed class EcbReferenceRateClient(HttpClient http, FxReferenceRateOptions options)
{
    public const string Source = "ECB";

    public async Task<EcbReferenceRates> FetchAsync(CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(options.EcbDailyUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var xml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return Parse(xml);
    }

    /// <summary>Reads the ECB envelope; throws <see cref="FormatException"/> when it carries no dated rates.</summary>
    internal static EcbReferenceRates Parse(string xml)
    {
        var document = XDocument.Parse(xml);
        var dated = document.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Cube" && e.Attribute("time") is not null)
            ?? throw new FormatException("The ECB feed carries no dated rates.");
        var date = DateOnly.ParseExact(dated.Attribute("time")!.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        var rates = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var cube in dated.Elements().Where(e => e.Name.LocalName == "Cube"))
        {
            var code = cube.Attribute("currency")?.Value.Trim().ToUpperInvariant();
            var text = cube.Attribute("rate")?.Value;
            if (code is { Length: 3 }
                && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate)
                && rate > 0)
            {
                rates[code] = rate;
            }
        }

        if (rates.Count == 0)
        {
            throw new FormatException("The ECB feed carries no rates.");
        }

        return new EcbReferenceRates(date, rates);
    }
}
