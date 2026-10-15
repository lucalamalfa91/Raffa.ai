namespace Raffa.AiGateway.Contracts;

/// <summary>
/// The result of <see cref="IAiGateway.EmbedBatchAsync"/> (T-05): one vector per input text, in the
/// input order, and the metadata of the one call that produced them (model, prompt version, a hash
/// over the whole batch input, and the batch's token usage when the provider reports it).
/// </summary>
/// <param name="Vectors">One vector per input text, same order as the input.</param>
/// <param name="Metadata">The call metadata, one per batch.</param>
public sealed record AiEmbeddingBatchResult(
    IReadOnlyList<IReadOnlyList<float>> Vectors,
    AiCallMetadata Metadata);
