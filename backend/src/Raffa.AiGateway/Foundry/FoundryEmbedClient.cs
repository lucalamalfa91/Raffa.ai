using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Foundry.Wire;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Foundry;

/// <summary>
/// `embed` role (ADR-004: fixed-dimension vectors for pgvector). Requests the configured
/// <see cref="AiModelSelection.Dimensions"/> (so demo's text-embedding-3-large is reduced to the
/// column width on the wire) and still validates the returned vector is exactly
/// <see cref="AiGatewayConstants.EmbeddingDimensions"/> wide before returning success — a
/// provider/model mismatch must fail visibly here, not surface later as a pgvector column-width
/// error several layers away.
/// </summary>
public sealed class FoundryEmbedClient(
    FoundryHttpJsonClient httpJsonClient,
    AiGatewayModelOptions modelOptions,
    AiGatewayFoundryOptions foundryOptions,
    IClock clock)
{
    private const string PromptVersion = "foundry-embed-v2";

    public async Task<Result<AiEmbeddingResult>> EmbedAsync(
        AiEmbeddingRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return Result<AiEmbeddingResult>.Failure("Embedding requires non-empty text.");
        }

        var model = modelOptions.Embed;
        var route = FoundryOpenAiRoutes.Embeddings(foundryOptions, model.ModelId);

        var result = await httpJsonClient
            .PostAsync<EmbeddingRequest, EmbeddingResponse>(
                route.RelativeUrl,
                new EmbeddingRequest(request.Text, route.ModelInBody ? model.ModelId : null, model.Dimensions),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<AiEmbeddingResult>.Failure(result.Error);
        }

        var vector = result.Value.Data is { Count: > 0 } data ? data[0].Embedding : null;
        if (vector is null)
        {
            return Result<AiEmbeddingResult>.Failure("Foundry embeddings response returned no vector.");
        }

        if (vector.Count != AiGatewayConstants.EmbeddingDimensions)
        {
            return Result<AiEmbeddingResult>.Failure(
                $"Foundry embeddings response returned a {vector.Count}-dimension vector; " +
                $"expected {AiGatewayConstants.EmbeddingDimensions} " +
                "(AiGatewayConstants.EmbeddingDimensions / ADR-004 schema-fixed dimension) — " +
                "set AiGateway:Models:Embed:Dimensions for a wider model.");
        }

        var usage = result.Value.Usage is { } u ? new AiTokenUsage(u.PromptTokens, 0) : null;
        var metadata = FoundryCallMetadataFactory.Build(model, PromptVersion, clock, request.Text, usage);

        return Result<AiEmbeddingResult>.Success(new AiEmbeddingResult(vector, metadata));
    }

    /// <summary>
    /// T-05: one embeddings call for the whole array. The provider returns one datum per input
    /// (with an <c>index</c>); the vectors are put back in input order and every one is checked for
    /// the schema-fixed dimension, exactly like the single call. One metadata record per batch (the
    /// input hash covers all texts, in order).
    /// </summary>
    public async Task<Result<AiEmbeddingBatchResult>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (texts.Count == 0 || texts.Any(string.IsNullOrWhiteSpace))
        {
            return Result<AiEmbeddingBatchResult>.Failure("Embedding requires non-empty text.");
        }

        var model = modelOptions.Embed;
        var route = FoundryOpenAiRoutes.Embeddings(foundryOptions, model.ModelId);

        var result = await httpJsonClient
            .PostAsync<EmbeddingBatchRequest, EmbeddingResponse>(
                route.RelativeUrl,
                new EmbeddingBatchRequest(texts, route.ModelInBody ? model.ModelId : null, model.Dimensions),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<AiEmbeddingBatchResult>.Failure(result.Error);
        }

        var data = result.Value.Data;
        if (data is null || data.Count != texts.Count)
        {
            return Result<AiEmbeddingBatchResult>.Failure(
                $"Foundry embeddings response returned {data?.Count ?? 0} vectors for {texts.Count} inputs.");
        }

        var ordered = data.Select((datum, position) => (Position: datum.Index ?? position, datum.Embedding))
            .OrderBy(x => x.Position)
            .ToList();

        var vectors = new List<IReadOnlyList<float>>(texts.Count);
        foreach (var (_, vector) in ordered)
        {
            if (vector is null || vector.Count != AiGatewayConstants.EmbeddingDimensions)
            {
                return Result<AiEmbeddingBatchResult>.Failure(
                    $"Foundry embeddings response returned a {vector?.Count ?? 0}-dimension vector; " +
                    $"expected {AiGatewayConstants.EmbeddingDimensions} " +
                    "(AiGatewayConstants.EmbeddingDimensions / ADR-004 schema-fixed dimension) - " +
                    "set AiGateway:Models:Embed:Dimensions for a wider model.");
            }

            vectors.Add(vector);
        }

        var usage = result.Value.Usage is { } u ? new AiTokenUsage(u.PromptTokens, 0) : null;
        var metadata = FoundryCallMetadataFactory.Build(
            model, PromptVersion, clock, string.Join('\n', texts), usage);

        return Result<AiEmbeddingBatchResult>.Success(new AiEmbeddingBatchResult(vectors, metadata));
    }
}
