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
/// number that is not what it claims to be). Every candidate record is priced in the contract's own
/// currency (no FX conversion exists anywhere in this corpus, so a GBP line is only ever compared
/// with GBP records) and has at least <see cref="MinimumSampleSize"/> comparables (the market-feed
/// adapter's own abstain bar). A line then gets, in this order:
/// <list type="number">
/// <item><b>Its own product</b> (<see cref="MarketMatchKind.Exact"/>): the record's supplier is the
/// contract's supplier (<see cref="MarketSupplierMatch"/>) and every word of the market product
/// name appears in the line's description/SKU (<c>"Sales Cloud Unlimited named users / licenses"</c>
/// carries <c>Sales Cloud Unlimited</c>), or the SKUs are equal — the longest product name wins, so
/// an edition beats its family.</item>
/// <item><b>A bundle</b> (<see cref="MarketMatchKind.Bundle"/>): the line names two or more distinct
/// products (<c>"Jira Software Premium + Confluence Premium"</c>) — comparing it with one of them
/// alone would read a bundle price as a single product's overcharge, so the band is the sum of each
/// product's own P25/P50/P75.</item>
/// <item><b>A similar product</b> (<see cref="MarketMatchKind.Similar"/>) when nothing names the
/// line's own product: first the supplier's own catalogue (a sibling edition), then the other
/// suppliers selling in the supplier's own market categories. A record is similar when more than
/// half of its product's distinctive words — the words left once the supplier's name and generic
/// edition/packaging words (<c>Premium</c>, <c>Enterprise</c>, <c>licences</c>, …) are set aside —
/// appear in the line. Never presented as the line's own price: <see cref="MarketPriceMatch.Kind"/>
/// says so and the product it names is the similar one.</item>
/// </list>
/// Among the survivors: customers of the same type first (the record's annual-value band holds the
/// contract's own yearly value), then the currency's home region (GBP→UK, CHF→CH, EUR→EU, USD→US),
/// then the contract's own term (else the nearest), then the larger sample, then the fresher record
/// — so the same corpus always yields the same match.
/// </summary>
public sealed class MarketPriceMatcher(IMarketDealLookup dealLookup) : IMarketPriceMatcher
{
    /// <summary>Same bar as <c>MarketFeedBenchmarkAdapter.MinimumViableSampleSize</c>.</summary>
    public const int MinimumSampleSize = 5;

    /// <summary>Specificity of an equal SKU — above any product-name word count.</summary>
    private const int SkuSpecificity = 1000;

    private static readonly Dictionary<string, string> HomeRegionByCurrency = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GBP"] = "UK",
        ["CHF"] = "CH",
        ["EUR"] = "EU",
        ["USD"] = "US",
    };

    /// <summary>Edition, packaging and billing words: they say which tier or how a product is
    /// sold, not what it is, so they never make two products similar on their own.</summary>
    private static readonly HashSet<string> GenericWords = Words(
        "standard premium enterprise professional pro business plus starter unlimited advanced " +
        "basic essentials core suite plan edition tier annual yearly monthly month year license " +
        "licence user named seat subscription package add on and the for with per of in to a an");

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

        var matches = Match(context, lines, deals);
        if (matches.All(m => m is not null))
        {
            return matches;
        }

        // Some line has no product of its own in the supplier's catalogue: widen to the markets
        // the supplier sells in, for a similar product from another supplier.
        var market = new List<MarketDeal>();
        foreach (var category in deals.Select(d => d.Category).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            market.AddRange(await dealLookup.GetByCategoryAsync(category, cancellationToken).ConfigureAwait(false));
        }

        return market.Count == 0 ? matches : Match(context, lines, deals, market);
    }

    /// <inheritdoc />
    public Task<string?> GetCorpusVersionAsync(CancellationToken cancellationToken) =>
        dealLookup.GetCorpusVersionAsync(cancellationToken);

    /// <summary>The pure half of <see cref="MatchAsync"/>: <paramref name="supplierDeals"/> is the
    /// supplier's slice of the corpus, <paramref name="marketDeals"/> the wider market a similar
    /// product may come from (the supplier's own deals in it are ignored — they are
    /// <paramref name="supplierDeals"/>).</summary>
    public static IReadOnlyList<MarketPriceMatch?> Match(
        MarketPriceContext context,
        IReadOnlyList<MarketPriceLine> lines,
        IReadOnlyList<MarketDeal> supplierDeals,
        IReadOnlyList<MarketDeal>? marketDeals = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(supplierDeals);

        var currency = context.Currency.Trim();
        HomeRegionByCurrency.TryGetValue(currency, out var homeRegion);
        var supplierWords = Words(context.SupplierName ?? string.Empty);

        bool Priced(MarketDeal d) =>
            string.Equals(d.Currency, currency, StringComparison.OrdinalIgnoreCase) && d.SampleSize >= MinimumSampleSize;

        var priced = supplierDeals.Where(Priced).ToList();
        var otherSuppliers = (marketDeals ?? [])
            .Where(d => Priced(d) && !MarketSupplierMatch.Matches(d.Supplier, context.SupplierName ?? string.Empty))
            .ToList();

        var results = new MarketPriceMatch?[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var named = priced
                .Select(deal => (Deal: deal, Specificity: ProductSpecificity(deal, line, supplierWords)))
                .Where(x => x.Specificity > 0)
                .ToList();

            if (named.Count > 0)
            {
                results[i] = named.Any(x => x.Specificity >= SkuSpecificity)
                    ? null
                    : Bundle(context, homeRegion, line, named.Select(x => x.Deal), supplierWords);
                results[i] ??= ToMatch(
                    Ranked(named, x => x.Deal, context, homeRegion, q => q.OrderByDescending(x => x.Specificity)).First().Deal,
                    MarketMatchKind.Exact);
                continue;
            }

            var similar = MostSimilar(context, homeRegion, line, priced, supplierWords)
                ?? MostSimilar(context, homeRegion, line, otherSuppliers, supplierWords);
            results[i] = similar is null ? null : ToMatch(similar, MarketMatchKind.Similar);
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
            return SkuSpecificity;
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

    /// <summary>
    /// Whether <paramref name="annualValue"/> falls in <paramref name="band"/> (<c>"&lt;100k"</c>,
    /// <c>"100k-250k"</c>, <c>"1m-5m"</c>, <c>"5m+"</c>) — a customer of the same type. An unknown
    /// value or a band this cannot read is never "the same type".
    /// </summary>
    public static bool IsInValueBand(string? band, decimal? annualValue)
    {
        if (annualValue is not { } value || string.IsNullOrWhiteSpace(band))
        {
            return false;
        }

        var text = band.Trim().ToLowerInvariant();
        if (text.StartsWith('<'))
        {
            return ParseAmount(text[1..]) is { } below && value < below;
        }

        if (text.EndsWith('+'))
        {
            return ParseAmount(text[..^1]) is { } from && value >= from;
        }

        var parts = text.Split('-', 2);
        return parts.Length == 2
            && ParseAmount(parts[0]) is { } low
            && ParseAmount(parts[1]) is { } high
            && value >= low && value < high;
    }

    private static decimal? ParseAmount(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var multiplier = char.ToLowerInvariant(trimmed[^1]) switch
        {
            'k' => 1_000m,
            'm' => 1_000_000m,
            _ => 1m,
        };
        var number = multiplier == 1m ? trimmed : trimmed[..^1];
        return decimal.TryParse(number, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount * multiplier
            : null;
    }

    /// <summary>
    /// The line as a bundle of two or more distinct products it names, or <see langword="null"/>.
    /// A product whose words all sit inside another named product's is that product's family, not a
    /// second product (<c>Sales Cloud</c> inside <c>Sales Cloud Unlimited</c>); two editions of one
    /// product (<c>Microsoft 365 E3</c> and <c>E5</c>) share a distinctive word and are not a bundle
    /// either — the line keeps its single best match.
    /// </summary>
    private static MarketPriceMatch? Bundle(
        MarketPriceContext context,
        string? homeRegion,
        MarketPriceLine line,
        IEnumerable<MarketDeal> named,
        IReadOnlySet<string> supplierWords)
    {
        var products = named
            .GroupBy(d => d.Product.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var words = Words(g.Key);
                words.ExceptWith(supplierWords);
                return (Words: words, Distinctive: Distinctive(words), Deals: g.ToList());
            })
            .ToList();

        var kept = products
            .Where(p => !products.Any(o => !ReferenceEquals(o.Deals, p.Deals) && p.Words.IsProperSubsetOf(o.Words)))
            .ToList();
        if (kept.Count < 2 || kept.Any(p => p.Distinctive.Count == 0))
        {
            return null;
        }

        for (var a = 0; a < kept.Count; a++)
        {
            for (var b = a + 1; b < kept.Count; b++)
            {
                if (kept[a].Distinctive.Overlaps(kept[b].Distinctive))
                {
                    return null;
                }
            }
        }

        var description = line.Description.ToLowerInvariant();
        var components = kept
            .Select(p => Ranked(p.Deals, d => d, context, homeRegion, q => q.OrderBy(_ => 0)).First())
            .OrderBy(d => FirstMention(description, Distinctive(Words(d.Product))))
            .ThenBy(d => d.Product, StringComparer.Ordinal)
            .ToList();

        var oldest = components.MinBy(d => d.UpdatedAt)!;
        var regions = components.Select(d => d.Geography).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new MarketPriceMatch(
            string.Join("+", components.Select(d => d.RecordId)),
            string.Join(" + ", components.Select(d => d.Product)),
            string.Join("/", regions),
            components[0].Currency.ToUpper(CultureInfo.InvariantCulture),
            components[0].TermMonths,
            components.Sum(d => d.UnitPriceP25),
            components.Sum(d => d.UnitPriceP50),
            components.Sum(d => d.UnitPriceP75),
            components.Min(d => d.SampleSize),
            MarketProvenance.Label(oldest),
            oldest.UpdatedAt,
            MarketMatchKind.Bundle);
    }

    /// <summary>The record in <paramref name="pool"/> most similar to <paramref name="line"/> —
    /// see this type's own doc comment for "similar" — or <see langword="null"/>.</summary>
    private static MarketDeal? MostSimilar(
        MarketPriceContext context,
        string? homeRegion,
        MarketPriceLine line,
        IReadOnlyList<MarketDeal> pool,
        IReadOnlySet<string> supplierWords)
    {
        var lineWords = Words($"{line.Description} {line.Sku}");
        var lineDistinctive = Distinctive(lineWords);
        if (lineDistinctive.Count == 0)
        {
            return null;
        }

        var candidates = new List<(MarketDeal Deal, int Shared, decimal Coverage, int SharedGeneric)>();
        foreach (var deal in pool)
        {
            var words = Words(deal.Product);
            words.ExceptWith(supplierWords);
            words.ExceptWith(Words(deal.Supplier));
            var distinctive = Distinctive(words);
            if (distinctive.Count == 0)
            {
                continue;
            }

            var shared = distinctive.Count(lineDistinctive.Contains);
            var coverage = (decimal)shared / distinctive.Count;
            if (shared == 0 || coverage <= 0.5m)
            {
                continue;
            }

            var sharedGeneric = words.Count(w => GenericWords.Contains(w) && lineWords.Contains(w));
            candidates.Add((deal, shared, coverage, sharedGeneric));
        }

        return candidates.Count == 0
            ? null
            : Ranked(
                candidates,
                x => x.Deal,
                context,
                homeRegion,
                q => q.OrderByDescending(x => x.Shared).ThenByDescending(x => x.Coverage).ThenByDescending(x => x.SharedGeneric))
                .First().Deal;
    }

    /// <summary>The one tie-break order every match uses after its own relevance
    /// (<paramref name="byRelevance"/>): same customer type, home region, the contract's term, the
    /// larger sample, the fresher record, then the record id.</summary>
    private static IOrderedEnumerable<T> Ranked<T>(
        IEnumerable<T> items,
        Func<T, MarketDeal> deal,
        MarketPriceContext context,
        string? homeRegion,
        Func<IEnumerable<T>, IOrderedEnumerable<T>> byRelevance) =>
        byRelevance(items)
            .ThenByDescending(x => IsInValueBand(deal(x).AnnualValueBand, context.AnnualValue))
            .ThenByDescending(x => homeRegion is not null
                && string.Equals(deal(x).Geography, homeRegion, StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => context.TermMonths is { } term ? Math.Abs(deal(x).TermMonths - term) : 0)
            .ThenByDescending(x => deal(x).SampleSize)
            .ThenByDescending(x => deal(x).UpdatedAt)
            .ThenBy(x => deal(x).RecordId, StringComparer.Ordinal);

    private static HashSet<string> Distinctive(IEnumerable<string> words) =>
        words.Where(w => !GenericWords.Contains(w)).ToHashSet(StringComparer.Ordinal);

    /// <summary>Where the line first mentions one of <paramref name="words"/> — a bundle is named in
    /// the order the line itself lists its products.</summary>
    private static int FirstMention(string description, IReadOnlySet<string> words)
    {
        var first = int.MaxValue;
        foreach (var word in words)
        {
            var index = description.IndexOf(word, StringComparison.Ordinal);
            if (index >= 0 && index < first)
            {
                first = index;
            }
        }

        return first;
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

    private static MarketPriceMatch ToMatch(MarketDeal deal, MarketMatchKind kind) => new(
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
        deal.UpdatedAt,
        kind);
}
