using System.Net;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Jev;
using Raffa.AiGateway.Tests.TestSupport;

namespace Raffa.AiGateway.Tests.Jev;

/// <summary>
/// Proves the Jev pilot's `classify` role over a fake HTTP handler: request shape (one "choice"
/// question over the fixed <see cref="AiDocumentType"/> taxonomy), both attested response shapes
/// (<see cref="JevAnswerReader"/>'s own doc comment), and failure handling. Mirrors
/// <c>Foundry.FoundryClassifyClientTests</c>'s own structure so the two are easy to compare.
/// </summary>
public class JevClassifyClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private static (JevClassifyClient Client, FakeHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> response, AiGatewayJevOptions? options = null)
    {
        var handler = new FakeHttpMessageHandler(response);
        var httpClient = new HttpClient(handler);
        options ??= new AiGatewayJevOptions { Enabled = true, ApiKey = "fake-openrouter-key" };
        var jevHttpClient = new JevHttpJsonClient(httpClient, options, TestRetryPolicies.NoDelay());
        var client = new JevClassifyClient(jevHttpClient, options, new FixedClock(Now));

        return (client, handler);
    }

    private static string AnswerEnvelope(string questionKey, string choice, double confidence, bool nested) =>
        nested
            ? JsonSerializer.Serialize(new
            {
                answers = new Dictionary<string, object>
                {
                    [questionKey] = new { route = new { choice, confidence } },
                },
            })
            : JsonSerializer.Serialize(new
            {
                answers = new Dictionary<string, object>
                {
                    [questionKey] = new { choice, confidence },
                },
            });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Classify_parses_both_attested_response_shapes(bool nested)
    {
        var (client, handler) = CreateClient(
            FakeHttpMessageHandler.Json(
                HttpStatusCode.OK, AnswerEnvelope("documentType", "Msa", 0.87, nested)));

        var result = await client.ClassifyAsync(
            new AiClassificationRequest("This MASTER SERVICES AGREEMENT is entered into as of..."),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AiDocumentType.Msa, result.Value.DocumentType);
        Assert.Equal(0.87, result.Value.Confidence);
        Assert.Equal("typesafe/jev-1.13", result.Value.Metadata.ModelId);
        Assert.Equal(Now, result.Value.Metadata.RespondedAtUtc);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("fake-openrouter-key", request.Headers.Authorization?.Parameter);

        var body = Assert.Single(handler.RequestBodies)!;
        using var bodyJson = JsonDocument.Parse(body);
        Assert.Equal("typesafe/jev-1.13", bodyJson.RootElement.GetProperty("model").GetString());
        var question = bodyJson.RootElement.GetProperty("questions").GetProperty("documentType");
        Assert.Equal("choice", question.GetProperty("type").GetString());
        Assert.True(question.GetProperty("criteria").TryGetProperty("Msa", out _));
        Assert.True(question.GetProperty("criteria").TryGetProperty("Nda", out _));
    }

    [Fact]
    public async Task Classify_fails_without_document_text_and_never_calls_Jev()
    {
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));

        var result = await client.ClassifyAsync(new AiClassificationRequest(""), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Classify_fails_without_an_api_key_and_never_calls_Jev()
    {
        var options = new AiGatewayJevOptions { Enabled = true, ApiKey = null };
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"), options);

        var result = await client.ClassifyAsync(new AiClassificationRequest("Some text."), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("ApiKey", result.Error, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Classify_fails_when_the_model_names_a_type_outside_the_fixed_label_set()
    {
        var (client, _) = CreateClient(
            FakeHttpMessageHandler.Json(
                HttpStatusCode.OK, AnswerEnvelope("documentType", "SomethingNotInTheEnum", 0.5, nested: false)));

        var result = await client.ClassifyAsync(new AiClassificationRequest("Some text."), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Classify_fails_when_the_answer_carries_neither_attested_shape()
    {
        var (client, _) = CreateClient(
            FakeHttpMessageHandler.Json(
                HttpStatusCode.OK,
                JsonSerializer.Serialize(new { answers = new Dictionary<string, object> { ["documentType"] = new { somethingElse = true } } })));

        var result = await client.ClassifyAsync(new AiClassificationRequest("Some text."), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("unverified", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Classify_fails_when_Jev_keeps_returning_a_server_error()
    {
        var (client, handler) = CreateClient(
            FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, """{"error":{"code":"internal_error","message":"boom"}}"""));

        var result = await client.ClassifyAsync(new AiClassificationRequest("Some text."), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith(AiGatewayErrors.UnavailablePrefix, result.Error, StringComparison.Ordinal);
        Assert.Equal(4, handler.Requests.Count); // 1 attempt + 3 retries
    }

    [Fact]
    public async Task Classify_reads_only_the_configured_prefix_of_a_long_document()
    {
        var options = new AiGatewayJevOptions
        {
            Enabled = true, ApiKey = "fake-openrouter-key", ClassifyMaxInputChars = 100,
        };
        var (client, handler) = CreateClient(
            FakeHttpMessageHandler.Json(HttpStatusCode.OK, AnswerEnvelope("documentType", "Msa", 0.9, nested: false)),
            options);
        var longText = new string('x', 100) + "TAIL-THAT-MUST-NOT-BE-SENT";

        var result = await client.ClassifyAsync(new AiClassificationRequest(longText), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("TAIL-THAT-MUST-NOT-BE-SENT", handler.RequestBodies[0]!, StringComparison.Ordinal);
    }
}
