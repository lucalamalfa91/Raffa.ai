using Contigo.SharedKernel;
using Pgvector;

namespace Contigo.Market.Infrastructure.Entities;

/// <summary>
/// The persisted <c>market_embedding</c> row (task objective, verbatim column list: "id, recordId
/// FK, chunkIndex, chunkText, vector(1536), model, createdAt") — the shared, read-only market notes
/// index ADR-011's epic-13 amendment names: "its own vector index `market_embedding`: no
/// `tenant_id`, readable by every tenant, written only by the ingestion job, never containing
/// tenant content, never joined with tenant tables." Reuses
/// <c>Contigo.Documents.Contracts.Domain.Embedding</c>'s own conventions (vector width, a
/// <see cref="Model"/> column) by agreement, not by reference — this module must not depend on
/// <c>Contigo.Documents.Contracts</c> at all (task's own "Do not touch" list;
/// <c>Contigo.ArchitectureTests.DependencyDirectionTests</c>'s allow-list for
/// <c>Contigo.Market</c> does not include it either).
/// </summary>
public sealed class MarketEmbeddingEntity
{
    /// <summary>Fixed at schema time, same value and rationale as
    /// <c>Contigo.Documents.Contracts.Domain.Embedding.VectorDimensions</c> (ADR-004: "small
    /// dimension preferred for cost/size" — matches Foundry's `text-embedding-3-small`). Recorded
    /// as an assumption in force in <c>reports/open-questions.md</c> (OQ-impl-001, EF-Core +
    /// pgvector wiring lane) — duplicated here by agreement, the same "cannot share a constant
    /// across this dependency boundary" reasoning
    /// <c>Contigo.AiGateway.AiGatewayConstants.EmbeddingDimensions</c>'s own doc comment states.</summary>
    public const int VectorDimensions = 1536;

    public required EntityId Id { get; set; }

    /// <summary>The owning <see cref="MarketRecordEntity.RecordId"/> — one narrative chunk per
    /// record today (<c>Retrieval.MarketNoteComposer</c> composes exactly one snippet per
    /// <see cref="Contracts.MarketDeal"/>), but modelled as a chunk list (<see cref="ChunkIndex"/>)
    /// rather than a 1:1 column so a future, longer narrative can split into more than one chunk
    /// without a schema change.</summary>
    public required string RecordId { get; set; }

    public int ChunkIndex { get; set; }

    /// <summary>The composed narrative text this row's <see cref="Vector"/> was embedded from
    /// (<c>Retrieval.MarketNoteComposer.Compose(deal).Snippet</c>) — kept verbatim so a hit can be
    /// cited/displayed without re-deserializing <see cref="MarketRecordEntity.PayloadJson"/> and
    /// re-composing it.</summary>
    public required string ChunkText { get; set; }

    public required Vector Vector { get; set; }

    /// <summary>Foundry embedding model id/version that produced this vector (ADR-004; brief §8
    /// logging) — echoes <c>Contracts.AiCallMetadata.ModelId</c> from the
    /// <c>IAiGateway.EmbedAsync</c> call that produced it.</summary>
    public required string Model { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
