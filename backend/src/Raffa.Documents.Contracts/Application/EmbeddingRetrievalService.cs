using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// Implements task E02/F02/US02/T02 (us-02-embedding-search-index, AC-2/AC-3): the tenant-scoped
/// pgvector similarity search, plus the embedding-generation path that populates the store it
/// searches. Task E02/F02/US02/T01 added the <see cref="Embedding"/> entity and its `vector(1536)`
/// column (AC-1); this task is the first thing that actually writes to it and queries it.
///
/// <b>AC-3</b> ("Embedding generation goes through IAiGateway, never a provider SDK"): both
/// <see cref="IndexChunkAsync"/> and <see cref="SearchAsync"/> obtain every vector from
/// <see cref="IAiGateway.EmbedAsync"/> — this class never touches a Foundry/OpenAI SDK directly,
/// structurally enforced by <c>Raffa.ArchitectureTests.DependencyDirectionTests</c>'s
/// provider-SDK allow-list for this module (same shape as <c>StagedExtractionService</c>'s
/// <see cref="IAiGateway.ExtractAsync"/> dependency).
///
/// <b>AC-2</b> ("similarity search is tenant-filtered — return only authorized tenant rows"):
/// <see cref="SearchAsync"/> follows the same belt-and-suspenders shape as
/// <see cref="PortfolioQueryService"/>/<see cref="DocumentQueryService"/> — an explicit
/// <c>Where(TenantId == tenantId)</c> predicate on top of the Postgres RLS policy the `embedding`
/// table already carries (added by task T01's migration, `Migrations/Scripts/documents-contracts.sql`
/// `tenant_isolation` policy), so a cross-tenant read is denied twice over, not once.
///
/// Opens its own <see cref="ITenantContext.BeginScope"/> per call rather than trusting one is
/// already active (same rationale as every other Application service in this module — see
/// <see cref="PortfolioQueryService"/>'s own doc comment): the RLS connection interceptor and
/// <c>Raffa.AiGateway.Logging.LoggingAiGateway</c> (once wired into composition) both read
/// <see cref="ITenantContext.Current"/>, and the latter throws if no scope is active — so the scope
/// must be open before either the gateway or the database is touched, not just around the query.
///
/// Distance metric is cosine (<c>Vector.CosineDistance</c>, the <c>Pgvector.EntityFrameworkCore</c>
/// LINQ extension that translates to pgvector's <c>&lt;=&gt;</c> operator), the metric pgvector's
/// own docs recommend for normalized text-embedding models — the family ADR-004 names for the
/// `embed` role (`text-embedding-3-small`). Approximate index choice (HNSW vs IVFFlat) stays a
/// later tuning decision per ADR-003/<c>EmbeddingConfiguration</c>'s own doc comment; this task
/// queries the column pgvector already supports without one (exact nearest neighbour via a
/// sequential scan).
/// </summary>
public sealed class EmbeddingRetrievalService(
    DocumentsContractsDbContext dbContext,
    IAiGateway aiGateway,
    ITenantContext tenantContext,
    IClock clock)
{
    /// <summary>
    /// Embeds <paramref name="chunkText"/> via <see cref="IAiGateway.EmbedAsync"/> (AC-3) and
    /// persists it as a new <see cref="Embedding"/> row scoped to <paramref name="tenantId"/>.
    /// <paramref name="sourceType"/>/<paramref name="sourceId"/> are the same loose polymorphic
    /// pointer <see cref="Embedding"/> itself documents (e.g. "Document"/"Clause") — this service
    /// does not validate that the source row exists, mirroring that entity's own "not a single FK"
    /// design.
    /// </summary>
    public Task<Result<EmbeddingIndexResult>> IndexChunkAsync(
        TenantId tenantId,
        string sourceType,
        EntityId sourceId,
        int chunkIndex,
        string chunkText,
        CancellationToken cancellationToken = default) =>
        IndexChunkAsync(tenantId, sourceType, sourceId, chunkIndex, chunkText, page: null, section: null, cancellationToken);

    /// <summary>
    /// Page-aware overload (task E13/F04/US01/T02; <c>inputs/requirements.md</c> R-EVD-01,
    /// R-DOC-07 AC-2): records the 1-based <paramref name="page"/> this chunk was read from and,
    /// when known, its <paramref name="section"/> label, so an Ask citation can land on the page
    /// instead of on the document. Both are honestly <see langword="null"/> when unknown — never a
    /// defaulted page 1.
    /// </summary>
    public async Task<Result<EmbeddingIndexResult>> IndexChunkAsync(
        TenantId tenantId,
        string sourceType,
        EntityId sourceId,
        int chunkIndex,
        string chunkText,
        int? page,
        string? section,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceType))
        {
            return Result<EmbeddingIndexResult>.Failure("A source type is required.");
        }

        if (string.IsNullOrWhiteSpace(chunkText))
        {
            return Result<EmbeddingIndexResult>.Failure("Chunk text is required.");
        }

        // Entry point: open this call's own tenant scope before the gateway or the database is
        // touched (see the type doc comment) — mirrors DocumentUploadService.UploadAsync.
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var embedResult = await aiGateway.EmbedAsync(new AiEmbeddingRequest(chunkText), cancellationToken)
            .ConfigureAwait(false);

        if (embedResult.IsFailure)
        {
            return Result<EmbeddingIndexResult>.Failure(embedResult.Error);
        }

        var vectorValues = embedResult.Value.Vector;

        // Defensive, not redundant: AiGatewayConstants.EmbeddingDimensions and
        // Embedding.VectorDimensions are two independently-maintained constants that "MUST equal"
        // each other by agreement, never a shared reference (ADR-002 forbids the gateway from
        // referencing a domain module — see AiGatewayConstants's own doc comment). A drift between
        // them must fail this call with a named reason, not corrupt the `vector(1536)` column or
        // surface as a raw Postgres error deep inside SaveChangesAsync.
        if (vectorValues.Count != Embedding.VectorDimensions)
        {
            return Result<EmbeddingIndexResult>.Failure(
                $"Embedding model returned a {vectorValues.Count}-dimension vector; expected " +
                $"{Embedding.VectorDimensions} (Embedding.VectorDimensions, ADR-004).");
        }

        var now = clock.UtcNow;

        var embedding = new Embedding
        {
            TenantId = tenantId,
            SourceType = sourceType,
            SourceId = sourceId,
            ChunkIndex = chunkIndex,
            ChunkText = chunkText,
            Page = page,
            Section = string.IsNullOrWhiteSpace(section) ? null : section,
            Vector = new Vector(vectorValues.ToArray()),
            Model = embedResult.Value.Metadata.ModelId,
            CreatedAt = now,
        };

        dbContext.Embeddings.Add(embedding);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<EmbeddingIndexResult>.Success(
            new EmbeddingIndexResult(embedding.Id, embedding.Model, now));
    }

    /// <summary>
    /// Embeds <paramref name="queryText"/> via <see cref="IAiGateway.EmbedAsync"/> (AC-3) and
    /// returns the <paramref name="topK"/> nearest <see cref="Embedding"/> rows for
    /// <paramref name="tenantId"/> only (AC-2), nearest-first by cosine distance. An authorized
    /// caller (e.g. a later Ask Raffa RAG task) can pass <see cref="EmbeddingSearchResult.ChunkText"/>
    /// straight into <see cref="IAiGateway.AnswerAsync"/> evidence — retrieval here has already
    /// applied the tenant authorization boundary spec Appendix C rule 4 requires before any content
    /// reaches an LLM context.
    /// </summary>
    /// <summary>
    /// Deletes every chunk indexed for one source (task E13/F04/US01/T02): the first half of a
    /// reprocess ("replace this document's embeddings with page-aware chunks", R-DOC-07) and part
    /// of a deletion (R-DOC-10). Returns how many rows were removed. Tenant-scoped like every
    /// other entry point here — RLS backstops it, the explicit predicate states it.
    /// </summary>
    public async Task<int> RemoveChunksAsync(
        TenantId tenantId,
        string sourceType,
        EntityId sourceId,
        CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var stale = await dbContext.Embeddings
            .Where(e => e.TenantId == tenantId && e.SourceType == sourceType && e.SourceId == sourceId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (stale.Count == 0)
        {
            return 0;
        }

        dbContext.Embeddings.RemoveRange(stale);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return stale.Count;
    }

    public async Task<Result<IReadOnlyList<EmbeddingSearchResult>>> SearchAsync(
        TenantId tenantId,
        string queryText,
        int topK,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return Result<IReadOnlyList<EmbeddingSearchResult>>.Failure("Query text is required.");
        }

        if (topK <= 0)
        {
            return Result<IReadOnlyList<EmbeddingSearchResult>>.Failure("topK must be a positive number.");
        }

        // Entry point: open this call's own tenant scope before the gateway or the database is
        // touched (see the type doc comment).
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var embedResult = await aiGateway.EmbedAsync(new AiEmbeddingRequest(queryText), cancellationToken)
            .ConfigureAwait(false);

        if (embedResult.IsFailure)
        {
            return Result<IReadOnlyList<EmbeddingSearchResult>>.Failure(embedResult.Error);
        }

        var queryVector = new Vector(embedResult.Value.Vector.ToArray());

        // AC-2: explicit tenant predicate (belt) on top of the `embedding` table's own RLS policy
        // (suspenders, already live from task T01's migration) — see the type doc comment.
        // Distance is projected once here and reused for both ORDER BY and the returned value,
        // rather than calling CosineDistance twice, so Postgres computes it once per row.
        var matches = await dbContext.Embeddings
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .Select(e => new
            {
                Embedding = e,
                Distance = e.Vector.CosineDistance(queryVector),
            })
            .OrderBy(x => x.Distance)
            .Take(topK)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<EmbeddingSearchResult> results = matches
            .Select(x => new EmbeddingSearchResult(
                x.Embedding.Id,
                x.Embedding.SourceType,
                x.Embedding.SourceId,
                x.Embedding.ChunkIndex,
                x.Embedding.ChunkText,
                x.Distance,
                x.Embedding.Page,
                x.Embedding.Section))
            .ToList();

        return Result<IReadOnlyList<EmbeddingSearchResult>>.Success(results);
    }
}
