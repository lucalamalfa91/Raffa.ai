using System.Net;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Foundry;
using Raffa.AiGateway.Tests.TestSupport;

namespace Raffa.AiGateway.Tests.Foundry;

/// <summary>
/// Proves the one chat-completions call shape every chat role shares: the GA <c>openai/v1</c>
/// route with the deployment in the body, the configuration-driven GPT-5.x knobs that are omitted
/// when unset and clamped/validated when set, the date-versioned fallback route, and the explicit
/// failures for a truncated, filtered or refused completion.
/// </summary>
public class FoundryChatCompletionsClientTests
{
    private static readonly Uri FoundryBaseAddress = new("https://fake-foundry.example.com/");
    private static readonly JsonElement ProbeSchema = JsonDocument.Parse(
        """{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}""").RootElement;

    private static (FoundryChatCompletionsClient Client, FakeHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> response, AiGatewayFoundryOptions? foundryOptions = null)
    {
        var handler = new FakeHttpMessageHandler(response);
        var httpClient = new HttpClient(handler) { BaseAddress = FoundryBaseAddress };
        foundryOptions ??= new AiGatewayFoundryOptions { Endpoint = FoundryBaseAddress.ToString() };
        var tokenProvider = new FoundryTokenProvider(new FakeTokenCredential());
        var httpJsonClient = new FoundryHttpJsonClient(httpClient, tokenProvider, foundryOptions, TestRetryPolicies.NoDelay());
        return (new FoundryChatCompletionsClient(httpJsonClient, foundryOptions), handler);
    }

    private static string Envelope(string content, string finishReason = "stop", string? refusal = null) =>
        JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { role = "assistant", content, refusal }, finish_reason = finishReason } },
            usage = new { prompt_tokens = 10, completion_tokens = 5, total_tokens = 15 },
            model = "gpt-5.4-nano-2026-03-17",
        });

    private static Task<Raffa.SharedKernel.Result<ChatCompletionOutcome>> Complete(
        FoundryChatCompletionsClient client, AiModelSelection model) =>
        client.CompleteAsync("Probe", model, "system", "user", "probe", ProbeSchema, CancellationToken.None);

    [Fact]
    public async Task Sends_the_v1_route_with_the_deployment_in_the_body_and_omits_unset_knobs()
    {
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, Envelope("""{"ok":true}""")));

        var result = await Complete(client, new AiModelSelection("gpt-5.4-nano-dev", "2026-03-17") { MaxCompletionTokens = 64 });

        Assert.True(result.IsSuccess);
        Assert.Equal("""{"ok":true}""", result.Value.Content);
        Assert.Equal("stop", result.Value.FinishReason);
        Assert.Equal(10, result.Value.Usage!.PromptTokens);
        Assert.Equal(5, result.Value.Usage.CompletionTokens);

        Assert.EndsWith("/openai/v1/chat/completions", handler.Requests[0].RequestUri!.ToString(), StringComparison.Ordinal);
        using var body = JsonDocument.Parse(handler.RequestBodies[0]!);
        Assert.Equal(
            ["model", "messages", "response_format", "max_completion_tokens"],
            body.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("gpt-5.4-nano-dev", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(64, body.RootElement.GetProperty("max_completion_tokens").GetInt32());
    }

    [Fact]
    public async Task Sends_temperature_clamped_to_the_ceiling_and_a_normalised_reasoning_effort_when_configured()
    {
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, Envelope("""{"ok":true}""")));

        var result = await Complete(client, new AiModelSelection("dep", "1") { Temperature = 0.9, ReasoningEffort = " LOW " });

        Assert.True(result.IsSuccess);
        using var body = JsonDocument.Parse(handler.RequestBodies[0]!);
        Assert.Equal(FoundryChatCompletionsClient.MaxTemperature, body.RootElement.GetProperty("temperature").GetDouble());
        Assert.Equal("low", body.RootElement.GetProperty("reasoning_effort").GetString());
    }

    [Fact]
    public async Task Refuses_an_unknown_reasoning_effort_before_sending_anything()
    {
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, Envelope("""{"ok":true}""")));

        var result = await Complete(client, new AiModelSelection("dep", "1") { ReasoningEffort = "turbo" });

        Assert.True(result.IsFailure);
        Assert.Contains("ReasoningEffort", result.Error, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Uses_the_date_versioned_route_without_a_body_model_when_an_api_version_is_configured()
    {
        var options = new AiGatewayFoundryOptions { Endpoint = FoundryBaseAddress.ToString(), OpenAiApiVersion = "2024-10-21" };
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, Envelope("""{"ok":true}""")), options);

        var result = await Complete(client, new AiModelSelection("my-deployment", "1"));

        Assert.True(result.IsSuccess);
        Assert.Contains(
            "/openai/deployments/my-deployment/chat/completions?api-version=2024-10-21",
            handler.Requests[0].RequestUri!.ToString(),
            StringComparison.Ordinal);
        using var body = JsonDocument.Parse(handler.RequestBodies[0]!);
        Assert.False(body.RootElement.TryGetProperty("model", out _));
    }

    [Fact]
    public async Task Refuses_an_api_version_that_predates_structured_outputs()
    {
        var options = new AiGatewayFoundryOptions { Endpoint = FoundryBaseAddress.ToString(), OpenAiApiVersion = "2024-06-01" };
        var (client, _) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, Envelope("""{"ok":true}""")), options);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Complete(client, new AiModelSelection("dep", "1")));

        Assert.Contains("2024-06-01", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_completion_cut_at_the_token_cap_is_an_explicit_failure()
    {
        var (client, _) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, Envelope("""{"ok":tr""", finishReason: "length")));

        var result = await Complete(client, new AiModelSelection("dep", "1") { MaxCompletionTokens = 8 });

        Assert.True(result.IsFailure);
        Assert.Contains("max_completion_tokens", result.Error, StringComparison.Ordinal);
        Assert.Contains("8", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_and_a_content_filter_stop_are_explicit_failures()
    {
        var (refusing, _) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, Envelope("", refusal: "I cannot help with that.")));
        var (filtered, _) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, Envelope("", finishReason: "content_filter")));

        var refused = await Complete(refusing, new AiModelSelection("dep", "1"));
        var stopped = await Complete(filtered, new AiModelSelection("dep", "1"));

        Assert.True(refused.IsFailure);
        Assert.Contains("I cannot help with that.", refused.Error, StringComparison.Ordinal);
        Assert.True(stopped.IsFailure);
        Assert.Contains("content filter", stopped.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_content_filter_400_names_the_filter_in_the_failure()
    {
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(
            HttpStatusCode.BadRequest,
            """{"error":{"code":"content_filter","message":"The response was filtered due to the prompt triggering content management policy.","innererror":{"code":"ResponsibleAIPolicyViolation"}}}"""));

        var result = await Complete(client, new AiModelSelection("dep", "1"));

        Assert.True(result.IsFailure);
        Assert.Contains("content_filter (ResponsibleAIPolicyViolation)", result.Error, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }
}
