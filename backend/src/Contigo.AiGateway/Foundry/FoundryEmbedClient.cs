using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Contracts;
using Contigo.AiGateway.Foundry.Wire;
using Contigo.SharedKernel;

namespace Contigo.AiGateway.Foundry;

/// <summary>
/// `embed` role (ADR-004: fixed-dimension vectors for pgvector). Validates the returned vector is
/// exactly <see cref="AiGatewayConstants.EmbeddingDimensions"/> wide before returning success — a
/// provider/model mismatch must fail visibly here, not surface later as a pgvector column-width
/// error several layers away.
/// </summary>
public sealed class FoundryEmbedClient(
    FoundryHttpJsonClient httpJsonClient, AiGatewayModelOptions modelOptions, IClock clock)
{
    private const string ApiVersion = "2024-06-01";
    private const string PromptVersion = "foundry-embed-v1";

    public async Task<Result<AiEmbeddingResult>> EmbedAsync(
        AiEmbeddingRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return Result<AiEmbeddingResult>.Failure("Embedding requires non-empty text.");
        }

        var model = modelOptions.Embed;
        var relativeUrl =
            $"openai/deployments/{Uri.EscapeDataString(model.ModelId)}/embeddings?api-version={ApiVersion}";

        var result = await httpJsonClient
            .PostAsync<EmbeddingRequest, EmbeddingResponse>(
                relativeUrl, new EmbeddingRequest(request.Text), cancellationToken)
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
                "(AiGatewayConstants.EmbeddingDimensions / ADR-004 schema-fixed dimension).");
        }

        var metadata = FoundryCallMetadataFactory.Build(model, PromptVersion, clock, request.Text);

        return Result<AiEmbeddingResult>.Success(new AiEmbeddingResult(vector, metadata));
    }
}
