using System.Text.Json.Serialization;

namespace Contigo.AiGateway.Foundry.Wire;

/// <summary>Azure OpenAI-compatible embeddings wire shapes (<see cref="FoundryEmbedClient"/>).</summary>
public sealed record EmbeddingRequest(
    [property: JsonPropertyName("input")] string Input);

public sealed record EmbeddingDatum(
    [property: JsonPropertyName("embedding")] IReadOnlyList<float>? Embedding);

public sealed record EmbeddingResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<EmbeddingDatum>? Data);
