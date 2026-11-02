using System.Globalization;
using System.Text;
using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure.Entities;

namespace Raffa.Market.Network;

/// <summary>
/// Turns anonymised observations into shared market deals: one deal per supplier, product, term and currency
/// that at least <c>minWorkspaces</c> different workspaces priced, with P25/P50/P75 of what suppliers offered
/// them. Each workspace counts once (its latest observation), and a secondary figure (discount, uplift cap,
/// notice, payment terms) is published only when enough workspaces stated it — nothing is ever shared that
/// rests on a single customer.
/// </summary>
public static class MarketNetworkDealBuilder
{
    public const string Provider = "raffa-network";

    public static IReadOnlyList<MarketDeal> Build(IReadOnlyList<MarketObservationEntity> observations, int minWorkspaces)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var min = Math.Max(2, minWorkspaces);
        var deals = new List<MarketDeal>();
        var groups = observations
            .Where(o => o.UnitPrice is > 0 && o.TermMonths is > 0 && !string.IsNullOrWhiteSpace(o.Product) && !string.IsNullOrWhiteSpace(o.Currency))
            .GroupBy(o => (o.SupplierKey, Product: o.Product!.Trim().ToLowerInvariant(), Term: o.TermMonths!.Value, Currency: o.Currency!.ToUpperInvariant()));

        foreach (var group in groups)
        {
            // One vote per workspace: its latest observation.
            var votes = group.GroupBy(o => o.WorkspaceKey).Select(g => g.OrderByDescending(o => o.ObservedAt).First()).ToList();
            if (votes.Count < min)
            {
                continue;
            }

            var prices = votes.Select(o => o.UnitPrice!.Value).OrderBy(p => p).ToList();
            var latest = votes.Max(o => o.ObservedAt);
            deals.Add(new MarketDeal(
                Provider: Provider,
                RecordId: RecordId(group.Key.SupplierKey, group.Key.Product, group.Key.Term, group.Key.Currency),
                Supplier: Mode(votes.Select(o => o.Supplier)) ?? group.Key.SupplierKey,
                Category: Mode(votes.Select(o => o.Category)) ?? "uncategorized",
                Product: Mode(votes.Select(o => o.Product)) ?? group.Key.Product,
                Geography: Mode(votes.Select(o => o.Geography)) ?? "n/a",
                Currency: group.Key.Currency,
                CompanySizeBand: "n/a",
                TermMonths: group.Key.Term,
                AnnualValueBand: "n/a",
                UnitPriceP25: Percentile(prices, 0.25m),
                UnitPriceP50: Percentile(prices, 0.50m),
                UnitPriceP75: Percentile(prices, 0.75m),
                NegotiatedClauses: [],
                ClosingPeriod: latest.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                SampleSize: votes.Count,
                Source: $"Raffa customer network (anonymised supplier replies of {votes.Count.ToString(CultureInfo.InvariantCulture)} companies)",
                Representative: false,
                UpdatedAt: latest,
                Sku: Mode(votes.Select(o => o.Sku)),
                DiscountAchievedPct: Median(votes.Select(o => o.DiscountPct), min),
                UpliftCapPct: Median(votes.Select(o => o.UpliftCapPct), min),
                NoticeDays: Median(votes.Select(o => o.NoticeDays is { } n ? (double?)n : null), min) is { } nd ? (int)Math.Round(nd) : null,
                PaymentTerms: ModeAtLeast(votes.Select(o => o.PaymentTerms), min),
                LicenseRestrictions: null,
                Industry: null,
                UnitMetric: Mode(votes.Select(o => o.Unit))));
        }

        return deals.OrderBy(d => d.RecordId, StringComparer.Ordinal).ToList();
    }

    internal static string RecordId(string supplierKey, string product, int term, string currency)
    {
        var raw = $"net:{Slug(supplierKey)}:{Slug(product)}:{term.ToString(CultureInfo.InvariantCulture)}:{currency.ToLowerInvariant()}";
        if (raw.Length <= 190)
        {
            return raw;
        }

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..16].ToLowerInvariant();
        return raw[..170] + "~" + hash;
    }

    private static string Slug(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text.ToLowerInvariant())
        {
            sb.Append(char.IsLetterOrDigit(c) ? c : '-');
        }

        return string.Join('-', sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Linear interpolation between ranks, rounded to cents; with sorted input P25 ≤ P50 ≤ P75.</summary>
    internal static decimal Percentile(IReadOnlyList<decimal> sorted, decimal p)
    {
        if (sorted.Count == 1)
        {
            return decimal.Round(sorted[0], 2);
        }

        var rank = p * (sorted.Count - 1);
        var lo = (int)Math.Floor(rank);
        var hi = Math.Min(lo + 1, sorted.Count - 1);
        return decimal.Round(sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo), 2);
    }

    private static double? Median(IEnumerable<double?> values, int minCount)
    {
        var list = values.Where(v => v is not null).Select(v => v!.Value).OrderBy(v => v).ToList();
        if (list.Count < minCount)
        {
            return null;
        }

        var mid = list.Count / 2;
        return Math.Round(list.Count % 2 == 1 ? list[mid] : (list[mid - 1] + list[mid]) / 2, 2);
    }

    private static string? Mode(IEnumerable<string?> values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v)).GroupBy(v => v!.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key).FirstOrDefault();

    private static string? ModeAtLeast(IEnumerable<string?> values, int minCount)
    {
        var best = values.Where(v => !string.IsNullOrWhiteSpace(v)).GroupBy(v => v!.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).FirstOrDefault();
        return best is not null && best.Count() >= minCount ? best.Key : null;
    }
}
