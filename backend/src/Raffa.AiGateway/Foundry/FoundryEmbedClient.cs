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
}
