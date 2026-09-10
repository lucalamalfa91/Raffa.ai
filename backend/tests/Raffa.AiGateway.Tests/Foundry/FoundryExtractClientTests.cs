using System.Net;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Foundry;
using Raffa.AiGateway.Tests.TestSupport;

namespace Raffa.AiGateway.Tests.Foundry;

/// <summary>
/// Proves the `extract` role: the caller's own JSON schema is forwarded verbatim as the
/// structured-output schema once it passes the strict-mode check, and the model's response JSON
/// comes back unparsed — "the gateway does not know or validate the domain schema"
/// (<see cref="IAiGateway.ExtractAsync"/>'s own doc comment) beyond what Azure itself would refuse.
/// </summary>
public class FoundryExtractClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri FoundryBaseAddress = new("https://fake-foundry.example.com/");

    /// <summary>A strict-mode-compliant schema: every object closed, every property required.</summary>
    private const string Schema =
        """
        {"type":"object","properties":{"facts":{"type":"array","items":{"type":"object","properties":{"field":{"type":"string"},"value":{"type":["string","null"]},"confidence":{"type":"number"}},"required":["field","value","confidence"],"additionalProperties":false}}},"required":["facts"],"additionalProperties":false}
        """;

    private static (FoundryExtractClient Client, FakeHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> response)
    {
        var handler = new FakeHttpMessageHandler(response);
        var httpClient = new HttpClient(handler) { BaseAddress = FoundryBaseAddress };
        var foundryOptions = new AiGatewayFoundryOptions { Endpoint = FoundryBaseAddress.ToString() };
        var tokenProvider = new FoundryTokenProvider(new FakeTokenCredential());
        var httpJsonClient = new FoundryHttpJsonClient(httpClient, tokenProvider, foundryOptions, TestRetryPolicies.NoDelay());
        var chatClient = new FoundryChatCompletionsClient(httpJsonClient, foundryOptions);
        var client = new FoundryExtractClient(chatClient, new AiGatewayModelOptions(), new FixedClock(Now));

        return (client, handler);
    }

    private static string ChatEnvelope(string rawContentJson)
    {
        var envelope = new
        {
            choices = new[] { new { message = new { role = "assistant", content = rawContentJson }, finish_reason = "stop" } },
            usage = new { prompt_tokens = 3000, completion_tokens = 150, total_tokens = 3150 },
        };
        return JsonSerializer.Serialize(envelope);
    }

    [Fact]
    public async Task Extract_forwards_the_callers_schema_and_returns_the_models_raw_json_unparsed()
    {
        const string modelPayload = """{"facts":[{"field":"AutoRenewal","value":"true","confidence":0.9}]}""";
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, ChatEnvelope(modelPayload)));

        var result = await client.ExtractAsync(
            new AiExtractionRequest("commercial-terms", "Auto-renewal for 12 months.", Schema),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(modelPayload, result.Value.PayloadJson);
        Assert.Equal("gpt-4o-mini", result.Value.Metadata.ModelId);
        Assert.Equal(new AiTokenUsage(3000, 150), result.Value.Metadata.Usage);

        var body = Assert.Single(handler.RequestBodies)!;
        Assert.DoesNotContain("\"tools\"", body, StringComparison.Ordinal);
        Assert.Contains("\"facts\"", body, StringComparison.Ordinal); // the caller's own schema, embedded verbatim
        using var bodyJson = JsonDocument.Parse(body);
        Assert.True(bodyJson.RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("strict").GetBoolean());
        Assert.Equal(16384, bodyJson.RootElement.GetProperty("max_completion_tokens").GetInt32());
    }

    [Fact]
    public async Task Extract_fails_without_document_text_and_never_calls_Foundry()
    {
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));

        var result = await client.ExtractAsync(new AiExtractionRequest("stage", "", Schema), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Extract_fails_without_a_schema_and_never_calls_Foundry()
    {
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));

        var result = await client.ExtractAsync(new AiExtractionRequest("stage", "some text", ""), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Extract_fails_when_the_callers_schema_is_not_valid_json_and_never_calls_Foundry()
    {
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));

        var result = await client.ExtractAsync(
            new AiExtractionRequest("stage", "some text", "{not valid json"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Extract_refuses_a_schema_azure_strict_mode_would_reject_and_names_the_path()
    {
        const string looseSchema =
            """{"type":"object","properties":{"facts":{"type":"array","items":{"type":"object","properties":{"confidence":{"type":"number","minimum":0}},"required":[]}}},"required":["facts"]}""";
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));

        var result = await client.ExtractAsync(
            new AiExtractionRequest("metadata", "some text", looseSchema), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("additionalProperties", result.Error, StringComparison.Ordinal);
        Assert.Contains("minimum", result.Error, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Extract_sanitizes_the_stage_name_into_the_schema_name()
    {
        const string modelPayload = """{"items":[]}""";
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, ChatEnvelope(modelPayload)));

        var result = await client.ExtractAsync(
            new AiExtractionRequest("dates and renewal terms!", "text", Schema), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var body = Assert.Single(handler.RequestBodies)!;
        Assert.Contains("raffa_extraction_dates_and_renewal_terms_", body, StringComparison.Ordinal);
    }
}
