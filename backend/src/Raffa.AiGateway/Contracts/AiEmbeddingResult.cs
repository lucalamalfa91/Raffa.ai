namespace Raffa.AiGateway.Contracts;

/// <summary>
/// Output of the `embed` role. <see cref="Vector"/> is always
/// <see cref="AiGatewayConstants.EmbeddingDimensions"/> wide so it can be written directly into
/// the pgvector column ADR-003/ADR-004 fix at schema time.
/// </summary>
/// <param name="Vector">Dense embedding vector, length <see cref="AiGatewayConstants.EmbeddingDimensions"/>.</param>
/// <param name="Metadata">Reproducibility metadata for this call (ADR-004, ADR-011).</param>
public sealed record AiEmbeddingResult(
    IReadOnlyList<float> Vector,
    AiCallMetadata Metadata);
