using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure.Entities;
using Raffa.Market.Ingestion;

namespace Raffa.Market.Retrieval;

/// <summary>
/// Lookup tables over one <see cref="MarketCorpusSnapshot"/>, built once per corpus version so the
/// supplier, category and benchmark-candidate reads are dictionary lookups instead of a table scan
/// plus a JSON parse per call. Each method returns exactly what the SQL it replaces returned: the
/// same predicate on the same stored filter columns (NULL columns kept as "match anything", so a
/// partly back-filled table answers as before), then the same exact in-memory check on the
/// deserialized deal, in the same order. Immutable after construction, so safe to share. Never
/// tenant data -- the market corpus is the shared system corpus.
/// </summary>
public sealed class MarketCorpusIndex
{
    private readonly Row[] _rows;

    // Position of each row in "ORDER BY updated_at DESC, record_id" as the database sorts it (its
    // collation, not ordinal), so a filtered subset sorted by this rank is what the SQL returned.
    private readonly int[] _queryRank;

    private readonly Dictionary<string, int[]> _bySupplierName;
    private readonly Dictionary<string, int[]> _byCategory;
    private readonly Dictionary<string, int[]> _bySupplierColumn;
    private readonly int[] _nullSupplierColumn;

    private MarketCorpusIndex(
        Row[] rows,
        int[] queryRank,
        Dictionary<string, int[]> bySupplierName,
        Dictionary<string, int[]> byCategory,
        Dictionary<string, int[]> bySupplierColumn,
        int[] nullSupplierColumn,
        DateTimeOffset? latestUpdatedAt)
    {
        _rows = rows;
        _queryRank = queryRank;
        _bySupplierName = bySupplierName;
        _byCategory = byCategory;
        _bySupplierColumn = bySupplierColumn;
        _nullSupplierColumn = nullSupplierColumn;
        LatestUpdatedAt = latestUpdatedAt;
    }

    /// <summary>Number of indexed records (those whose payload deserialized).</summary>
    public int Count => _rows.Length;

    /// <summary>The newest <c>updated_at</c> column over every record of the table (what
    /// <c>MAX(updated_at)</c> returned), <see langword="null"/> for an empty corpus.</summary>
    public DateTimeOffset? LatestUpdatedAt { get; }

    /// <summary>
    /// Builds the index. <paramref name="records"/> are the table rows with their deserialized deal, in
    /// the order the table returned them; <paramref name="recordIdsInQueryOrder"/> are the same
    /// rows' ids as <c>ORDER BY updated_at DESC, record_id</c> returns them.
    /// </summary>
    public static MarketCorpusIndex Build(
        IReadOnlyList<(MarketRecordEntity Row, MarketDeal Deal)> records,
        IReadOnlyList<string> recordIdsInQueryOrder,
        DateTimeOffset? latestUpdatedAt)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(recordIdsInQueryOrder);

        var position = new Dictionary<string, int>(recordIdsInQueryOrder.Count, StringComparer.Ordinal);
        for (var i = 0; i < recordIdsInQueryOrder.Count; i++)
        {
            position.TryAdd(recordIdsInQueryOrder[i], i);
        }

        var rows = new Row[records.Count];
        var rank = new int[records.Count];
        List<int>? unranked = null;
        var bySupplierName = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var byCategory = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var bySupplierColumn = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var nullSupplierColumn = new List<int>();

        for (var i = 0; i < records.Count; i++)
        {
            var (entity, deal) = records[i];
            rows[i] = new Row(deal, entity.SupplierNorm, entity.ProductNorm, entity.Category);

            if (position.TryGetValue(entity.RecordId, out var p))
            {
                rank[i] = p;
            }
            else
            {
                // Written between the two reads of the load: ranked after every other row below.
                (unranked ??= []).Add(i);
            }

            // A deal without a supplier or category can never pass the exact check, so it is not indexed.
            if (deal.Supplier is not null)
            {
                var name = MarketSupplierMatch.Normalize(deal.Supplier);
                if (name.Length > 0)
                {
                    Add(bySupplierName, name, i);
                }
            }

            if (deal.Category is not null)
            {
                Add(byCategory, deal.Category, i);
            }

            if (entity.SupplierNorm is null)
            {
                nullSupplierColumn.Add(i);
            }
            else
            {
                Add(bySupplierColumn, entity.SupplierNorm, i);
            }
        }

        if (unranked is not null)
        {
            unranked.Sort((a, b) =>
            {
                var byUpdated = records[b].Row.UpdatedAt.CompareTo(records[a].Row.UpdatedAt);
                return byUpdated != 0
                    ? byUpdated
                    : string.CompareOrdinal(records[a].Row.RecordId, records[b].Row.RecordId);
            });
            var next = recordIdsInQueryOrder.Count;
            foreach (var i in unranked)
            {
                rank[i] = next++;
            }
        }

        return new MarketCorpusIndex(
            rows,
            rank,
            Freeze(bySupplierName),
            Freeze(byCategory),
            Freeze(bySupplierColumn),
            [.. nullSupplierColumn],
            latestUpdatedAt);
    }

    /// <summary>
    /// What <see cref="MarketRecordQueryService.GetBySupplierAsync"/> returned from SQL: deals whose
    /// supplier matches <paramref name="supplierName"/> (<see cref="MarketSupplierMatch"/>), among
    /// the rows whose <c>supplier_norm</c> is NULL, equal, or contains / is contained in the wanted
    /// name on word boundaries; newest first, then by record id.
    /// </summary>
    public IReadOnlyList<MarketDeal> BySupplier(string supplierName)
    {
        var wanted = MarketSupplierMatch.Normalize(supplierName ?? string.Empty);
        if (wanted.Length == 0)
        {
            return [];
        }

        var paddedWanted = " " + wanted + " ";
        var hits = new List<int>();
        foreach (var (name, indexes) in _bySupplierName)
        {
            if (!MarketSupplierMatch.MatchesNormalized(name, wanted))
            {
                continue;
            }

            foreach (var i in indexes)
            {
                if (SupplierColumnAdmits(_rows[i].SupplierNorm, wanted, paddedWanted))
                {
                    hits.Add(i);
                }
            }
        }

        return InQueryOrder(hits);
    }

    /// <summary>
    /// What <see cref="MarketRecordQueryService.GetByCategoryAsync"/> returned from SQL: deals whose
    /// category equals <paramref name="category"/> (trimmed, case-insensitive), among the rows whose
    /// <c>category</c> is NULL or equals its folded form; newest first, then by record id.
    /// </summary>
    public IReadOnlyList<MarketDeal> ByCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return [];
        }

        if (!_byCategory.TryGetValue(category.Trim(), out var indexes))
        {
            return [];
        }

        var folded = MarketFilterColumns.Category(category);
        var hits = new List<int>(indexes.Length);
        foreach (var i in indexes)
        {
            var column = _rows[i].Category;
            if (column is null || string.Equals(column, folded, StringComparison.Ordinal))
            {
                hits.Add(i);
            }
        }

        return InQueryOrder(hits);
    }

    /// <summary>
    /// What the benchmark adapter's candidate query returned: the rows whose <c>supplier_norm</c> is
    /// NULL or equals <paramref name="supplierNorm"/> and whose <c>product_norm</c> is NULL or
    /// equals <paramref name="productNorm"/>, in the order the table returned them (the adapter's
    /// tie-breaking relies on it).
    /// </summary>
    public IReadOnlyList<MarketDeal> Candidates(string supplierNorm, string productNorm)
    {
        _bySupplierColumn.TryGetValue(supplierNorm, out var equal);
        equal ??= [];
        var nulls = _nullSupplierColumn;

        var result = new List<MarketDeal>();
        int a = 0, b = 0;
        while (a < equal.Length || b < nulls.Length)
        {
            // Both lists ascend in table position; their rows are disjoint (NULL vs non-NULL column).
            var next = b >= nulls.Length || (a < equal.Length && equal[a] < nulls[b]) ? equal[a++] : nulls[b++];
            var row = _rows[next];
            if (row.ProductNorm is null || string.Equals(row.ProductNorm, productNorm, StringComparison.Ordinal))
            {
                result.Add(row.Deal);
            }
        }

        return result;
    }

    // The SQL predicate on the stored column, written out: NULL, equal, the column contained in the
    // wanted name or the wanted name contained in the column, both on word boundaries (the two
    // LIKEs; the column is MarketFilterColumns.SupplierNorm output -- letters, digits and single
    // spaces -- so it holds no LIKE wildcard).
    private static bool SupplierColumnAdmits(string? column, string wanted, string paddedWanted)
    {
        if (column is null || string.Equals(column, wanted, StringComparison.Ordinal))
        {
            return true;
        }

        var paddedColumn = " " + column + " ";
        return paddedColumn.Contains(paddedWanted, StringComparison.Ordinal)
            || paddedWanted.Contains(paddedColumn, StringComparison.Ordinal);
    }

    private List<MarketDeal> InQueryOrder(List<int> hits)
    {
        hits.Sort((x, y) => _queryRank[x].CompareTo(_queryRank[y]));
        var deals = new List<MarketDeal>(hits.Count);
        foreach (var i in hits)
        {
            deals.Add(_rows[i].Deal);
        }

        return deals;
    }

    private static void Add(Dictionary<string, List<int>> map, string key, int index)
    {
        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = [];
        }

        list.Add(index);
    }

    private static Dictionary<string, int[]> Freeze(Dictionary<string, List<int>> map)
    {
        var frozen = new Dictionary<string, int[]>(map.Count, map.Comparer);
        foreach (var (key, list) in map)
        {
            frozen[key] = [.. list];
        }

        return frozen;
    }

    private readonly record struct Row(MarketDeal Deal, string? SupplierNorm, string? ProductNorm, string? Category);
}
