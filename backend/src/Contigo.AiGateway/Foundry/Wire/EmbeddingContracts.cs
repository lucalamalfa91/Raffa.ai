using System.Text.Json.Serialization;

namespace Contigo.AiGateway.Foundry.Wire;

/// <summary>Azure OpenAI embeddings wire shapes (<see cref="FoundryEmbedClient"/>).</summary>
/// <param name="Model">Deployment name — required on the <c>v1</c> route, omitted on the
/// date-versioned route where the deployment is in the URL.</param>
/// <param name="Dimensions">Requested vector width (text-embedding-3 models), omitted when unset.</param>
public sealed record EmbeddingRequest(
    [property: JsonPropertyName("input")] string Input,
    [property: JsonPropertyName("model")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Model = null,
    [property: JsonPropertyName("dimensions")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Dimensions = null);

public sealed record EmbeddingDatum(
    [property: JsonPropertyName("embedding")] IReadOnlyList<float>? Embedding);

public sealed record EmbeddingUsage(
    [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
    [property: JsonPropertyName("total_tokens")] int TotalTokens);

public sealed record EmbeddingResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<EmbeddingDatum>? Data,
    [property: JsonPropertyName("usage")] EmbeddingUsage? Usage = null);
