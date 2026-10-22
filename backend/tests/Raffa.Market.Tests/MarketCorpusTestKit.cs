using System.Text.RegularExpressions;
using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;
using Raffa.Market.Infrastructure.Entities;
using Raffa.Market.Ingestion;
using Raffa.Market.Retrieval;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Market.Tests;

/// <summary>
/// Docker-free building blocks for the corpus cache / index tests: table rows built the way ingestion
/// builds them, a <see cref="MarketDbContext"/> that never connects, and the SQL the lookups used to
/// run written out as LINQ-to-objects -- the reference the in-memory index must reproduce.
/// </summary>
internal static class MarketCorpusTestKit
{
    public static List<(MarketRecordEntity Row, MarketDeal Deal)> Rows(IEnumerable<MarketDeal> deals) =>
        deals.Select(deal =>
        {
            var row = new MarketRecordEntity
            {
                RecordId = deal.RecordId,
                FeedVersion = "v1",
                Provider = deal.Provider,
                PayloadJson = "{}",
                ProvenanceLabel = "label",
                UpdatedAt = deal.UpdatedAt,
            };
            MarketFilterColumns.Apply(row, deal);
            return (row, deal);
        }).ToList();

    /// <summary>A row that predates the filter columns (all of them NULL).</summary>
    public static void BlankFilterColumns(MarketRecordEntity row)
    {
        row.SupplierNorm = null;
        row.ProductNorm = null;
        row.Category = null;
        row.Geography = null;
        row.Currency = null;
        row.TermMonths = null;
        row.UnitMetric = null;
    }

    /// <summary>The ids as <c>ORDER BY updated_at DESC, record_id</c> returns them (ordinal collation here).</summary>
    public static List<string> QueryOrder(IEnumerable<(MarketRecordEntity Row, MarketDeal Deal)> rows) =>
        rows.OrderByDescending(r => r.Row.UpdatedAt)
            .ThenBy(r => r.Row.RecordId, StringComparer.Ordinal)
            .Select(r => r.Row.RecordId)
            .ToList();

    public static MarketCorpusIndex Index(IReadOnlyList<(MarketRecordEntity Row, MarketDeal Deal)> rows) =>
        MarketCorpusIndex.Build(rows, QueryOrder(rows), rows.Count == 0 ? null : rows.Max(r => r.Row.UpdatedAt));

    /// <summary>A context that is never used to query: the fake corpus source ignores it.</summary>
    public static MarketDbContext OfflineDb()
    {
        var options = new DbContextOptionsBuilder<MarketDbContext>();
        MarketDbContextOptions.Configure(options, "Host=localhost;Database=offline;Username=u;Password=p");
        return new MarketDbContext(options.Options);
    }

    /// <summary>SQL <c>LIKE</c> (case-sensitive, <c>%</c> any run, <c>_</c> one character), through a regex so
    /// it is an independent implementation from the index's <c>Contains</c> checks.</summary>
    public static bool Like(string value, string pattern)
    {
        var regex = "^" + string.Concat(pattern.Select(c => c switch
        {
            '%' => ".*",
            '_' => ".",
            _ => Regex.Escape(c.ToString()),
        })) + "$";
        return Regex.IsMatch(value, regex, RegexOptions.Singleline | RegexOptions.CultureInvariant);
    }

    /// <summary>The old <c>GetBySupplierAsync</c>: the SQL predicate on the stored columns, <c>ORDER BY
    /// updated_at DESC, record_id</c>, then the exact in-memory <see cref="MarketSupplierMatch"/> check.</summary>
    public static List<string> OldBySupplier(
        IEnumerable<(MarketRecordEntity Row, MarketDeal Deal)> table, string? supplierName)
    {
        var wanted = MarketSupplierMatch.Normalize(supplierName ?? string.Empty);
        if (wanted.Length == 0)
        {
            return [];
        }

        var padded = " " + wanted + " ";
        var likeWanted = "% " + wanted + " %";

        // Both predicates are pure functions of one string, so each distinct value is decided once.
        var admitted = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool Admits(string column) => admitted.TryGetValue(column, out var known)
            ? known
            : admitted[column] = column == wanted
                || Like(" " + column + " ", likeWanted)
                || Like(padded, "% " + column + " %");
        var matched = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool Exact(string dealSupplier) => matched.TryGetValue(dealSupplier, out var known)
            ? known
            : matched[dealSupplier] = MarketSupplierMatch.Matches(dealSupplier, supplierName!);

        return table
            .Where(r => r.Row.SupplierNorm == null || Admits(r.Row.SupplierNorm))
            .OrderByDescending(r => r.Row.UpdatedAt)
            .ThenBy(r => r.Row.RecordId, StringComparer.Ordinal)
            .Where(r => Exact(r.Deal.Supplier))
            .Select(r => r.Row.RecordId)
            .ToList();
    }

    /// <summary>The old <c>GetByCategoryAsync</c>.</summary>
    public static List<string> OldByCategory(
        IEnumerable<(MarketRecordEntity Row, MarketDeal Deal)> table, string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return [];
        }

        var folded = MarketFilterColumns.Category(category);
        return table
            .Where(r => r.Row.Category == null || r.Row.Category == folded)
            .OrderByDescending(r => r.Row.UpdatedAt)
            .ThenBy(r => r.Row.RecordId, StringComparer.Ordinal)
            .Where(r => string.Equals(r.Deal.Category, category.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Row.RecordId)
            .ToList();
    }

    /// <summary>The old adapter candidate query: NULL-or-equal on both normalised columns, in table order.</summary>
    public static List<string> OldCandidates(
        IEnumerable<(MarketRecordEntity Row, MarketDeal Deal)> table, string supplierNorm, string productNorm) =>
        table
            .Where(r => (r.Row.SupplierNorm == null || r.Row.SupplierNorm == supplierNorm)
                && (r.Row.ProductNorm == null || r.Row.ProductNorm == productNorm))
            .Select(r => r.Row.RecordId)
            .ToList();
}

/// <summary>An <see cref="IMarketCorpusSource"/> over an in-memory table that counts what the cache asks of it.</summary>
internal sealed class FakeCorpusSource : IMarketCorpusSource
{
    public List<(MarketRecordEntity Row, MarketDeal Deal)> Table { get; set; } = [];

    public int Fingerprints { get; private set; }

    public int Versions { get; private set; }

    public int Reads { get; private set; }

    /// <summary>Bumped by a test to simulate the Worker's ingest changing the table.</summary>
    public int Generation { get; set; } = 1;

    /// <summary>When set, a fingerprint call waits for it (to line concurrent callers up behind one check).</summary>
    public TaskCompletionSource? HoldFingerprint { get; set; }

    public async Task<string> FingerprintAsync(MarketDbContext db, CancellationToken cancellationToken)
    {
        Fingerprints++;
        if (HoldFingerprint is { } hold)
        {
            await hold.Task.ConfigureAwait(false);
        }

        return $"{Table.Count}:feed-{Generation}:hash{Generation}";
    }

    public Task<string> CorpusVersionAsync(MarketDbContext db, CancellationToken cancellationToken)
    {
        Versions++;
        return Task.FromResult(Table.Count == 0 ? "empty" : $"feed-{Generation}:{Table.Count}");
    }

    /// <summary>When set, reading the corpus throws it (an unreadable payload).</summary>
    public Exception? FailRead { get; set; }

    public Task<MarketCorpusRead> ReadAsync(MarketDbContext db, CancellationToken cancellationToken)
    {
        Reads++;
        if (FailRead is { } failure)
        {
            throw failure;
        }

        return Task.FromResult(new MarketCorpusRead(
            Table.ToList(),
            MarketCorpusTestKit.QueryOrder(Table),
            Table.Count == 0 ? null : Table.Max(r => r.Row.UpdatedAt)));
    }
}
