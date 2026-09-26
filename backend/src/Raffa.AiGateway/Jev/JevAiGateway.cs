using Raffa.AiGateway.Contracts;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Jev;

/// <summary>
/// Decorator that routes the `classify` role to Jev and forwards every other role unchanged to
/// <paramref name="inner"/> (the Foundry or Fixture gateway this pilot sits in front of) --
/// see <see cref="Configuration.AiGatewayJevOptions"/> for why this pilot is scoped to `classify`
/// only. Wrapped by <c>Logging.LoggingAiGateway</c> the same as any other <see cref="IAiGateway"/>
/// (<see cref="ServiceCollectionExtensions.AddAiGatewayModule"/>), so every Jev call is audited
/// exactly like a Foundry call -- the recorded <see cref="AiCallMetadata.ModelId"/> is the one
/// visible way to tell, after the fact, which calls this pilot actually touched.
/// </summary>
public sealed class JevAiGateway(IAiGateway inner, JevClassifyClient classifyClient) : IAiGateway
{
    public Task<Result<AiClassificationResult>> ClassifyAsync(
        AiClassificationRequest request, CancellationToken cancellationToken = default) =>
        classifyClient.ClassifyAsync(request, cancellationToken);

    public Task<Result<AiExtractionResult>> ExtractAsync(
        AiExtractionRequest request, CancellationToken cancellationToken = default) =>
        inner.ExtractAsync(request, cancellationToken);

    public Task<Result<AiEmbeddingResult>> EmbedAsync(
        AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
        inner.EmbedAsync(request, cancellationToken);

    public Task<Result<AiAnswerResult>> AnswerAsync(
        AiAnswerRequest request, CancellationToken cancellationToken = default) =>
        inner.AnswerAsync(request, cancellationToken);

    public Task<Result<AiOcrResult>> OcrAsync(
        AiOcrRequest request, CancellationToken cancellationToken = default) =>
        inner.OcrAsync(request, cancellationToken);

    public Task<Result<AiAnalysisResult>> AnalyzeAsync(
        AiAnalysisRequest request, CancellationToken cancellationToken = default) =>
        inner.AnalyzeAsync(request, cancellationToken);

    public Task<Result<AiResearchResult>> ResearchAsync(
        AiResearchRequest request, CancellationToken cancellationToken = default) =>
        inner.ResearchAsync(request, cancellationToken);
}
