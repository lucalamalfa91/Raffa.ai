using Contigo.SharedKernel;

namespace Contigo.Documents.Contracts.Application;

/// <summary>
/// One tenant-scoped nearest-neighbour hit returned by <see cref="EmbeddingRetrievalService.SearchAsync"/>.
/// </summary>
/// <param name="EmbeddingId">Id of the matched <see cref="Domain.Embedding"/> row.</param>
/// <param name="SourceType">The polymorphic source discriminator (e.g. "Document", "Clause") — see
/// <see cref="Domain.Embedding.SourceType"/>.</param>
/// <param name="SourceId">Id of the source row within <paramref name="SourceType"/>.</param>
/// <param name="ChunkIndex">Position of this chunk within its source.</param>
/// <param name="ChunkText">The literal embedded text, so a caller (e.g. Ask Contigo grounded Q&amp;A,
/// <c>IAiGateway.AnswerAsync</c>) can cite it without a second lookup.</param>
/// <param name="Distance">Cosine distance to the query vector — smaller is more similar (0 = identical
/// direction). Not a similarity score; callers wanting a 0-1 similarity can compute <c>1 - Distance</c>.</param>
/// <param name="Page">1-based page this chunk came from, or <see langword="null"/> when unknown
/// (task E13/F04/US01/T02, R-EVD-01: a citation prints "p.N" only when the page is really known).</param>
/// <param name="Section">Section label for this chunk when one is known, else <see langword="null"/>.</param>
public sealed record EmbeddingSearchResult(
    EntityId EmbeddingId,
    string SourceType,
    EntityId SourceId,
    int ChunkIndex,
    string ChunkText,
    double Distance,
    int? Page = null,
    string? Section = null);
