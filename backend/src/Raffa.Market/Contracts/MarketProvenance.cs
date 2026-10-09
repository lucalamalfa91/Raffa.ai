using System.Globalization;
using Raffa.SharedKernel.Market;

namespace Raffa.Market.Contracts;

/// <summary>
/// The one place that formats R-MKT-04's UX provenance label: "Every market number or note shown
/// in Ask, Renewals or Quote check carries the label *representative (mock feed)* and
/// <c>updatedAt</c> until the live API is wired; then the provider name. A precise-looking number
/// without provenance is a defect (ADR-001)." <see cref="Benchmark.MarketFeedBenchmarkAdapter"/>
/// and <see cref="Retrieval.MarketNoteComposer"/> both call this rather than formatting their own
/// copy, so the exact wording only ever needs to change in one place — including the day R-MKT-05's
/// live provider lands and this label's second half ("then the provider name") becomes real.
///
/// <para>
/// Next to the label, <see cref="Info"/> gives the same provenance as a structured
/// <see cref="MarketProvenanceInfo"/> (F7-T11 / F7-D04) so callers and tests read fields rather than
/// parse the label. The structured form is internal metadata: it is never rendered by itself
/// (decision D5), and <see cref="Display"/> is the single, configuration-gated way to turn it into a
/// line for the user.
/// </para>
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

    /// <summary>
    /// The structured provenance of <paramref name="deal"/>: the fields code and tests read instead
    /// of parsing <see cref="Label"/>. Pure metadata — it decides nothing about what the user sees
    /// (decision D5: no "demo" badge, no environment gate).
    /// </summary>
    public static MarketProvenanceInfo Info(MarketDeal deal)
    {
        ArgumentNullException.ThrowIfNull(deal);

        var sourceClass = ClassifySource(deal.Source);
        return new MarketProvenanceInfo(
            sourceClass,
            IsDemo: sourceClass == MarketSourceClasses.Mock,
            N: deal.SampleSize,
            AsOf: deal.UpdatedAt,
            Unit: string.IsNullOrWhiteSpace(deal.UnitMetric) ? null : deal.UnitMetric.Trim(),
            Source: deal.Source);
    }

    /// <summary>
    /// The provenance of a figure built from several records (a bundle): the sample is the smallest
    /// component's, the as-of date the oldest's, the class the shared one — or
    /// <see cref="MarketSourceClasses.Mixed"/> — and it is demo as soon as one component is.
    /// </summary>
    public static MarketProvenanceInfo Combine(IReadOnlyList<MarketDeal> deals)
    {
        ArgumentNullException.ThrowIfNull(deals);
        if (deals.Count == 0)
        {
            throw new ArgumentException("At least one record is required.", nameof(deals));
        }

        var infos = deals.Select(Info).ToList();
        var classes = infos.Select(i => i.SourceClass).Distinct(StringComparer.Ordinal).ToList();
        var units = infos.Select(i => i.Unit).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        return new MarketProvenanceInfo(
            classes.Count == 1 ? classes[0] : MarketSourceClasses.Mixed,
            infos.Any(i => i.IsDemo),
            infos.Min(i => i.N),
            infos.Min(i => i.AsOf),
            units.Count == 0 ? null : string.Join(" + ", units),
            string.Join("+", infos.Select(i => i.Source).Distinct(StringComparer.Ordinal)));
    }

    /// <summary>
    /// Maps a record's free-text <see cref="MarketDeal.Source"/> onto <see cref="MarketSourceClasses"/>.
    /// An unknown value is a third-party provider, never silently a demo.
    /// </summary>
    public static string ClassifySource(string? source)
    {
        var normalized = (source ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized switch
        {
            "mock" or "demo" or "fixture" or "synthetic" => MarketSourceClasses.Mock,
            "list_price" or "listino" or "price_list" => MarketSourceClasses.ListPrice,
            "public_procurement" or "pa" or "consip" or "anac" or "ted" => MarketSourceClasses.PublicProcurement,
            "tenant_pool" or "pool" => MarketSourceClasses.TenantPool,
            _ => MarketSourceClasses.Provider,
        };
    }

    /// <summary>
    /// The optional user-facing line derived from <paramref name="info"/>, or <see langword="null"/>
    /// — which is the default: nothing is shown unless <see cref="MarketProvenanceOptions.ShowLabel"/>
    /// is switched on (decision D5). A configured <see cref="MarketProvenanceOptions.RealSourceName"/>
    /// replaces the internal source name once real data is behind the figure, without touching the
    /// stored data.
    /// </summary>
    public static string? Display(MarketProvenanceInfo info, MarketProvenanceOptions? options)
    {
        ArgumentNullException.ThrowIfNull(info);

        if (options is not { ShowLabel: true })
        {
            return null;
        }

        var parts = new List<string>();
        if (info.IsDemo && !string.IsNullOrWhiteSpace(options.DemoLabel))
        {
            parts.Add(options.DemoLabel.Trim());
        }
        else
        {
            parts.Add(string.IsNullOrWhiteSpace(options.RealSourceName) ? info.Source : options.RealSourceName.Trim());
        }

        if (info.N > 0)
        {
            parts.Add($"n={info.N.ToString(CultureInfo.InvariantCulture)}");
        }

        parts.Add(info.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return string.Join(" · ", parts);
    }
}

/// <summary>
/// Configuration (<c>Market:Provenance</c>) for the optional provenance line
/// (<see cref="MarketProvenance.Display"/>). Everything is off by default: market figures are
/// presented as market data and the structured <see cref="MarketProvenanceInfo"/> stays internal
/// (decision D5). Flipping a switch here is the whole change needed to show a label or a real
/// source later — the stored records are not touched.
/// </summary>
public sealed class MarketProvenanceOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Market:Provenance";

    /// <summary>When <see langword="true"/>, <see cref="MarketProvenance.Display"/> returns a line.</summary>
    public bool ShowLabel { get; init; }

    /// <summary>Name of the real source to show instead of the internal one (e.g. a listing or
    /// procurement dataset). Only used when <see cref="ShowLabel"/> is on.</summary>
    public string? RealSourceName { get; init; }

    /// <summary>Text to show for demo figures (e.g. an explicit "illustrative data" label). Only used
    /// when <see cref="ShowLabel"/> is on; unset means a demo figure is labelled like any other.</summary>
    public string? DemoLabel { get; init; }
}
