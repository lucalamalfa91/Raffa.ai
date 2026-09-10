using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.SharedKernel;

namespace Raffa.IntegrationTests;

/// <summary>
/// Test-only <see cref="IAiGateway"/> for <see cref="R1IntegrationFixture"/> — task
/// E02/F06/US01/T01 (r1-integration) needs a gateway where every one of the five roles behaves
/// meaningfully, not just the one <c>Raffa.Documents.Contracts.Tests.StagedExtractionServiceTests
/// .ScriptedAiGateway</c> already scripts (<c>ExtractAsync</c>, since
/// <c>Raffa.AiGateway.Fixtures.FixtureAiGateway.ExtractAsync</c> always returns an empty <c>{}</c>
/// placeholder — see that method's own doc comment). <see cref="ClassifyAsync"/> (keyword
/// classification), <see cref="EmbedAsync"/>/<see cref="AnswerAsync"/> (deterministic RAG) and
/// <see cref="OcrAsync"/> (fixture page split, PDFs through <c>FixturePdfTextScanner</c>) are all already meaningful on the real
/// <c>FixtureAiGateway</c> — this type delegates those four straight through to
/// <paramref name="inner"/> (composition, the same shape
/// <c>Raffa.AiGateway.Logging.LoggingAiGateway</c> already uses to wrap a real gateway) and only
/// overrides <see cref="ExtractAsync"/> with a scripted, per-<see cref="AiExtractionRequest.StageName"/>
/// payload so <c>Raffa.Documents.Contracts.Application.Extraction.StagedExtractionService</c> has
/// real facts to persist end-to-end (parent story us-01-final-integration AC-1/AC-2).
///
/// <see cref="OcrCallCount"/> is direct, unambiguous proof for AC-4 ("at least one scanned or
/// image-based contract extracts via Document Intelligence, full document") that the `ocr` role
/// really ran for a fixture whose mime type
/// <c>Raffa.Documents.Contracts.Application.Extraction.NativeDocumentTextExtractor.CanHandle</c>
/// returns <see langword="false"/> for — stronger than inferring it from the mime type alone.
/// </summary>
public sealed class ScriptedR1AiGateway(
    IAiGateway inner, IReadOnlyDictionary<string, string> extractPayloadByStage) : IAiGateway
{
    private int _ocrCallCount;

    /// <summary>How many times <see cref="OcrAsync"/> has been called so far, across every tenant
    /// and document this fixture's host has processed (this gateway is registered as a singleton —
    /// see <see cref="R1IntegrationFixture"/> — so the count is call-scoped to the whole test
    /// class, not per-request).</summary>
    public int OcrCallCount => _ocrCallCount;

    /// <inheritdoc/>
    public Task<Result<AiClassificationResult>> ClassifyAsync(
        AiClassificationRequest request, CancellationToken cancellationToken = default) =>
        inner.ClassifyAsync(request, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<AiExtractionResult>> ExtractAsync(
        AiExtractionRequest request, CancellationToken cancellationToken = default)
    {
        var payloadJson = extractPayloadByStage.TryGetValue(request.StageName, out var payload) ? payload : "{}";
        var metadata = new AiCallMetadata(
            "test-r1-extract-model", "1", "test-r1-prompt-v1", DateTimeOffset.UtcNow, "test-r1-input-hash");

        return Task.FromResult(Result<AiExtractionResult>.Success(new AiExtractionResult(payloadJson, metadata)));
    }

    /// <inheritdoc/>
    public Task<Result<AiEmbeddingResult>> EmbedAsync(
        AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
        inner.EmbedAsync(request, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<AiAnswerResult>> AnswerAsync(
        AiAnswerRequest request, CancellationToken cancellationToken = default) =>
        inner.AnswerAsync(request, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<AiOcrResult>> OcrAsync(
        AiOcrRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _ocrCallCount);
        return inner.OcrAsync(request, cancellationToken);
    }
}
