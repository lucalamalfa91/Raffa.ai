using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure.Entities;
using Raffa.Market.Retrieval;

namespace Raffa.Market.Ingestion;

/// <summary>
/// F7-T02: the denormalised, indexable copies of the fields every read path filters on, written next
/// to the jsonb payload so <c>WHERE</c>/<c>LIMIT</c> run in SQL instead of after a whole-table read.
/// Each value is a deterministic function of the record's own text, so two strings the readers treat
/// as equal always fold to the same column value -- the SQL predicate is therefore a superset of the
/// in-memory one, and the readers still apply the exact in-memory check to the (few) rows it returns.
/// </summary>
public static class MarketFilterColumns
{
    /// <summary>Supplier folded with <see cref="MarketSupplierMatch.Normalize"/> (lowercase words, legal suffixes dropped).</summary>
    public static string SupplierNorm(string supplier) => MarketSupplierMatch.Normalize(supplier);

    /// <summary>Product folded to lowercase alphanumeric words (no suffix stripping).</summary>
    public static string ProductNorm(string product) =>
        string.Join(
            ' ',
            new string(product.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Category, trimmed and lowercased (the readers compare it case-insensitively).</summary>
    public static string Category(string category) => category.Trim().ToLowerInvariant();

    /// <summary>Geography, trimmed and uppercased (ISO-style codes).</summary>
    public static string Geography(string geography) => geography.Trim().ToUpperInvariant();

    /// <summary>Currency, trimmed and uppercased.</summary>
    public static string Currency(string currency) => currency.Trim().ToUpperInvariant();

    /// <summary>Unit metric, trimmed and lowercased; <see langword="null"/> when the record has none.</summary>
    public static string? UnitMetric(string? unitMetric) =>
        string.IsNullOrWhiteSpace(unitMetric) ? null : unitMetric.Trim().ToLowerInvariant();

    /// <summary>Writes all filter columns of <paramref name="deal"/> onto <paramref name="record"/>.</summary>
    public static void Apply(MarketRecordEntity record, MarketDeal deal)
    {
        record.SupplierNorm = SupplierNorm(deal.Supplier);
        record.ProductNorm = ProductNorm(deal.Product);
        record.Category = Category(deal.Category);
        record.Geography = Geography(deal.Geography);
        record.Currency = Currency(deal.Currency);
        record.TermMonths = deal.TermMonths;
        record.UnitMetric = UnitMetric(deal.UnitMetric);
    }

    /// <summary>Whether the filter columns were never written (a row that predates them).</summary>
    public static bool IsMissing(MarketRecordEntity record) => record.SupplierNorm is null;
}
