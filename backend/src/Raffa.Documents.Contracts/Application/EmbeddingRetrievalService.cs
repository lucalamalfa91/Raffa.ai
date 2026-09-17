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
    /// <summary>Same discriminator <c>DocumentProcessingPipeline</c> indexes page chunks under
    /// (mirrors <see cref="ContractEvidenceQueryService"/>'s own private constant of the same
    /// name) — the only <see cref="Embedding.SourceType"/> anything actually writes today, so
    /// <see cref="SearchByContractAsync"/> resolves "this contract"/"similar types" membership by
    /// joining through <see cref="Domain.Document.ContractId"/>, never a <c>Clause</c>-sourced row.</summary>
    private const string DocumentSourceType = "Document";

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
            .Select(x => ToSearchResult(x.Embedding, x.Distance))
            .ToList();

        return Result<IReadOnlyList<EmbeddingSearchResult>>.Success(results);
    }

    /// <summary>
    /// Task E28/F02/US01/T01 (NW-81; ADR-024 w19 cl. 15; ADR-011 market isolation): the
    /// contract-scoped counterpart to <see cref="SearchAsync"/>. <see cref="SearchAsync"/> is a
    /// cosine top-K over <b>every</b> embedding this tenant owns — correct for an unscoped
    /// question with no named contract, but the exact defect this task closes for a scoped turn: a
    /// tenant with 37 contracts got another supplier's MSA back just because it embedded closer to
    /// the query than anything from the contract the user actually asked about (Q2/Q3 "mixing 37
    /// contracts").
    ///
    /// <para>
    /// Returns two independent slices, both nearest-first by cosine distance, from one embedded
    /// query vector (a single <see cref="IAiGateway.EmbedAsync"/> call, AC-3 — the two slices are a
    /// read-side split, not two separate questions):
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>This contract</b> (AC-1, <see cref="EmbeddingContractScopedSearchResult.ThisContract"/>):
    /// only <see cref="Embedding"/> rows whose <see cref="Embedding.SourceId"/> resolves — via
    /// <see cref="Domain.Document.ContractId"/>, since <see cref="Embedding"/> itself carries no
    /// contract id, only the loose <see cref="Embedding.SourceType"/>/<see cref="Embedding.SourceId"/>
    /// pointer (see that entity's own doc comment) — to <see cref="EmbeddingSearchQuery.ContractId"/>
    /// itself.</description></item>
    /// <item><description><b>Similar types</b> (AC-2, <see cref="EmbeddingContractScopedSearchResult.SimilarTypes"/>):
    /// a labelled peer slice at <see cref="EmbeddingSearchQuery.PeerTopK"/> (deliberately lower than
    /// <see cref="EmbeddingSearchQuery.TopK"/>) drawn from <b>other</b> contracts of the same
    /// <see cref="ContractDocumentType"/> — "same type/category" per the wave's own row — that are
    /// themselves validated (at least one linked <see cref="Domain.Document"/> reached
    /// <see cref="DocumentProcessingStatus.Completed"/>; the same definition
    /// <see cref="PortfolioQueryService.CountValidatedContractsAsync"/> already establishes as "the
    /// one definition of validated", never <see cref="Contract.Status"/>, which is the literal
    /// <c>"processing"</c> string for a bootstrapped-but-unprocessed shell). Callers keep this slice
    /// labelled as "similar contract" evidence (never this contract's own) — see
    /// <c>AskCopilotService.BuildClausePackAsync</c>.</description></item>
    /// </list>
    ///
    /// <para>
    /// <b>Market never enters this method (AC-3, ADR-011).</b> Both slices are drawn exclusively
    /// from this tenant's own <see cref="Embedding"/> rows in Postgres — the market-intelligence
    /// feed is a separate corpus behind <c>IMarketKnowledgeRetrieval</c> with its own index, never
    /// this pgvector table (ADR-024 "three sources of truth", ADR-011 "no-training... never mixed
    /// into tenant pgvector"). This method has no market dependency to accidentally reach for.
    /// </para>
    ///
    /// <para>
    /// RLS remains the non-bypassable backstop (ADR-009, not re-decided by this task): the explicit
    /// <c>tenant_id</c> predicates below are belt-and-suspenders on top of it, the same shape every
    /// other query in this class already uses.
    /// </para>
    /// </summary>
    public async Task<Result<EmbeddingContractScopedSearchResult>> SearchByContractAsync(
        EmbeddingSearchQuery query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.QueryText))
        {
            return Result<EmbeddingContractScopedSearchResult>.Failure("Query text is required.");
        }

        if (query.TopK <= 0)
        {
            return Result<EmbeddingContractScopedSearchResult>.Failure("topK must be a positive number.");
        }

        if (query.PeerTopK < 0)
        {
            return Result<EmbeddingContractScopedSearchResult>.Failure("peerTopK must not be negative.");
        }

        // Entry point: open this call's own tenant scope before the gateway or the database is
        // touched (see the type doc comment).
        using var tenantScope = tenantContext.BeginScope(query.TenantId);

        var embedResult = await aiGateway.EmbedAsync(new AiEmbeddingRequest(query.QueryText), cancellationToken)
            .ConfigureAwait(false);

        if (embedResult.IsFailure)
        {
            return Result<EmbeddingContractScopedSearchResult>.Failure(embedResult.Error);
        }

        var queryVector = new Vector(embedResult.Value.Vector.ToArray());

        // AC-1: "this contract" is resolved via SourceId/SourceType, not a column on Embedding
        // itself — same two-step (documents-of-the-contract, then embeddings-of-those-documents)
        // ContractEvidenceQueryService already uses, materialising the id list first rather than
        // nesting an un-materialised IQueryable.Contains subquery, for the same reason that file
        // does: a proven-translatable shape over a value-converted EntityId column.
        var thisContractDocumentIds = await dbContext.Documents
            .AsNoTracking()
            .Where(d => d.TenantId == query.TenantId && d.ContractId.HasValue && d.ContractId.Value == query.ContractId)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var thisContractMatches = thisContractDocumentIds.Count == 0
            ? []
            : await dbContext.Embeddings
                .AsNoTracking()
                .Where(e => e.TenantId == query.TenantId
                    && e.SourceType == DocumentSourceType
                    && thisContractDocumentIds.Contains(e.SourceId))
                .Select(e => new { Embedding = e, Distance = e.Vector.CosineDistance(queryVector) })
                .OrderBy(x => x.Distance)
                .Take(query.TopK)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        IReadOnlyList<EmbeddingSearchResult> similarTypes = [];

        if (query.PeerTopK > 0)
        {
            // Cast to nullable so "no such contract for this tenant" (null) is distinguishable
            // from the real, non-nullable default enum member (Msa) — SingleOrDefaultAsync over a
            // non-nullable projection cannot tell those two cases apart.
            var contractType = await dbContext.Contracts
                .AsNoTracking()
                .Where(c => c.TenantId == query.TenantId && c.Id == query.ContractId)
                .Select(c => (ContractDocumentType?)c.Type)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (contractType is { } type)
            {
                // AC-2: other validated contracts of the same type/category — "other" excludes
                // query.ContractId by construction (c.Id != query.ContractId), so the peer slice
                // can never silently duplicate the this-contract slice above.
                var peerContractIds = await dbContext.Contracts
                    .AsNoTracking()
                    .Where(c => c.TenantId == query.TenantId
                        && c.Id != query.ContractId
                        && c.Type == type
                        && dbContext.Documents.Any(d =>
                            d.TenantId == query.TenantId
                            && d.ContractId.HasValue
                            && d.ContractId.Value == c.Id
                            && d.ProcessingStatus == DocumentProcessingStatus.Completed))
                    .Select(c => c.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var peerDocumentIds = peerContractIds.Count == 0
                    ? []
                    : await dbContext.Documents
                        .AsNoTracking()
                        .Where(d => d.TenantId == query.TenantId && d.ContractId.HasValue && peerContractIds.Contains(d.ContractId.Value))
                        .Select(d => d.Id)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                if (peerDocumentIds.Count > 0)
                {
                    var peerMatches = await dbContext.Embeddings
                        .AsNoTracking()
                        .Where(e => e.TenantId == query.TenantId
                            && e.SourceType == DocumentSourceType
                            && peerDocumentIds.Contains(e.SourceId))
                        .Select(e => new { Embedding = e, Distance = e.Vector.CosineDistance(queryVector) })
                        .OrderBy(x => x.Distance)
                        .Take(query.PeerTopK)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    similarTypes = peerMatches.Select(x => ToSearchResult(x.Embedding, x.Distance)).ToList();
                }
            }
        }

        return Result<EmbeddingContractScopedSearchResult>.Success(
            new EmbeddingContractScopedSearchResult(
                thisContractMatches.Select(x => ToSearchResult(x.Embedding, x.Distance)).ToList(),
                similarTypes));
    }

    private static EmbeddingSearchResult ToSearchResult(Embedding embedding, double distance) =>
        new(
            embedding.Id,
            embedding.SourceType,
            embedding.SourceId,
            embedding.ChunkIndex,
            embedding.ChunkText,
            distance,
            embedding.Page,
            embedding.Section);
}

/// <summary>
/// Input to <see cref="EmbeddingRetrievalService.SearchByContractAsync"/> (task E28/F02/US01/T01,
/// NW-81; ADR-024 w19 cl. 15). Carries <see cref="ContractId"/> rather than a pre-resolved source-id
/// list — the service itself resolves which <see cref="Embedding.SourceId"/> values belong to this
/// contract (via <see cref="Domain.Document.ContractId"/>) and which belong to a validated peer of
/// the same <see cref="ContractDocumentType"/>, so no caller has to know the SourceType/SourceId
/// pointer shape <see cref="Domain.Embedding"/> itself documents.
/// </summary>
/// <param name="TenantId">The caller's already-authorized tenant (ADR-009).</param>
/// <param name="QueryText">The natural-language question to embed and search with.</param>
/// <param name="TopK">Max rows for the "this contract" slice.</param>
/// <param name="ContractId">The contract this turn is scoped to.</param>
/// <param name="PeerTopK">Max rows for the labelled "similar types" peer slice — deliberately lower
/// than <paramref name="TopK"/> (NW-81 "lower K"); <c>0</c> skips the peer slice entirely rather
/// than failing, for a caller that only wants this contract's own evidence.</param>
public sealed record EmbeddingSearchQuery(
    TenantId TenantId,
    string QueryText,
    int TopK,
    EntityId ContractId,
    int PeerTopK);

/// <summary>
/// Result of <see cref="EmbeddingRetrievalService.SearchByContractAsync"/>: two independently
/// nearest-first slices that a caller must keep labelled apart (task E28/F02/US01/T01, NW-81
/// AC-1/AC-2) — <see cref="SimilarTypes"/> is evidence about a <em>different</em>, merely
/// similar-type contract and must never be rendered or cited as if it were <see cref="ThisContract"/>'s
/// own fact.
/// </summary>
/// <param name="ThisContract">Nearest-first hits from the scoped contract's own embeddings only.</param>
/// <param name="SimilarTypes">Nearest-first hits from other validated contracts of the same
/// <see cref="ContractDocumentType"/> — empty when <see cref="EmbeddingSearchQuery.PeerTopK"/> was
/// <c>0</c>, the contract itself was not found for this tenant, or no validated peer exists.</param>
public sealed record EmbeddingContractScopedSearchResult(
    IReadOnlyList<EmbeddingSearchResult> ThisContract,
    IReadOnlyList<EmbeddingSearchResult> SimilarTypes);
