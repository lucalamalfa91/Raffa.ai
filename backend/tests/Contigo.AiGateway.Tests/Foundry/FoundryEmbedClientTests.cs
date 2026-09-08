using System.Net;
using System.Text.Json;
using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Contracts;
using Contigo.AiGateway.Foundry;
using Contigo.AiGateway.Tests.TestSupport;

namespace Contigo.AiGateway.Tests.Foundry;

/// <summary>
/// Proves task E13/F01/US01/T02's `embed` role: request shape (deployment id in the URL) and the
/// ADR-004 fixed-dimension contract (<see cref="AiGatewayConstants.EmbeddingDimensions"/>) is
/// actually enforced against the provider's own response, not just assumed.
/// </summary>
public class FoundryEmbedClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri FoundryBaseAddress = new("https://fake-foundry.example.com/");

    private static (FoundryEmbedClient Client, FakeHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> response)
    {
        var handler = new FakeHttpMessageHandler(response);
        var httpClient = new HttpClient(handler) { BaseAddress = FoundryBaseAddress };
        var foundryOptions = new AiGatewayFoundryOptions { Endpoint = FoundryBaseAddress.ToString() };
        var tokenProvider = new FoundryTokenProvider(new FakeTokenCredential());
        var httpJsonClient = new FoundryHttpJsonClient(httpClient, tokenProvider, foundryOptions);
        var client = new FoundryEmbedClient(httpJsonClient, new AiGatewayModelOptions(), new FixedClock(Now));

        return (client, handler);
    }

    private static string EmbeddingEnvelope(IReadOnlyList<float> vector) =>
        JsonSerializer.Serialize(new { data = new[] { new { embedding = vector } } });

    [Fact]
    public async Task Embed_returns_a_1536_dimension_vector_from_the_configured_deployment()
    {
        var vector = Enumerable.Range(0, AiGatewayConstants.EmbeddingDimensions).Select(i => i / 1536f).ToArray();
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, EmbeddingEnvelope(vector)));

        var result = await client.EmbedAsync(new AiEmbeddingRequest("Limitation of liability clause."), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AiGatewayConstants.EmbeddingDimensions, result.Value.Vector.Count);
        Assert.Equal(vector[0], result.Value.Vector[0]);
        Assert.Equal("text-embedding-3-small", result.Value.Metadata.ModelId);

        var request = Assert.Single(handler.Requests);
        Assert.Contains("/openai/deployments/text-embedding-3-small/embeddings", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Embed_fails_when_the_provider_returns_the_wrong_dimension()
    {
        var wrongSizeVector = new float[] { 0.1f, 0.2f, 0.3f };
        var (client, _) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, EmbeddingEnvelope(wrongSizeVector)));

        var result = await client.EmbedAsync(new AiEmbeddingRequest("some text"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("3-dimension", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Embed_fails_without_text_and_never_calls_Foundry()
    {
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));

        var result = await client.EmbedAsync(new AiEmbeddingRequest(""), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Embed_fails_when_Foundry_returns_no_data()
    {
        var (client, _) = CreateClient(
            FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { data = Array.Empty<object>() })));

        var result = await client.EmbedAsync(new AiEmbeddingRequest("some text"), CancellationToken.None);

        Assert.True(result.IsFailure);
    }
}
