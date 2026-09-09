using System.Net;
using System.Text.Json;
using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Contracts;
using Contigo.AiGateway.Foundry;
using Contigo.AiGateway.Tests.TestSupport;

namespace Contigo.AiGateway.Tests.Foundry;

/// <summary>
/// Proves the `embed` role: request shape (deployment and requested dimensions in the body on the
/// <c>openai/v1</c> route) and the ADR-004 fixed-dimension contract
/// (<see cref="AiGatewayConstants.EmbeddingDimensions"/>) is actually enforced against the
/// provider's own response, not just assumed.
/// </summary>
public class FoundryEmbedClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri FoundryBaseAddress = new("https://fake-foundry.example.com/");

    private static (FoundryEmbedClient Client, FakeHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> response,
        AiGatewayModelOptions? modelOptions = null,
        AiGatewayFoundryOptions? foundryOptions = null)
    {
        var handler = new FakeHttpMessageHandler(response);
        var httpClient = new HttpClient(handler) { BaseAddress = FoundryBaseAddress };
        foundryOptions ??= new AiGatewayFoundryOptions { Endpoint = FoundryBaseAddress.ToString() };
        var tokenProvider = new FoundryTokenProvider(new FakeTokenCredential());
        var httpJsonClient = new FoundryHttpJsonClient(httpClient, tokenProvider, foundryOptions, TestRetryPolicies.NoDelay());
        var client = new FoundryEmbedClient(
            httpJsonClient, modelOptions ?? new AiGatewayModelOptions(), foundryOptions, new FixedClock(Now));

        return (client, handler);
    }

    private static string EmbeddingEnvelope(IReadOnlyList<float> vector) =>
        JsonSerializer.Serialize(new
        {
            data = new[] { new { embedding = vector, index = 0 } },
            usage = new { prompt_tokens = 8, total_tokens = 8 },
        });

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
        Assert.Equal(new AiTokenUsage(8, 0), result.Value.Metadata.Usage);

        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/openai/v1/embeddings", request.RequestUri!.ToString(), StringComparison.Ordinal);
        using var bodyJson = JsonDocument.Parse(handler.RequestBodies[0]!);
        Assert.Equal("text-embedding-3-small", bodyJson.RootElement.GetProperty("model").GetString());
        Assert.Equal(AiGatewayConstants.EmbeddingDimensions, bodyJson.RootElement.GetProperty("dimensions").GetInt32());
        Assert.Equal("Limitation of liability clause.", bodyJson.RootElement.GetProperty("input").GetString());
    }

    [Fact]
    public async Task Embed_uses_the_date_versioned_route_when_an_api_version_is_configured()
    {
        var vector = new float[AiGatewayConstants.EmbeddingDimensions];
        var foundryOptions = new AiGatewayFoundryOptions
        {
            Endpoint = FoundryBaseAddress.ToString(),
            OpenAiApiVersion = "2024-10-21",
        };
        var (client, handler) = CreateClient(
            FakeHttpMessageHandler.Json(HttpStatusCode.OK, EmbeddingEnvelope(vector)), foundryOptions: foundryOptions);

        var result = await client.EmbedAsync(new AiEmbeddingRequest("some text"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(
            "/openai/deployments/text-embedding-3-small/embeddings?api-version=2024-10-21",
            handler.Requests[0].RequestUri!.ToString(),
            StringComparison.Ordinal);
        using var bodyJson = JsonDocument.Parse(handler.RequestBodies[0]!);
        Assert.False(bodyJson.RootElement.TryGetProperty("model", out _));
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
