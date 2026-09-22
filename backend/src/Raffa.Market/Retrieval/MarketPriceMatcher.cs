using System.Globalization;
using Raffa.Market.Contracts;
using Raffa.SharedKernel.Market;

namespace Raffa.Market.Retrieval;

/// <summary>
/// <see cref="IMarketPriceMatcher"/> over the shared market corpus (<see cref="IMarketDealLookup"/>:
/// the persisted <c>market_record</c> rows when the Market database is composed in, the provider's
/// own feed otherwise). Deterministic on purpose: the vector index is good at "what is this note
/// about" but cannot tell <c>Sales Cloud Enterprise</c> from <c>Sales Cloud Unlimited</c>, and a
/// market price for the wrong edition is worse than no price (ADR-001: never a precise-looking
/// number that is not what it claims to be). So a line is matched only when:
/// <list type="number">
/// <item>the record's supplier is the contract's supplier (<see cref="MarketSupplierMatch"/>, the
/// same legal-suffix-insensitive rule Ask's deal lookup uses);</item>
/// <item>the line names the record's product: every word of the market product name appears in
/// the line's description/SKU (<c>"Sales Cloud Unlimited named users / licenses"</c> carries
/// <c>Sales Cloud Unlimited</c>), or the SKUs are equal — the longest product name wins, so an
/// edition beats its family;</item>
/// <item>the record is priced in the contract's own currency — no FX conversion exists anywhere
/// in this corpus, so a GBP line is only ever compared with GBP records; the currency's home
/// region (GBP→UK, CHF→CH, EUR→EU, USD→US) is preferred over another region sharing it;</item>
/// <item>the record has at least <see cref="MinimumSampleSize"/> comparables (the
/// market-feed adapter's own abstain bar).</item>
/// </list>
/// Among the survivors: the contract's own term first (else the nearest), then the larger sample,
/// then the fresher record — so the same corpus always yields the same match.
/// </summary>
public sealed class MarketPriceMatcher(IMarketDealLookup dealLookup) : IMarketPriceMatcher
{
    /// <summary>Same bar as <c>MarketFeedBenchmarkAdapter.MinimumViableSampleSize</c>.</summary>
    public const int MinimumSampleSize = 5;

    private static readonly Dictionary<string, string> HomeRegionByCurrency = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GBP"] = "UK",
        ["CHF"] = "CH",
        ["EUR"] = "EU",
        ["USD"] = "US",
    };

    /// <inheritdoc />
    public async Task<IReadOnlyList<MarketPriceMatch?>> MatchAsync(
        MarketPriceContext context,
        IReadOnlyList<MarketPriceLine> lines,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(context.SupplierName) || string.IsNullOrWhiteSpace(context.Currency))
        {
            return new MarketPriceMatch?[lines.Count];
        }

        var deals = await dealLookup
            .GetBySupplierAsync(context.SupplierName, cancellationToken)
            .ConfigureAwait(false);

        return Match(context, lines, deals);
    }

    /// <summary>The pure half of <see cref="MatchAsync"/>: <paramref name="supplierDeals"/> is the
    /// supplier's slice of the corpus.</summary>
    public static IReadOnlyList<MarketPriceMatch?> Match(
        MarketPriceContext context,
        IReadOnlyList<MarketPriceLine> lines,
        IReadOnlyList<MarketDeal> supplierDeals)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(supplierDeals);

        var currency = context.Currency.Trim();
        HomeRegionByCurrency.TryGetValue(currency, out var homeRegion);
        var supplierWords = Words(context.SupplierName ?? string.Empty);

        var priced = supplierDeals
            .Where(d => string.Equals(d.Currency, currency, StringComparison.OrdinalIgnoreCase)
                && d.SampleSize >= MinimumSampleSize)
            .ToList();

        var results = new MarketPriceMatch?[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var best = priced
                .Select(deal => (Deal: deal, Specificity: ProductSpecificity(deal, line, supplierWords)))
                .Where(x => x.Specificity > 0)
                .OrderByDescending(x => x.Specificity)
                .ThenByDescending(x => homeRegion is not null
                    && string.Equals(x.Deal.Geography, homeRegion, StringComparison.OrdinalIgnoreCase))
                .ThenBy(x => context.TermMonths is { } term ? Math.Abs(x.Deal.TermMonths - term) : 0)
                .ThenByDescending(x => x.Deal.SampleSize)
                .ThenByDescending(x => x.Deal.UpdatedAt)
                .ThenBy(x => x.Deal.RecordId, StringComparer.Ordinal)
                .Select(x => x.Deal)
                .FirstOrDefault();

            results[i] = best is null ? null : ToMatch(best);
        }

        return results;
    }

    /// <summary>
    /// How specifically <paramref name="line"/> names <paramref name="deal"/>'s product: <c>0</c>
    /// when it does not; otherwise higher for an equal SKU, then for a longer product name (an
    /// edition, <c>Sales Cloud Unlimited</c>, beats a family name a record might also carry).
    /// </summary>
    public static int ProductSpecificity(MarketDeal deal, MarketPriceLine line, IReadOnlySet<string> supplierWords)
    {
        ArgumentNullException.ThrowIfNull(deal);
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(supplierWords);

        if (NormalizeSku(deal.Sku) is { } dealSku && NormalizeSku(line.Sku) is { } lineSku && dealSku == lineSku)
        {
            return 1000;
        }

        var productWords = Words(deal.Product);
        productWords.ExceptWith(supplierWords);
        if (productWords.Count == 0)
        {
            return 0;
        }

        var lineWords = Words($"{line.Description} {line.Sku}");
        return productWords.IsSubsetOf(lineWords) ? productWords.Count : 0;
    }

    /// <summary>Lower-cased alphanumeric words, a trailing plural "s" dropped from longer words so
    /// "licenses"/"license" and "credits"/"credit" read the same.</summary>
    public static HashSet<string> Words(string text)
    {
        var chars = text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray();
        return new string(chars)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length > 3 && w.EndsWith('s') ? w[..^1] : w)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string? NormalizeSku(string? sku)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return null;
        }

        var normalized = new string(sku.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return normalized.Length == 0 ? null : normalized;
    }

    private static MarketPriceMatch ToMatch(MarketDeal deal) => new(
        deal.RecordId,
        deal.Product,
        deal.Geography,
        deal.Currency.ToUpper(CultureInfo.InvariantCulture),
        deal.TermMonths,
        deal.UnitPriceP25,
        deal.UnitPriceP50,
        deal.UnitPriceP75,
        deal.SampleSize,
        MarketProvenance.Label(deal),
        deal.UpdatedAt);
}
