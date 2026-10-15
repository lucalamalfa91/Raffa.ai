using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure.Entities;
using Raffa.SharedKernel;

namespace Raffa.Market.Tests;

/// <summary>Shared stand-ins for the ingestion tests that need a feed, a gateway and a clock.</summary>
internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow => now;
}

/// <summary>Serves a caller-supplied deal list under one feed version.</summary>
internal sealed class StubFeed(string feedVersion, IReadOnlyList<MarketDeal> deals) : IMarketIntelligenceProvider
{
    public IReadOnlyList<MarketDeal> Deals { get; set; } = deals;

    public string FeedVersion { get; set; } = feedVersion;

    public Task<Result<MarketFeedSnapshot>> GetDealsAsync(
        string? requestedFeedVersion = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<MarketFeedSnapshot>.Success(new MarketFeedSnapshot(Deals, FeedVersion)));
}

/// <summary>
/// A deterministic gateway (never a live model) that counts single and batched embed calls. Its
/// <c>EmbedBatchAsync</c> is a real batch, so a test can tell the two paths apart.
/// </summary>
internal sealed class BatchCountingGateway : IAiGateway
{
    public int SingleCalls { get; private set; }

    public List<int> BatchSizes { get; } = [];

    public List<string> AllTexts { get; } = [];

    /// <summary>1-based number of the batch call that fails (0 = never).</summary>
    public int FailOnBatchNumber { get; set; }

    public Task<Result<AiClassificationResult>> ClassifyAsync(
        AiClassificationRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Result<AiExtractionResult>> ExtractAsync(
        AiExtractionRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Result<AiAnswerResult>> AnswerAsync(
        AiAnswerRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Result<AiOcrResult>> OcrAsync(
        AiOcrRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Result<AiEmbeddingResult>> EmbedAsync(
        AiEmbeddingRequest request, CancellationToken cancellationToken = default)
    {
        SingleCalls++;
        AllTexts.Add(request.Text);
        return Task.FromResult(Result<AiEmbeddingResult>.Success(
            new AiEmbeddingResult(VectorFor(request.Text), Metadata())));
    }

    public Task<Result<AiEmbeddingBatchResult>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        BatchSizes.Add(texts.Count);
        AllTexts.AddRange(texts);
        if (FailOnBatchNumber != 0 && BatchSizes.Count == FailOnBatchNumber)
        {
            return Task.FromResult(Result<AiEmbeddingBatchResult>.Failure("stub batch failure"));
        }

        return Task.FromResult(Result<AiEmbeddingBatchResult>.Success(
            new AiEmbeddingBatchResult(texts.Select(VectorFor).ToList(), Metadata())));
    }

    private static AiCallMetadata Metadata() =>
        new("stub-embed-model", "v1", "stub-v1", DateTimeOffset.UtcNow, "n/a");

    private static IReadOnlyList<float> VectorFor(string text)
    {
        var vector = new float[MarketEmbeddingEntity.VectorDimensions];
        var seed = text.Length + text.Sum(c => c);
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = ((seed + i) % 1000) / 1000f;
        }

        return vector;
    }
}
