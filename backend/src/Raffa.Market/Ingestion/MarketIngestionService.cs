using System.Text.Json;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;
using Raffa.Market.Infrastructure.Entities;
using Raffa.Market.Retrieval;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace Raffa.Market.Ingestion;

/// <summary>
/// The ingestion job ADR-024 names as the *only* caller of
/// <see cref="IMarketIntelligenceProvider"/>: "the provider is called only by that job; at question
/// time Ask reads Raffa's own store and nothing else" (task objective). Reads the feed, upserts
/// <c>market_record</c> by <see cref="MarketDeal.RecordId"/>, composes one narrative per record
/// (<see cref="MarketNoteComposer"/>) and embeds it through <see cref="IAiGateway.EmbedAsync"/>
/// (ADR-004 `embed` role, hash-logged via <c>Raffa.AiGateway.Logging.LoggingAiGateway</c>),
/// replacing that record's <c>market_embedding</c> row(s) — idempotent: a second run with the same
/// feed version and payload changes zero rows and issues zero embed calls (parent story AC-4).
///
/// <para>
/// T-05 / F7-T03: changed records are embedded in batches (<see cref="MarketIngestionOptions"/>),
/// optionally behind a TPM limiter (<see cref="EmbeddingRateLimiter"/>) and optionally committed
/// per batch. The defaults (batch size 1, no limiter, one commit) are the original behaviour.
/// </para>
///
/// Never registered unless <c>ServiceCollectionExtensions.AddMarketModule</c> is called with a
/// non-null connection string (there is nothing to ingest into otherwise) — the Worker's
/// `ingest-market` command (<c>Raffa.Worker.Commands.IngestMarketCommand</c>) is its only caller
/// today.
/// </summary>
public sealed class MarketIngestionService(
    MarketDbContext dbContext,
    IMarketIntelligenceProvider provider,
    IAiGateway aiGateway,
    ITenantContext tenantContext,
    IClock clock,
    MarketIngestionOptions? options = null,
    EmbeddingRateLimiter? limiter = null,
    IMarketBatchCurator? curator = null)
{
    /// <summary>
    /// Every AI Gateway call — including this job's own <see cref="IAiGateway.EmbedAsync"/> calls
    /// — flows through <c>Raffa.AiGateway.Logging.LoggingAiGateway</c>, which requires an active
    /// <see cref="ITenantContext.BeginScope"/> scope to attribute its hash-only audit row (ADR-011)
    /// and throws otherwise: "AI Gateway logging requires an active tenant scope". This job is not
    /// tenant-scoped at all (ADR-024: the market index is shared, read-only, never a tenant row) —
    /// there is no real <see cref="TenantId"/> to open a scope with. <see cref="Guid.Empty"/> is the
    /// well-known "not a real tenant, a system job produced this log line" sentinel:
    /// <c>Raffa.Identity.Workspace</c> provisioning only ever assigns <c>TenantId.New()</c>
    /// (a random v4 guid) to a real tenant, so <see cref="Guid.Empty"/> can never collide with one.
    /// The resulting audit row's <c>tenant_id</c> column reads <c>00000000-0000-0000-0000-000000000000</c>
    /// — an honest, greppable marker that this particular AI call came from the market ingestion
    /// job, not from any customer's own request.
    /// </summary>
    public static readonly TenantId SystemTenantId = new(Guid.Empty);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly MarketIngestionOptions _options = options ?? new MarketIngestionOptions();

    private readonly EmbeddingRateLimiter _limiter =
        limiter ?? new EmbeddingRateLimiter((options ?? new MarketIngestionOptions()).EmbedTokensPerMinute);

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

        // F7-T08: the corpus version this run writes into -- the active one (in place, the default) or a
        // new one built next to it (blue/green). Rows are always addressed by version explicitly, so
        // the "active only" read filter is bypassed here.
        var versions = new MarketCorpusVersionService(dbContext, clock);
        var corpusVersion = _options.BlueGreen
            ? await versions.BeginBuildAsync(snapshot.FeedVersion, cancellationToken).ConfigureAwait(false)
            : await versions.GetActiveVersionAsync(cancellationToken).ConfigureAwait(false);

        var existingByRecordId = await dbContext.MarketRecords
            .IgnoreQueryFilters()
            .Where(r => r.CorpusVersion == corpusVersion)
            .ToDictionaryAsync(r => r.RecordId, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        // What each record's embedding was made from (text, model, kind) -- no vectors, so this stays
        // small. Lets a record whose payload changed but whose composed note did not (a new
        // non-narrative field, such as industry or unitMetric) keep its embedding instead of paying
        // for a new embed call.
        var existingEmbeddings = (await dbContext.MarketEmbeddings
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(e => e.CorpusVersion == corpusVersion && e.ChunkIndex == 0)
                .Select(e => new EmbeddingSource(e.RecordId, e.ChunkText, e.Model, e.IsFixture))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .GroupBy(e => e.RecordId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // The embedding identity (model, fixture-or-not) this run actually embeds with, learnt from
        // the first embed call of the run. Until it is known nothing can be reused, so the first
        // changed record (or, batched, the first batch) always embeds.
        MarketEmbeddingIdentity? runIdentity = null;

        var inserted = 0;
        var updated = 0;
        var unchanged = 0;
        var rejections = new List<MarketRejection>();

        var batchSize = Math.Max(1, _options.EmbedBatchSize);
        var pending = new List<PendingRecord>(batchSize);

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
            //
            // F7-T04: rows written since `payload_hash` exists carry the SHA-256 of that canonical
            // payload, so the check is one string comparison and no jsonb round trip. A row that
            // predates the column (null hash) falls back to the logical comparison above and, when
            // it is unchanged, gets its hash back-filled so the next run takes the fast path.
            var payloadHash = MarketPayloadHash.Of(payloadJson);
            var sameVersion = existing is not null
                && string.Equals(existing.FeedVersion, snapshot.FeedVersion, StringComparison.Ordinal);

            if (existing is not null && sameVersion && existing.PayloadHash is not null)
            {
                if (string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
                {
                    // F7-T02: a row that predates the filter columns gets them back-filled.
                    if (MarketFilterColumns.IsMissing(existing))
                    {
                        MarketFilterColumns.Apply(existing, deal);
                    }

                    unchanged++;
                    continue;
                }
            }
            else if (existing is not null && sameVersion)
            {
                var existingNormalizedPayloadJson = JsonSerializer.Serialize(
                    JsonSerializer.Deserialize<MarketDeal>(existing.PayloadJson, JsonOptions), JsonOptions);
                if (string.Equals(existingNormalizedPayloadJson, payloadJson, StringComparison.Ordinal))
                {
                    existing.PayloadHash = payloadHash;
                    if (MarketFilterColumns.IsMissing(existing))
                    {
                        MarketFilterColumns.Apply(existing, deal);
                    }

                    unchanged++;
                    continue;
                }
            }

            pending.Add(new PendingRecord(
                deal, payloadJson, payloadHash, existing, MarketNoteComposer.Compose(deal)));
            if (pending.Count < batchSize)
            {
                continue;
            }

            var flushed = await FlushAsync(
                    pending, snapshot.FeedVersion, corpusVersion, existingEmbeddings, runIdentity, cancellationToken)
                .ConfigureAwait(false);
            if (flushed.IsFailure)
            {
                return Result<IngestionSummary>.Failure(flushed.Error);
            }

            runIdentity = flushed.Value.RunIdentity;
            inserted += flushed.Value.Inserted;
            updated += flushed.Value.Updated;
            rejections.AddRange(flushed.Value.Rejected);
            pending.Clear();
        }

        if (pending.Count > 0)
        {
            var flushed = await FlushAsync(
                    pending, snapshot.FeedVersion, corpusVersion, existingEmbeddings, runIdentity, cancellationToken)
                .ConfigureAwait(false);
            if (flushed.IsFailure)
            {
                return Result<IngestionSummary>.Failure(flushed.Error);
            }

            inserted += flushed.Value.Inserted;
            updated += flushed.Value.Updated;
            rejections.AddRange(flushed.Value.Rejected);
        }

        // One SaveChangesAsync for the whole run (unless CheckpointPerBatch committed each batch
        // already): when every record was unchanged, the change tracker holds nothing
        // added/modified/removed, so this issues zero SQL and returns 0 -- the literal "changes zero
        // rows" the idempotency proof asserts.
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (!_options.BlueGreen)
        {
            return Result<IngestionSummary>.Success(
                new IngestionSummary(
                    snapshot.FeedVersion, inserted, updated, unchanged, corpusVersion, 0, rejections));
        }

        // Blue/green: the building version should mirror the feed, so a record the feed no longer
        // carries is dropped from it (its embeddings go by cascade); the live version is untouched.
        var feedIds = snapshot.Deals.Select(d => d.RecordId).ToHashSet(StringComparer.Ordinal);
        var removed = await dbContext.MarketRecords
            .IgnoreQueryFilters()
            .Where(r => r.CorpusVersion == corpusVersion && !feedIds.Contains(r.RecordId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (inserted + updated + removed == 0)
        {
            // Nothing differs from the live corpus: discard the copy instead of switching to an
            // identical one.
            await versions.DeleteAsync(corpusVersion, cancellationToken).ConfigureAwait(false);
            return Result<IngestionSummary>.Success(
                new IngestionSummary(
                    snapshot.FeedVersion, 0, 0, unchanged,
                    await versions.GetActiveVersionAsync(cancellationToken).ConfigureAwait(false), 0, rejections));
        }

        await versions.SwitchAsync(corpusVersion, cancellationToken).ConfigureAwait(false);
        await versions.PurgeRetiredAsync(_options.RetiredVersionsToKeep, cancellationToken).ConfigureAwait(false);

        return Result<IngestionSummary>.Success(
            new IngestionSummary(
                snapshot.FeedVersion, inserted, updated, unchanged, corpusVersion, removed, rejections));
    }

    private sealed record EmbeddingSource(string RecordId, string ChunkText, string Model, bool? IsFixture);

    private sealed record PendingRecord(
        MarketDeal Deal, string PayloadJson, string PayloadHash, MarketRecordEntity? Existing, MarketNote Note);

    private sealed record FlushOutcome(
        int Inserted, int Updated, MarketEmbeddingIdentity? RunIdentity, IReadOnlyList<MarketRejection> Rejected);

    /// <summary>
    /// Embeds the records of one batch (those that cannot keep their previous embedding) with a
    /// single gateway call, then stages their rows. With <c>EmbedBatchSize = 1</c> this is the
    /// original one-record-one-<c>EmbedAsync</c> path.
    /// </summary>
    private async Task<Result<FlushOutcome>> FlushAsync(
        List<PendingRecord> pendingBatch,
        string feedVersion,
        string corpusVersion,
        IReadOnlyDictionary<string, EmbeddingSource> existingEmbeddings,
        MarketEmbeddingIdentity? runIdentity,
        CancellationToken cancellationToken)
    {
        // A-11 / F7-T09: the batch of changed records goes through the curator (data quality, provenance)
        // before anything is embedded or stored. Rejected records are left out; accepted ones may come
        // back completed. The idempotency hash stays that of the feed payload, so a curated record is
        // still "unchanged" on the next run.
        var batch = pendingBatch;
        IReadOnlyList<MarketRejection> rejected = [];
        if (curator is not null && pendingBatch.Count > 0)
        {
            var outcome = await curator
                .CurateAsync(pendingBatch.Select(p => p.Deal).ToList(), cancellationToken)
                .ConfigureAwait(false);
            rejected = outcome.Rejected;

            var byId = pendingBatch.ToDictionary(p => p.Deal.RecordId, StringComparer.Ordinal);
            batch = [];
            foreach (var deal in outcome.Accepted)
            {
                if (byId.TryGetValue(deal.RecordId, out var original))
                {
                    batch.Add(original with
                    {
                        Deal = deal,
                        PayloadJson = JsonSerializer.Serialize(deal, JsonOptions),
                        Note = MarketNoteComposer.Compose(deal),
                    });
                }
            }
        }

        // Keep the embedding when the narrative it was made from is unchanged, and it was made by
        // the same model and kind this run embeds with: only a payload field outside the note
        // moved, so a new vector would be identical. Never for a new record, and never before this
        // run has embedded once (the run's identity is unknown until then).
        var reuse = new bool[batch.Count];
        var toEmbed = new List<int>(batch.Count);
        for (var i = 0; i < batch.Count; i++)
        {
            var item = batch[i];
            reuse[i] = item.Existing is not null
                && runIdentity is { } known
                && existingEmbeddings.TryGetValue(item.Deal.RecordId, out var previous)
                && string.Equals(previous.ChunkText, item.Note.Snippet, StringComparison.Ordinal)
                && previous.IsFixture == known.IsFixture
                && string.Equals(previous.Model, known.Model, StringComparison.Ordinal);
            if (!reuse[i])
            {
                toEmbed.Add(i);
            }
        }

        IReadOnlyList<IReadOnlyList<float>> vectors = [];
        var identity = default(MarketEmbeddingIdentity);
        var newRunIdentity = runIdentity;

        if (toEmbed.Count > 0)
        {
            var texts = toEmbed.Select(i => batch[i].Note.Snippet).ToList();
            var label = toEmbed.Count == 1
                ? $"record '{batch[toEmbed[0]].Deal.RecordId}'"
                : $"records '{batch[toEmbed[0]].Deal.RecordId}'..'{batch[toEmbed[^1]].Deal.RecordId}'";

            if (_limiter.Enabled)
            {
                await _limiter.WaitAsync(texts.Sum(EmbeddingRateLimiter.EstimateTokens), cancellationToken)
                    .ConfigureAwait(false);
            }

            if (_options.EmbedBatchSize <= 1)
            {
                var embedResult = await aiGateway
                    .EmbedAsync(new AiEmbeddingRequest(texts[0]), cancellationToken)
                    .ConfigureAwait(false);
                if (embedResult.IsFailure)
                {
                    return Result<FlushOutcome>.Failure($"Ingestion failed embedding {label}: {embedResult.Error}");
                }

                vectors = [embedResult.Value.Vector];
                identity = MarketEmbeddingIdentity.From(embedResult.Value.Metadata);
            }
            else
            {
                var embedResult = await aiGateway.EmbedBatchAsync(texts, cancellationToken).ConfigureAwait(false);
                if (embedResult.IsFailure)
                {
                    return Result<FlushOutcome>.Failure($"Ingestion failed embedding {label}: {embedResult.Error}");
                }

                if (embedResult.Value.Vectors.Count != texts.Count)
                {
                    return Result<FlushOutcome>.Failure(
                        $"Ingestion failed embedding {label}: the gateway returned " +
                        $"{embedResult.Value.Vectors.Count} vectors for {texts.Count} texts.");
                }

                vectors = embedResult.Value.Vectors;
                identity = MarketEmbeddingIdentity.From(embedResult.Value.Metadata);
            }

            // Defensive, not redundant -- same "two independently-maintained constants that MUST
            // agree, never a shared reference" reasoning
            // Raffa.Documents.Contracts.Application.EmbeddingRetrievalService.IndexChunkAsync's
            // own identical check already documents for ADR-004.
            for (var k = 0; k < vectors.Count; k++)
            {
                if (vectors[k].Count != MarketEmbeddingEntity.VectorDimensions)
                {
                    return Result<FlushOutcome>.Failure(
                        $"Embed model returned a {vectors[k].Count}-dimension vector for record " +
                        $"'{batch[toEmbed[k]].Deal.RecordId}'; expected {MarketEmbeddingEntity.VectorDimensions} " +
                        "(MarketEmbeddingEntity.VectorDimensions, ADR-004).");
                }
            }

            // F7-T06: record who produced the vector (model + fixture-or-not) so query time can
            // refuse an index built with something the query is not comparable with.
            newRunIdentity = identity;
        }

        var inserted = 0;
        var updated = 0;
        var now = clock.UtcNow;
        var vectorIndex = 0;

        for (var i = 0; i < batch.Count; i++)
        {
            var (deal, payloadJson, payloadHash, existing, note) = batch[i];
            var provenanceLabel = MarketProvenance.Label(deal);

            if (existing is null)
            {
                var added = new MarketRecordEntity
                {
                    RecordId = deal.RecordId,
                    CorpusVersion = corpusVersion,
                    FeedVersion = feedVersion,
                    Provider = deal.Provider,
                    PayloadJson = payloadJson,
                    PayloadHash = payloadHash,
                    ProvenanceLabel = provenanceLabel,
                    UpdatedAt = deal.UpdatedAt,
                };
                MarketFilterColumns.Apply(added, deal);
                dbContext.MarketRecords.Add(added);
                inserted++;
            }
            else
            {
                existing.FeedVersion = feedVersion;
                existing.Provider = deal.Provider;
                existing.PayloadJson = payloadJson;
                existing.PayloadHash = payloadHash;
                MarketFilterColumns.Apply(existing, deal);
                existing.ProvenanceLabel = provenanceLabel;
                existing.UpdatedAt = deal.UpdatedAt;

                // "replace the record's embeddings" (task objective): a changed record's stale
                // chunk(s) never linger next to the freshly-embedded one added below. Never reached
                // for a brand-new record (existing is null there), so this never queries against a
                // record that cannot yet own any embedding rows.
                if (!reuse[i])
                {
                    var staleEmbeddings = await dbContext.MarketEmbeddings
                        .IgnoreQueryFilters()
                        .Where(e => e.RecordId == deal.RecordId && e.CorpusVersion == corpusVersion)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    dbContext.MarketEmbeddings.RemoveRange(staleEmbeddings);
                }

                updated++;
            }

            if (!reuse[i])
            {
                dbContext.MarketEmbeddings.Add(new MarketEmbeddingEntity
                {
                    Id = EntityId.New(),
                    RecordId = deal.RecordId,
                    CorpusVersion = corpusVersion,
                    ChunkIndex = 0,
                    ChunkText = note.Snippet,
                    Vector = new Vector(vectors[vectorIndex++].ToArray()),
                    Model = identity.Model,
                    IsFixture = identity.IsFixture,
                    CreatedAt = now,
                });
            }
        }

        // Checkpoint per lot (T-05): what this batch embedded is committed before the next call.
        if (_options.CheckpointPerBatch)
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result<FlushOutcome>.Success(new FlushOutcome(inserted, updated, newRunIdentity, rejected));
    }
}
