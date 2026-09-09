using System.Text.Json;
using Contigo.AiGateway;
using Contigo.AiGateway.Contracts;
using Contigo.Market.Contracts;
using Contigo.Market.Infrastructure;
using Contigo.Market.Infrastructure.Entities;
using Contigo.Market.Retrieval;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace Contigo.Market.Ingestion;

/// <summary>
/// The ingestion job ADR-024 names as the *only* caller of
/// <see cref="IMarketIntelligenceProvider"/>: "the provider is called only by that job; at question
/// time Ask reads Contigo's own store and nothing else" (task objective). Reads the feed, upserts
/// <c>market_record</c> by <see cref="MarketDeal.RecordId"/>, composes one narrative per record
/// (<see cref="MarketNoteComposer"/>) and embeds it through <see cref="IAiGateway.EmbedAsync"/>
/// (ADR-004 `embed` role, hash-logged via <c>Contigo.AiGateway.Logging.LoggingAiGateway</c>),
/// replacing that record's <c>market_embedding</c> row(s) — idempotent: a second run with the same
/// feed version and payload changes zero rows and issues zero embed calls (parent story AC-4).
///
/// Never registered unless <c>ServiceCollectionExtensions.AddMarketModule</c> is called with a
/// non-null connection string (there is nothing to ingest into otherwise) — the Worker's
/// `ingest-market` command (<c>Contigo.Worker.Commands.IngestMarketCommand</c>) is its only caller
/// today.
/// </summary>
public sealed class MarketIngestionService(
    MarketDbContext dbContext,
    IMarketIntelligenceProvider provider,
    IAiGateway aiGateway,
    ITenantContext tenantContext,
    IClock clock)
{
    /// <summary>
    /// Every AI Gateway call — including this job's own <see cref="IAiGateway.EmbedAsync"/> calls
    /// — flows through <c>Contigo.AiGateway.Logging.LoggingAiGateway</c>, which requires an active
    /// <see cref="ITenantContext.BeginScope"/> scope to attribute its hash-only audit row (ADR-011)
    /// and throws otherwise: "AI Gateway logging requires an active tenant scope". This job is not
    /// tenant-scoped at all (ADR-024: the market index is shared, read-only, never a tenant row) —
    /// there is no real <see cref="TenantId"/> to open a scope with. <see cref="Guid.Empty"/> is the
    /// well-known "not a real tenant, a system job produced this log line" sentinel:
    /// <c>Contigo.Identity.Workspace</c> provisioning only ever assigns <c>TenantId.New()</c>
    /// (a random v4 guid) to a real tenant, so <see cref="Guid.Empty"/> can never collide with one.
    /// The resulting audit row's <c>tenant_id</c> column reads <c>00000000-0000-0000-0000-000000000000</c>
    /// — an honest, greppable marker that this particular AI call came from the market ingestion
    /// job, not from any customer's own request.
    /// </summary>
    public static readonly TenantId SystemTenantId = new(Guid.Empty);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Runs one ingestion pass. <paramref name="feedVersion"/> is forwarded to
    /// <see cref="IMarketIntelligenceProvider.GetDealsAsync"/> unchanged (<see langword="null"/> —
    /// the common case — asks for the provider's current feed); this method itself has no opinion
    /// on feed versioning beyond comparing whatever version the provider hands back against what is
    /// already persisted per record.
    /// </summary>
    public async Task<Result<IngestionSummary>> IngestAsync(
        string? feedVersion = null, CancellationToken cancellationToken = default)
    {
        var feedResult = await provider.GetDealsAsync(feedVersion, cancellationToken).ConfigureAwait(false);
        if (feedResult.IsFailure)
        {
            return Result<IngestionSummary>.Failure(feedResult.Error);
        }

        var snapshot = feedResult.Value;

        var existingByRecordId = await dbContext.MarketRecords
            .ToDictionaryAsync(r => r.RecordId, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var inserted = 0;
        var updated = 0;
        var unchanged = 0;

        // See SystemTenantId's own doc comment: opened once for the whole run, before the first
        // embed call, and disposed only after every record has been considered.
        using var tenantScope = tenantContext.BeginScope(SystemTenantId);

        foreach (var deal in snapshot.Deals)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var payloadJson = JsonSerializer.Serialize(deal, JsonOptions);
            existingByRecordId.TryGetValue(deal.RecordId, out var existing);

            // Idempotency (parent story AC-4, task objective): "a second run with the same feed
            // version and payload hash changes zero rows". Comparing existing.PayloadJson directly
            // against payloadJson looks byte-identical in principle, but MarketRecordEntity.PayloadJson
            // is mapped to a `jsonb` column (MarketRecordConfiguration), and Postgres's jsonb type does
            // not preserve object key order (or whitespace) on round trip -- only the plain json/text
            // types do. existing.PayloadJson was just read back from jsonb (via existingByRecordId's own
            // MarketRecords query above); payloadJson was freshly produced by JsonSerializer.Serialize
            // moments ago -- the two can differ byte-for-byte even when every field is logically
            // identical. Re-serializing existing's own payload (deserialize, then serialize with this
            // same JsonOptions) puts both sides of the comparison through System.Text.Json's own
            // canonical key order, so this is an exact logical-equality check regardless of what order
            // jsonb happens to hand keys back in.
            var existingNormalizedPayloadJson = existing is null
                ? null
                : JsonSerializer.Serialize(
                    JsonSerializer.Deserialize<MarketDeal>(existing.PayloadJson, JsonOptions), JsonOptions);

            if (existing is not null
                && string.Equals(existing.FeedVersion, snapshot.FeedVersion, StringComparison.Ordinal)
                && string.Equals(existingNormalizedPayloadJson, payloadJson, StringComparison.Ordinal))
            {
                unchanged++;
                continue;
            }

            var provenanceLabel = MarketProvenance.Label(deal);
            var note = MarketNoteComposer.Compose(deal);

            var embedResult = await aiGateway
                .EmbedAsync(new AiEmbeddingRequest(note.Snippet), cancellationToken)
                .ConfigureAwait(false);

            if (embedResult.IsFailure)
            {
                return Result<IngestionSummary>.Failure(
                    $"Ingestion failed embedding record '{deal.RecordId}': {embedResult.Error}");
            }

            var vectorValues = embedResult.Value.Vector;

            // Defensive, not redundant -- same "two independently-maintained constants that MUST
            // agree, never a shared reference" reasoning
            // Contigo.Documents.Contracts.Application.EmbeddingRetrievalService.IndexChunkAsync's
            // own identical check already documents for ADR-004.
            if (vectorValues.Count != MarketEmbeddingEntity.VectorDimensions)
            {
                return Result<IngestionSummary>.Failure(
                    $"Embed model returned a {vectorValues.Count}-dimension vector for record " +
                    $"'{deal.RecordId}'; expected {MarketEmbeddingEntity.VectorDimensions} " +
                    "(MarketEmbeddingEntity.VectorDimensions, ADR-004).");
            }

            var now = clock.UtcNow;

            if (existing is null)
            {
                dbContext.MarketRecords.Add(new MarketRecordEntity
                {
                    RecordId = deal.RecordId,
                    FeedVersion = snapshot.FeedVersion,
                    Provider = deal.Provider,
                    PayloadJson = payloadJson,
                    ProvenanceLabel = provenanceLabel,
                    UpdatedAt = deal.UpdatedAt,
                });
                inserted++;
            }
            else
            {
                existing.FeedVersion = snapshot.FeedVersion;
                existing.Provider = deal.Provider;
                existing.PayloadJson = payloadJson;
                existing.ProvenanceLabel = provenanceLabel;
                existing.UpdatedAt = deal.UpdatedAt;

                // "replace the record's embeddings" (task objective): a changed record's stale
                // chunk(s) never linger next to the freshly-embedded one added below. Never reached
                // for a brand-new record (existing is null there), so this never queries against a
                // record that cannot yet own any embedding rows.
                var staleEmbeddings = await dbContext.MarketEmbeddings
                    .Where(e => e.RecordId == deal.RecordId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                dbContext.MarketEmbeddings.RemoveRange(staleEmbeddings);

                updated++;
            }

            dbContext.MarketEmbeddings.Add(new MarketEmbeddingEntity
            {
                Id = EntityId.New(),
                RecordId = deal.RecordId,
                ChunkIndex = 0,
                ChunkText = note.Snippet,
                Vector = new Vector(vectorValues.ToArray()),
                Model = embedResult.Value.Metadata.ModelId,
                CreatedAt = now,
            });
        }

        // One SaveChangesAsync for the whole run: when every record was unchanged, the change
        // tracker holds nothing added/modified/removed, so this issues zero SQL and returns 0 --
        // the literal "changes zero rows" the idempotency proof asserts.
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<IngestionSummary>.Success(
            new IngestionSummary(snapshot.FeedVersion, inserted, updated, unchanged));
    }
}
