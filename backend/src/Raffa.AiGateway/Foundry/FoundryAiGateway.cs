using Raffa.AiGateway.Contracts;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Foundry;

/// <summary>
/// Live, Foundry-backed <see cref="IAiGateway"/> (ADR-004 amendment; ADR-024; task
/// E13/F01/US01/T02, foundry-gateway). Registered by <see cref="ServiceCollectionExtensions
/// .AddAiGatewayModule"/> when <see cref="Configuration.AiGatewayFoundryOptions.Endpoint"/> is
/// set, always wrapped by <see cref="Logging.LoggingAiGateway"/> — this type never writes an
/// audit entry itself, the same "logging is cross-cutting, not baked into the inner gateway" split
/// <see cref="Fixtures.FixtureAiGateway"/>'s own doc comment already establishes for the fixture.
///
/// A thin façade over five independently unit-testable per-role clients
/// (<see cref="FoundryClassifyClient"/>, <see cref="FoundryExtractClient"/>,
/// <see cref="FoundryEmbedClient"/>, <see cref="FoundryAnswerClient"/>,
/// <see cref="FoundryOcrClient"/>) rather than one large class with five methods, so a compliance
/// assertion (for example "the answer request carries no tools key",
/// <c>Raffa.AiGateway.Tests.Foundry.FoundryAnswerClientTests</c>) stays scoped to the one client
/// that builds that request.
/// </summary>
public sealed class FoundryAiGateway(
    FoundryClassifyClient classifyClient,
    FoundryExtractClient extractClient,
    FoundryEmbedClient embedClient,
    FoundryAnswerClient answerClient,
    FoundryOcrClient ocrClient) : IAiGateway
{
    /// <inheritdoc/>
    public Task<Result<AiClassificationResult>> ClassifyAsync(
        AiClassificationRequest request, CancellationToken cancellationToken = default) =>
        classifyClient.ClassifyAsync(request, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<AiExtractionResult>> ExtractAsync(
        AiExtractionRequest request, CancellationToken cancellationToken = default) =>
        extractClient.ExtractAsync(request, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<AiEmbeddingResult>> EmbedAsync(
        AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
        embedClient.EmbedAsync(request, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<AiAnswerResult>> AnswerAsync(
        AiAnswerRequest request, CancellationToken cancellationToken = default) =>
        answerClient.AnswerAsync(request, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<AiOcrResult>> OcrAsync(
        AiOcrRequest request, CancellationToken cancellationToken = default) =>
        ocrClient.OcrAsync(request, cancellationToken);
}
