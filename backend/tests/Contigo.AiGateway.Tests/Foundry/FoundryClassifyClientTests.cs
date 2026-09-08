using System.Net;
using System.Text.Json;
using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Contracts;
using Contigo.AiGateway.Foundry;
using Contigo.AiGateway.Tests.TestSupport;

namespace Contigo.AiGateway.Tests.Foundry;

/// <summary>
/// Proves task E13/F01/US01/T02's `classify` role over a fake HTTP handler: request shape
/// (deployment id in the URL, JSON-schema structured output, no tools), and response parsing.
/// </summary>
public class FoundryClassifyClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri FoundryBaseAddress = new("https://fake-foundry.example.com/");

    private static (FoundryClassifyClient Client, FakeHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> response, AiGatewayModelOptions? modelOptions = null)
    {
        var handler = new FakeHttpMessageHandler(response);
        var httpClient = new HttpClient(handler) { BaseAddress = FoundryBaseAddress };
        var foundryOptions = new AiGatewayFoundryOptions
        {
            Endpoint = FoundryBaseAddress.ToString(),
            ProjectName = "contigo-dev",
        };
        var tokenProvider = new FoundryTokenProvider(new FakeTokenCredential());
        var httpJsonClient = new FoundryHttpJsonClient(httpClient, tokenProvider, foundryOptions);
        var chatClient = new FoundryChatCompletionsClient(httpJsonClient);
        var client = new FoundryClassifyClient(chatClient, modelOptions ?? new AiGatewayModelOptions(), new FixedClock(Now));

        return (client, handler);
    }

    private static string ChatEnvelope(object payload)
    {
        var contentJson = JsonSerializer.Serialize(payload);
        var envelope = new { choices = new[] { new { message = new { role = "assistant", content = contentJson } } } };
        return JsonSerializer.Serialize(envelope);
    }

    [Fact]
    public async Task Classify_sends_the_deployment_id_and_no_tools_and_parses_the_structured_response()
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

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/openai/deployments/gpt-4o-mini/chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("fake-foundry-token", request.Headers.Authorization?.Parameter);
        Assert.Equal("contigo-dev", Assert.Single(request.Headers.GetValues("x-ms-foundry-project")));

        var body = Assert.Single(handler.RequestBodies)!;
        Assert.DoesNotContain("\"tools\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"tool_choice\"", body, StringComparison.Ordinal);
        Assert.Contains("\"response_format\"", body, StringComparison.Ordinal);
        Assert.Contains("\"json_schema\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Classify_uses_the_configured_model_id_for_a_custom_selection()
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
        Assert.Contains("/openai/deployments/custom-classify/chat/completions", handler.Requests[0].RequestUri!.ToString());
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
    public async Task Classify_fails_when_Foundry_returns_a_non_success_status()
    {
        var (client, _) = CreateClient(
            FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, """{"error":"boom"}"""));

        var result = await client.ClassifyAsync(new AiClassificationRequest("Some text."), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("500", result.Error, StringComparison.Ordinal);
    }
}
