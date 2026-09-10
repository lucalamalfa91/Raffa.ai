using System.Net;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Foundry;
using Raffa.AiGateway.Tests.TestSupport;

namespace Raffa.AiGateway.Tests.Foundry;

/// <summary>
/// Proves the `classify` role over a fake HTTP handler: request shape (deployment name in the
/// body on the <c>openai/v1</c> route, JSON-schema structured output, no tools), the input prefix
/// cap, and response parsing.
/// </summary>
public class FoundryClassifyClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri FoundryBaseAddress = new("https://fake-foundry.example.com/");

    private static (FoundryClassifyClient Client, FakeHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> response,
        AiGatewayModelOptions? modelOptions = null,
        AiGatewayFoundryOptions? foundryOptions = null)
    {
        var handler = new FakeHttpMessageHandler(response);
        var httpClient = new HttpClient(handler) { BaseAddress = FoundryBaseAddress };
        foundryOptions ??= new AiGatewayFoundryOptions
        {
            Endpoint = FoundryBaseAddress.ToString(),
            ProjectName = "raffa-dev",
        };
        var tokenProvider = new FoundryTokenProvider(new FakeTokenCredential());
        var httpJsonClient = new FoundryHttpJsonClient(httpClient, tokenProvider, foundryOptions, TestRetryPolicies.NoDelay());
        var chatClient = new FoundryChatCompletionsClient(httpJsonClient, foundryOptions);
        var client = new FoundryClassifyClient(
            chatClient, modelOptions ?? new AiGatewayModelOptions(), foundryOptions, new FixedClock(Now));

        return (client, handler);
    }

    private static string ChatEnvelope(object payload)
    {
        var contentJson = JsonSerializer.Serialize(payload);
        var envelope = new
        {
            choices = new[] { new { message = new { role = "assistant", content = contentJson, refusal = (string?)null }, finish_reason = "stop" } },
            usage = new { prompt_tokens = 120, completion_tokens = 9, total_tokens = 129 },
        };
        return JsonSerializer.Serialize(envelope);
    }

    [Fact]
    public async Task Classify_sends_the_deployment_in_the_body_and_no_tools_and_parses_the_structured_response()
    {
        var (client, handler) = CreateClient(
            FakeHttpMessageHandler.Json(
                HttpStatusCode.OK, ChatEnvelope(new { documentType = "Msa", confidence = 0.93 })));

        var result = await client.ClassifyAsync(
            new AiClassificationRequest("This MASTER SERVICES AGREEMENT is entered into as of..."),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AiDocumentType.Msa, result.Value.DocumentType);
        Assert.Equal(0.93, result.Value.Confidence);
        Assert.Equal("gpt-4o-mini", result.Value.Metadata.ModelId);
        Assert.Equal(Now, result.Value.Metadata.RespondedAtUtc);
        Assert.Equal(new AiTokenUsage(120, 9), result.Value.Metadata.Usage);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("/openai/v1/chat/completions", request.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("fake-foundry-token", request.Headers.Authorization?.Parameter);
        Assert.Equal("raffa-dev", Assert.Single(request.Headers.GetValues("x-ms-foundry-project")));

        var body = Assert.Single(handler.RequestBodies)!;
        using var bodyJson = JsonDocument.Parse(body);
        Assert.Equal("gpt-4o-mini", bodyJson.RootElement.GetProperty("model").GetString());
        Assert.DoesNotContain("\"tools\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"tool_choice\"", body, StringComparison.Ordinal);
        Assert.Contains("\"response_format\"", body, StringComparison.Ordinal);
        Assert.Contains("\"json_schema\"", body, StringComparison.Ordinal);
        Assert.Equal(2048, bodyJson.RootElement.GetProperty("max_completion_tokens").GetInt32());
    }

    [Fact]
    public async Task Classify_uses_the_configured_deployment_for_a_custom_selection()
    {
        var options = new AiGatewayModelOptions { Classify = new AiModelSelection("custom-classify", "3") };
        var (client, handler) = CreateClient(
            FakeHttpMessageHandler.Json(
                HttpStatusCode.OK, ChatEnvelope(new { documentType = "OrderForm", confidence = 0.7 })),
            options);

        var result = await client.ClassifyAsync(new AiClassificationRequest("An order form."), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("custom-classify", result.Value.Metadata.ModelId);
        Assert.Equal("3", result.Value.Metadata.ModelVersion);
        using var bodyJson = JsonDocument.Parse(handler.RequestBodies[0]!);
        Assert.Equal("custom-classify", bodyJson.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Classify_reads_only_the_configured_prefix_of_a_long_document()
    {
        var foundryOptions = new AiGatewayFoundryOptions
        {
            Endpoint = FoundryBaseAddress.ToString(),
            ClassifyMaxInputChars = 100,
        };
        var (client, handler) = CreateClient(
            FakeHttpMessageHandler.Json(HttpStatusCode.OK, ChatEnvelope(new { documentType = "Msa", confidence = 0.9 })),
            foundryOptions: foundryOptions);
        var longText = new string('x', 100) + "TAIL-THAT-MUST-NOT-BE-SENT";

        var result = await client.ClassifyAsync(new AiClassificationRequest(longText), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("TAIL-THAT-MUST-NOT-BE-SENT", handler.RequestBodies[0]!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Classify_fails_without_document_text_and_never_calls_Foundry()
    {
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));

        var result = await client.ClassifyAsync(new AiClassificationRequest(""), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Classify_fails_when_the_model_names_a_type_outside_the_fixed_label_set()
    {
        var (client, _) = CreateClient(
            FakeHttpMessageHandler.Json(
                HttpStatusCode.OK, ChatEnvelope(new { documentType = "SomethingNotInTheEnum", confidence = 0.5 })));

        var result = await client.ClassifyAsync(new AiClassificationRequest("Some text."), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Classify_fails_when_Foundry_keeps_returning_a_server_error()
    {
        var (client, handler) = CreateClient(
            FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, """{"error":{"code":"InternalError","message":"boom"}}"""));

        var result = await client.ClassifyAsync(new AiClassificationRequest("Some text."), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("500", result.Error, StringComparison.Ordinal);
        Assert.StartsWith(AiGatewayErrors.UnavailablePrefix, result.Error, StringComparison.Ordinal);
        Assert.Equal(4, handler.Requests.Count); // 1 attempt + 3 retries
    }

    [Fact]
    public async Task Classify_fails_without_retrying_on_a_bad_request()
    {
        var (client, handler) = CreateClient(
            FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, """{"error":{"code":"invalid_json_schema","message":"schema rejected"}}"""));

        var result = await client.ClassifyAsync(new AiClassificationRequest("Some text."), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("invalid_json_schema", result.Error, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }
}
