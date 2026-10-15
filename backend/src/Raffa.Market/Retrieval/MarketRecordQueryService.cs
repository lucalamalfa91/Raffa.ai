using System.Text.Json;
using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;
using Raffa.Market.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Market.Retrieval;

/// <summary>
/// One persisted <see cref="MarketDeal"/> plus its precomputed
/// <see cref="Infrastructure.Entities.MarketRecordEntity.ProvenanceLabel"/> — parent story AC-5's
/// own response shape: "returns one record with its provenance label and updatedAt for the
/// citation panel" (<see cref="MarketDeal"/> already carries <see cref="MarketDeal.UpdatedAt"/>).
/// </summary>
public sealed record MarketRecordDetail(MarketDeal Deal, string ProvenanceLabel);

/// <summary>
/// Backs <c>Raffa.Api.MarketEndpointExtensions</c>'s <c>GET /api/market/records/{id}</c> (task
/// objective, parent story AC-5). Business logic lives here, not in the endpoint delegate itself
/// (ADR-002 "host is a thin composition root"), so it is directly unit-testable against a real
/// Postgres+pgvector database with no HTTP host in the loop — mirrors
/// <c>Raffa.Documents.Contracts.Application.DocumentQueryService</c>'s identical split.
/// </summary>
public sealed class MarketRecordQueryService(MarketDbContext dbContext, MarketCorpusCache? corpusCache = null)
    : IMarketDealLookup
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Returns <paramref name="recordId"/>'s record, or <see langword="null"/> when no
    /// <c>market_record</c> row exists for it — the endpoint's own 404 signal. No tenant
    /// scoping/authorization check of any kind: the market index is shared and read-only for every
    /// tenant (ADR-024), so any caller may read any record by id.
    /// </summary>
    public async Task<MarketRecordDetail?> GetByIdAsync(
        string recordId, CancellationToken cancellationToken = default)
    {
        var record = await dbContext.MarketRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.RecordId == recordId, cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            return null;
        }

        var deal = JsonSerializer.Deserialize<MarketDeal>(record.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException(
                $"market_record '{recordId}' has a payload that failed to deserialize back into a " +
                "MarketDeal -- this should be impossible for a row MarketIngestionService wrote.");

        return new MarketRecordDetail(deal, record.ProvenanceLabel);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MarketDeal>> GetBySupplierAsync(
        string supplierName, CancellationToken cancellationToken = default)
    {
        var wanted = MarketSupplierMatch.Normalize(supplierName ?? string.Empty);
        if (wanted.Length == 0)
        {
            return [];
        }

        // F7-T02: the supplier filter runs in SQL on the indexed `supplier_norm` column. The predicate
        // is the SQL form of MarketSupplierMatch (equal, or one contains the other on word
        // boundaries); rows whose column was never written (NULL) are included and decided by the
        // exact in-memory check below, so a partly back-filled table answers exactly as before.
        var padded = " " + wanted + " ";
        var likeWanted = "% " + wanted + " %";
        var records = dbContext.MarketRecords
            .AsNoTracking()
            .Where(r => r.SupplierNorm == null
                || r.SupplierNorm == wanted
                || EF.Functions.Like(" " + r.SupplierNorm + " ", likeWanted)
                || EF.Functions.Like(padded, "% " + r.SupplierNorm + " %"));

        return await ReadDealsAsync(
                records, deal => MarketSupplierMatch.Matches(deal.Supplier, supplierName!), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MarketDeal>> GetByCategoryAsync(
        string category, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return [];
        }

        var folded = MarketFilterColumns.Category(category);
        var records = dbContext.MarketRecords
            .AsNoTracking()
            .Where(r => r.Category == null || r.Category == folded);

        return await ReadDealsAsync(
                records,
                deal => string.Equals(deal.Category, category.Trim(), StringComparison.OrdinalIgnoreCase),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MarketDeal>> GetAllAsync(CancellationToken cancellationToken = default) =>
        LoadWhereAsync(_ => true, cancellationToken);

    /// <inheritdoc />
    public async Task<string?> GetCorpusVersionAsync(CancellationToken cancellationToken = default)
    {
        var count = await dbContext.MarketRecords.CountAsync(cancellationToken).ConfigureAwait(false);
        if (count == 0)
        {
            return "empty";
        }

        var feedVersion = await dbContext.MarketRecords
            .MaxAsync(r => r.FeedVersion, cancellationToken)
            .ConfigureAwait(false);
        return $"{feedVersion}:{count}";
    }

    private static async Task<IReadOnlyList<MarketDeal>> ReadDealsAsync(
        IQueryable<Infrastructure.Entities.MarketRecordEntity> records,
        Func<MarketDeal, bool> exact,
        CancellationToken cancellationToken)
    {
        var rows = await records
            .OrderByDescending(r => r.UpdatedAt)
            .ThenBy(r => r.RecordId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var deals = new List<MarketDeal>(rows.Count);
        foreach (var row in rows)
        {
            var deal = JsonSerializer.Deserialize<MarketDeal>(row.PayloadJson, JsonOptions);
            if (deal is not null && exact(deal))
            {
                deals.Add(deal);
            }
        }

        return deals;
    }

    private async Task<IReadOnlyList<MarketDeal>> LoadWhereAsync(
        Func<MarketDeal, bool> predicate, CancellationToken cancellationToken)
    {
        // F7-T05: with a corpus cache the whole-table read and the JSON parse happen once per corpus
        // version, not once per call; without one (a bare construction) the table is read per call, as
        // before. Supplier and category still live inside PayloadJson here -- F7-T02 moves them to
        // columns.
        if (corpusCache is not null)
        {
            var snapshot = await corpusCache.GetAsync(dbContext, cancellationToken).ConfigureAwait(false);
            return snapshot.Deals.Where(predicate).ToList();
        }

        var records = await dbContext.MarketRecords
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var deals = new List<MarketDeal>();
        foreach (var record in records)
        {
            var deal = JsonSerializer.Deserialize<MarketDeal>(record.PayloadJson, JsonOptions);
            if (deal is not null && predicate(deal))
            {
                deals.Add(deal);
            }
        }

        return deals.OrderByDescending(d => d.UpdatedAt).ToList();
    }
}
