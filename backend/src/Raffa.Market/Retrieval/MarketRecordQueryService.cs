using System.Text.Json;
using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;
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
public sealed class MarketRecordQueryService(MarketDbContext dbContext)
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
}
