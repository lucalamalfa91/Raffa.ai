using System.Text.RegularExpressions;
using Raffa.Benchmark.Contracts;
using Raffa.Market.Contracts;
using Raffa.Market.Retrieval;

namespace Raffa.AiFlows.Shared.ContractContext;

/// <summary>
/// The market band for a priced line that neither the stored market comparison nor the benchmark
/// service (an exact product-name match) could price: the supplier's market deals whose product name
/// is contained in the line's own description ("Microsoft 365 E5" in "Microsoft 365 E5 named users /
/// licenses"), narrowed to the contract's currency and geography. The band is the median P25/P50/P75
/// across those deals (one record per company-size band or industry), the sample their summed sample
/// size, and the source names the product it was matched on — so a reader never takes it for the
/// stored comparison Contract 360 shows. Pure: same inputs, same band; never a fabricated figure.
/// </summary>
public static partial class MarketBandFallback
{
    /// <summary>
    /// <paramref name="lines"/> with every line that has no band (and has a unit price) filled from
    /// <paramref name="deals"/> when a deal's product matches it; every other line is returned as is.
    /// </summary>
    public static IReadOnlyList<PricedLine> Fill(
        IReadOnlyList<PricedLine> lines,
        IReadOnlyList<MarketDeal> deals,
        string? supplierName,
        string currency,
        string? geography)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(deals);

        if (deals.Count == 0 || lines.All(l => l.Benchmark is not null))
        {
            return lines;
        }

        var supplierWords = supplierName is null ? [] : Words(supplierName).ToHashSet(StringComparer.Ordinal);
        var candidates = Narrow(deals, currency, geography);
        if (candidates.Count == 0)
        {
            return lines;
        }

        return lines.Select(line => line.Benchmark is not null ? line : FillOne(line, candidates, supplierWords)).ToList();
    }

    /// <summary>
    /// <see cref="Fill"/> over the supplier's deals read from <paramref name="lookup"/>: the one entry
    /// point every reader of priced lines (Ask's packs, the strategy endpoint) calls, so a line gets the
    /// same band wherever it is shown. A missing lookup, supplier or a failed read keeps the lines as they were.
    /// </summary>
    public static async Task<IReadOnlyList<PricedLine>> FillFromCorpusAsync(
        IReadOnlyList<PricedLine> lines,
        IMarketDealLookup? lookup,
        string? supplierName,
        string currency,
        string? geography,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lookup is null || string.IsNullOrWhiteSpace(supplierName) || lines.All(l => l.Benchmark is not null || l.UnitPrice is null))
        {
            return lines;
        }

        try
        {
            var deals = await lookup.GetBySupplierAsync(supplierName, cancellationToken).ConfigureAwait(false);
            return Fill(lines, deals, supplierName, currency, geography);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return lines;
        }
    }

    private static PricedLine FillOne(PricedLine line, IReadOnlyList<MarketDeal> deals, HashSet<string> supplierWords)
    {
        var lineWords = Words(line.Description).Concat(line.Sku is null ? [] : Words(line.Sku)).ToHashSet(StringComparer.Ordinal);
        if (lineWords.Count == 0)
        {
            return line;
        }

        // A SKU match is the strongest signal; else the longest product name contained in the line.
        var bySku = line.Sku is { Length: > 0 } sku
            ? deals.Where(d => string.Equals(d.Sku, sku, StringComparison.OrdinalIgnoreCase)).ToList()
            : [];
        List<MarketDeal> matched;
        string product;
        if (bySku.Count > 0)
        {
            matched = bySku;
            product = bySku[0].Product;
        }
        else
        {
            var best = deals
                .GroupBy(d => d.Product, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Product: g.Key, Deals: g.ToList(), Words: Words(g.Key).ToHashSet(StringComparer.Ordinal)))
                .Where(g => g.Words.Count > 0
                    && g.Words.Except(supplierWords).Any()
                    && g.Words.All(lineWords.Contains))
                .OrderByDescending(g => g.Words.Count)
                .ThenByDescending(g => g.Deals.Sum(d => d.SampleSize))
                .FirstOrDefault();
            if (best.Deals is null)
            {
                return line;
            }

            matched = best.Deals;
            product = best.Product;
        }

        // Same term when the corpus has it for this product.
        if (line.TermMonths is { } term && matched.Where(d => d.TermMonths == term).ToList() is { Count: > 0 } sameTerm)
        {
            matched = sameTerm;
        }

        var band = new BenchmarkDistribution(
            Median(matched.Select(d => d.UnitPriceP25)),
            Median(matched.Select(d => d.UnitPriceP50)),
            Median(matched.Select(d => d.UnitPriceP75)));

        return line with
        {
            Benchmark = band,
            SampleSize = matched.Sum(d => d.SampleSize),
            AdapterName = $"market-feed, matched by product name {product} (representative, mock)",
            AsOf = matched.Max(d => d.UpdatedAt),
        };
    }

    /// <summary>Same currency and geography when the corpus has them, else same currency; never
    /// another currency (a band in EUR is not a benchmark for a CHF price).</summary>
    private static IReadOnlyList<MarketDeal> Narrow(IReadOnlyList<MarketDeal> deals, string currency, string? geography)
    {
        var sameCurrency = deals.Where(d => string.Equals(d.Currency, currency, StringComparison.OrdinalIgnoreCase)).ToList();
        if (geography is null || sameCurrency.Count == 0)
        {
            return sameCurrency;
        }

        var sameGeography = sameCurrency.Where(d => string.Equals(d.Geography, geography, StringComparison.OrdinalIgnoreCase)).ToList();
        return sameGeography.Count > 0 ? sameGeography : sameCurrency;
    }

    private static decimal Median(IEnumerable<decimal> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2m;
    }

    private static IEnumerable<string> Words(string text) =>
        WordPattern().Matches(text.ToLowerInvariant()).Select(m => m.Value);

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();
}
