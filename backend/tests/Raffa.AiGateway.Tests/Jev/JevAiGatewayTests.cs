using System.Net;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Jev;
using Raffa.AiGateway.Tests.TestSupport;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Tests.Jev;

/// <summary>
/// Proves the decorator's one job: `classify` goes to Jev, every other role passes straight
/// through to <c>inner</c> untouched (<see cref="JevAiGateway"/>'s own doc comment — this pilot's
/// scope boundary).
/// </summary>
public class JevAiGatewayTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private static AiCallMetadata InnerMetadata() => new("inner-model", "1", "inner-v1", Now, "hash");

    /// <summary>Records which role was called and returns a value tagged with
    /// <see cref="InnerMetadata"/>'s model id, distinguishable from anything
    /// <see cref="JevClassifyClient"/> could ever produce (it always reports
    /// <c>typesafe/jev-1.13</c>).</summary>
    private sealed class RecordingGateway : IAiGateway
    {
        public List<string> Calls { get; } = [];

        public Task<Result<AiClassificationResult>> ClassifyAsync(
            AiClassificationRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(ClassifyAsync));
            return Task.FromResult(Result<AiClassificationResult>.Success(
                new AiClassificationResult(AiDocumentType.Other, 0.42, InnerMetadata())));
        }

        public Task<Result<AiExtractionResult>> ExtractAsync(
            AiExtractionRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(ExtractAsync));
            return Task.FromResult(Result<AiExtractionResult>.Success(
                new AiExtractionResult("{}", InnerMetadata())));
        }

        public Task<Result<AiEmbeddingResult>> EmbedAsync(
            AiEmbeddingRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(EmbedAsync));
            return Task.FromResult(Result<AiEmbeddingResult>.Success(
                new AiEmbeddingResult(new[] { 0.1f }, InnerMetadata())));
        }

        public Task<Result<AiAnswerResult>> AnswerAsync(
            AiAnswerRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(AnswerAsync));
            return Task.FromResult(Result<AiAnswerResult>.Success(
                new AiAnswerResult(false, null, [], InnerMetadata())));
        }

        public Task<Result<AiOcrResult>> OcrAsync(
            AiOcrRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(OcrAsync));
            return Task.FromResult(Result<AiOcrResult>.Success(
                new AiOcrResult([], InnerMetadata())));
        }

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(
            AiAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(AnalyzeAsync));
            return Task.FromResult(Result<AiAnalysisResult>.Success(
                new AiAnalysisResult("{}", InnerMetadata())));
        }

        public Task<Result<AiResearchResult>> ResearchAsync(
            AiResearchRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(ResearchAsync));
            return Task.FromResult(Result<AiResearchResult>.Success(
                new AiResearchResult("inner", [], false, InnerMetadata())));
        }
    }

    private static (JevAiGateway Gateway, RecordingGateway Inner, FakeHttpMessageHandler JevHandler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> jevResponse)
    {
        var inner = new RecordingGateway();
        var handler = new FakeHttpMessageHandler(jevResponse);
        var httpClient = new HttpClient(handler);
        var options = new AiGatewayJevOptions { Enabled = true, ApiKey = "fake-openrouter-key" };
        var jevHttpClient = new JevHttpJsonClient(httpClient, options, TestRetryPolicies.NoDelay());
        var classifyClient = new JevClassifyClient(jevHttpClient, options, new FixedClock(Now));
        var gateway = new JevAiGateway(inner, classifyClient);

        return (gateway, inner, handler);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> JevClassifiesAsMsa() =>
        FakeHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"answers":{"documentType":{"choice":"Msa","confidence":0.77}}}""");

    [Fact]
    public async Task ClassifyAsync_goes_to_Jev_not_inner()
    {
        var (gateway, inner, handler) = Create(JevClassifiesAsMsa());

        var result = await gateway.ClassifyAsync(new AiClassificationRequest("MSA text"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AiDocumentType.Msa, result.Value.DocumentType);
        Assert.Equal(0.77, result.Value.Confidence);
        Assert.Equal("typesafe/jev-1.13", result.Value.Metadata.ModelId);
        Assert.Empty(inner.Calls);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ExtractAsync_passes_straight_through_to_inner()
    {
        var (gateway, inner, handler) = Create(JevClassifiesAsMsa());

        var result = await gateway.ExtractAsync(new AiExtractionRequest("Metadata", "text", "{}"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("inner-model", result.Value.Metadata.ModelId);
        Assert.Equal([nameof(IAiGateway.ExtractAsync)], inner.Calls);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task EmbedAsync_passes_straight_through_to_inner()
    {
        var (gateway, inner, handler) = Create(JevClassifiesAsMsa());

        var result = await gateway.EmbedAsync(new AiEmbeddingRequest("text"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("inner-model", result.Value.Metadata.ModelId);
        Assert.Equal([nameof(IAiGateway.EmbedAsync)], inner.Calls);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AnswerAsync_passes_straight_through_to_inner()
    {
        var (gateway, inner, handler) = Create(JevClassifiesAsMsa());

        var result = await gateway.AnswerAsync(new AiAnswerRequest("question", []), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("inner-model", result.Value.Metadata.ModelId);
        Assert.Equal([nameof(IAiGateway.AnswerAsync)], inner.Calls);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task OcrAsync_passes_straight_through_to_inner()
    {
        var (gateway, inner, handler) = Create(JevClassifiesAsMsa());

        var result = await gateway.OcrAsync(
            new AiOcrRequest("test.pdf", "application/pdf", new byte[] { 1 }), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("inner-model", result.Value.Metadata.ModelId);
        Assert.Equal([nameof(IAiGateway.OcrAsync)], inner.Calls);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AnalyzeAsync_passes_straight_through_to_inner()
    {
        var (gateway, inner, handler) = Create(JevClassifiesAsMsa());

        var result = await gateway.AnalyzeAsync(
            new AiAnalysisRequest("agent", "system prompt", "{}", "{}", "v1"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("inner-model", result.Value.Metadata.ModelId);
        Assert.Equal([nameof(IAiGateway.AnalyzeAsync)], inner.Calls);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ResearchAsync_passes_straight_through_to_inner()
    {
        var (gateway, inner, handler) = Create(JevClassifiesAsMsa());

        var result = await gateway.ResearchAsync(
            new AiResearchRequest("query", "market-practice", "en", 3, "system prompt", "v1"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("inner-model", result.Value.Metadata.ModelId);
        Assert.Equal([nameof(IAiGateway.ResearchAsync)], inner.Calls);
        Assert.Empty(handler.Requests);
    }
}
