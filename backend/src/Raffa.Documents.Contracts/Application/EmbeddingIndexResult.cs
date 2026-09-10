using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// Result of indexing one chunk of text into the tenant's embedding store
/// (<see cref="EmbeddingRetrievalService.IndexChunkAsync"/>).
/// </summary>
/// <param name="EmbeddingId">Id of the persisted <see cref="Domain.Embedding"/> row.</param>
/// <param name="Model">Foundry embedding model id that produced the vector (ADR-004 `embed` role).</param>
/// <param name="CreatedAt">When the embedding was persisted.</param>
public sealed record EmbeddingIndexResult(EntityId EmbeddingId, string Model, DateTimeOffset CreatedAt);
