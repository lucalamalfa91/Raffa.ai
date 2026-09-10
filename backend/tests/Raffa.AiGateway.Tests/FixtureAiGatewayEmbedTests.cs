using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.AiGateway.Tests.TestSupport;

namespace Raffa.AiGateway.Tests;

public class FixtureAiGatewayEmbedTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static FixtureAiGateway CreateGateway() =>
        new(new AiGatewayModelOptions(), new FixedClock(Now));

    [Fact]
    public async Task Embed_returns_a_vector_matching_AiGatewayConstants_EmbeddingDimensions()
    {
        var gateway = CreateGateway();

        var result = await gateway.EmbedAsync(new AiEmbeddingRequest("Limitation of liability clause."));

        Assert.True(result.IsSuccess);
        Assert.Equal(AiGatewayConstants.EmbeddingDimensions, result.Value.Vector.Count);
    }

    [Fact]
    public async Task Embed_is_deterministic_for_the_same_text()
    {
        var gateway = CreateGateway();

        var first = await gateway.EmbedAsync(new AiEmbeddingRequest("Clause text"));
        var second = await gateway.EmbedAsync(new AiEmbeddingRequest("Clause text"));

        Assert.Equal(first.Value.Vector, second.Value.Vector);
    }

    [Fact]
    public async Task Embed_differs_for_different_text()
    {
        var gateway = CreateGateway();

        var first = await gateway.EmbedAsync(new AiEmbeddingRequest("Clause A"));
        var second = await gateway.EmbedAsync(new AiEmbeddingRequest("Clause B"));

        Assert.NotEqual(first.Value.Vector, second.Value.Vector);
    }

    [Fact]
    public async Task Embed_fails_for_empty_text()
    {
        var gateway = CreateGateway();

        var result = await gateway.EmbedAsync(new AiEmbeddingRequest(""));

        Assert.True(result.IsFailure);
    }
}
