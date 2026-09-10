using System.Globalization;

namespace Raffa.Market.Contracts;

/// <summary>
/// The one place that formats R-MKT-04's UX provenance label: "Every market number or note shown
/// in Ask, Renewals or Quote check carries the label *representative (mock feed)* and
/// <c>updatedAt</c> until the live API is wired; then the provider name. A precise-looking number
/// without provenance is a defect (ADR-001)." <see cref="Benchmark.MarketFeedBenchmarkAdapter"/>
/// and <see cref="Retrieval.MarketNoteComposer"/> both call this rather than formatting their own
/// copy, so the exact wording only ever needs to change in one place — including the day R-MKT-05's
/// live provider lands and this label's second half ("then the provider name") becomes real.
/// </summary>
public static class MarketProvenance
{
    /// <summary>
    /// Formats <paramref name="deal"/>'s provenance label: <c>"representative market data · mock
    /// feed · updated &lt;yyyy-MM-dd&gt;"</c> (task objective, verbatim). Uses
    /// <see cref="MarketDeal.UpdatedAt"/>, not "now" — the label always reflects when the
    /// <em>data</em> was last refreshed, never when it happened to be read.
    /// </summary>
    public static string Label(MarketDeal deal)
    {
        ArgumentNullException.ThrowIfNull(deal);

        var updatedAt = deal.UpdatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return $"representative market data · mock feed · updated {updatedAt}";
    }
}
