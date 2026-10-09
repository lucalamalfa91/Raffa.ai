using Raffa.AiGateway.Configuration;
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
public sealed class JevAiGateway(IAiGateway inner, JevClassifyClient classifyClient, AiGatewayJevOptions options) : IAiGateway
{
    /// <summary>
    /// Jev first, Foundry only when Jev cannot be trusted with this document: a failed call (network,
    /// 5xx/529 after the retries, an unreadable answer, a missing key) or an answer under
    /// <see cref="AiGatewayJevOptions.ClassifyMinConfidence"/>. This is the confidence-gated cascade
    /// TypeSafe's own cookbooks recommend (a cheap, fast decision that reports how sure it is, with a
    /// fallback for the cases it is not sure about) -- the admission gate never sees a Jev outage as
    /// a 503 that Foundry would have served, and a document Jev is unsure about is judged by the
    /// model the pilot is being compared with. <see cref="AiCallMetadata.ModelId"/> on the result
    /// says which of the two answered.
    /// </summary>
    public async Task<Result<AiClassificationResult>> ClassifyAsync(
        AiClassificationRequest request, CancellationToken cancellationToken = default)
    {
        var jev = await classifyClient.ClassifyAsync(request, cancellationToken).ConfigureAwait(false);
        if (jev.IsSuccess && jev.Value.Confidence >= options.ClassifyMinConfidence)
        {
            return jev;
        }

        return await inner.ClassifyAsync(request, cancellationToken).ConfigureAwait(false);
    }

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
