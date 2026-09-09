namespace Contigo.Market.Ingestion;

/// <summary>
/// <see cref="MarketIngestionService.IngestAsync"/>'s own result shape (task objective: "return a
/// summary with inserted / updated / unchanged counts") — the operator-facing proof that a
/// re-ingest of the same feed changed nothing (parent story AC-4).
/// </summary>
/// <param name="FeedVersion">The feed version this run actually ingested (<see cref="Contracts.MarketFeedSnapshot.FeedVersion"/>).</param>
/// <param name="Inserted">Records seen for the first time — a new <c>market_record</c> row (and its
/// first <c>market_embedding</c> chunk) was written.</param>
/// <param name="Updated">Records that already existed but whose feed version or payload changed —
/// the <c>market_record</c> row and its <c>market_embedding</c> chunk(s) were replaced.</param>
/// <param name="Unchanged">Records whose feed version and payload were byte-identical to what was
/// already persisted — no database write and no <c>IAiGateway.EmbedAsync</c> call for these at
/// all (idempotency, parent story AC-4).</param>
public sealed record IngestionSummary(
    string FeedVersion,
    int Inserted,
    int Updated,
    int Unchanged);
