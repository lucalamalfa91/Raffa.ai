using System.Net;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Foundry;
using Raffa.AiGateway.Tests.TestSupport;

namespace Raffa.AiGateway.Tests.Foundry;

/// <summary>T-05: the batched embeddings call (array <c>input</c>, one datum per text).</summary>
public class FoundryEmbedBatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri FoundryBaseAddress = new("https://fake-foundry.example.com/");

    private static (FoundryEmbedClient Client, FakeHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> response)
    {
        var handler = new FakeHttpMessageHandler(response);
        var httpClient = new HttpClient(handler) { BaseAddress = FoundryBaseAddress };
        var foundryOptions = new AiGatewayFoundryOptions { Endpoint = FoundryBaseAddress.ToString() };
        var tokenProvider = new FoundryTokenProvider(new FakeTokenCredential());
        var httpJsonClient = new FoundryHttpJsonClient(httpClient, tokenProvider, foundryOptions, TestRetryPolicies.NoDelay());
        return (new FoundryEmbedClient(httpJsonClient, new AiGatewayModelOptions(), foundryOptions, new FixedClock(Now)), handler);
    }

    private static float[] Vector(float seed) =>
        Enumerable.Range(0, AiGatewayConstants.EmbeddingDimensions).Select(i => seed + i / 10000f).ToArray();

    [Fact]
    public async Task Batch_sends_one_request_with_an_input_array_and_returns_vectors_in_input_order()
    {
        // The provider may answer out of order: each datum carries its index.
        var body = JsonSerializer.Serialize(new
        {
            data = new[]
            {
                new { embedding = Vector(2), index = 1 },
                new { embedding = Vector(1), index = 0 },
                new { embedding = Vector(3), index = 2 },
            },
            usage = new { prompt_tokens = 30, total_tokens = 30 },
        });
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, body));

        var result = await client.EmbedBatchAsync(["a", "b", "c"], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Vectors.Count);
        Assert.Equal(Vector(1)[0], result.Value.Vectors[0][0]);
        Assert.Equal(Vector(2)[0], result.Value.Vectors[1][0]);
        Assert.Equal(Vector(3)[0], result.Value.Vectors[2][0]);
        Assert.Equal(new AiTokenUsage(30, 0), result.Value.Metadata.Usage);

        Assert.Single(handler.Requests);
        using var json = JsonDocument.Parse(handler.RequestBodies[0]!);
        var input = json.RootElement.GetProperty("input");
        Assert.Equal(JsonValueKind.Array, input.ValueKind);
        Assert.Equal("a,b,c", string.Join(",", input.EnumerateArray().Select(e => e.GetString())));
    }

    [Fact]
    public async Task Batch_fails_when_the_provider_returns_fewer_vectors_than_inputs()
    {
        var body = JsonSerializer.Serialize(new { data = new[] { new { embedding = Vector(1), index = 0 } } });
        var (client, _) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, body));

        var result = await client.EmbedBatchAsync(["a", "b"], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("1 vectors for 2 inputs", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Batch_fails_on_a_wrong_dimension_and_on_blank_text()
    {
        var body = JsonSerializer.Serialize(new { data = new[] { new { embedding = new float[8], index = 0 } } });
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, body));

        var wrongDimension = await client.EmbedBatchAsync(["a"], CancellationToken.None);
        var blank = await client.EmbedBatchAsync(["a", " "], CancellationToken.None);

        Assert.True(wrongDimension.IsFailure);
        Assert.Contains("8-dimension", wrongDimension.Error, StringComparison.Ordinal);
        Assert.True(blank.IsFailure);
        Assert.Single(handler.Requests);
    }
}
