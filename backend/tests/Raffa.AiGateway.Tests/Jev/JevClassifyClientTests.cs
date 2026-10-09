using System.Net;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Jev;
using Raffa.AiGateway.Tests.TestSupport;

namespace Raffa.AiGateway.Tests.Jev;

/// <summary>
/// Proves the Jev pilot's `classify` role over a fake HTTP handler: request shape (one "choice"
/// question over the fixed <see cref="AiDocumentType"/> taxonomy, asked in both option orders),
/// TypeSafe's documented flat answer shape (https://docs.typesafe.ai/api#choice-answer), the audit
/// metadata, and failure handling. Mirrors <c>Foundry.FoundryClassifyClientTests</c>'s own structure
/// so the two are easy to compare.
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

    /// <summary>A System One response with the type answered in both option orders.</summary>
    private static string Envelope(
        string choice, double confidence, string? reversedChoice = null, double? reversedConfidence = null) =>
        JsonSerializer.Serialize(new
        {
            model = "typesafe/jev-1.13-20260917",
            answers = new Dictionary<string, object>
            {
                ["documentType"] = ChoiceAnswer(choice, confidence),
                ["documentTypeReversed"] = ChoiceAnswer(reversedChoice ?? choice, reversedConfidence ?? confidence),
            },
            usage = new { input_tokens = 321, output_tokens = 7, cost = 0.00001 },
        });

    private static object ChoiceAnswer(string choice, double confidence) => new
    {
        type = "choice",
        choice,
        probabilities = new Dictionary<string, double> { [choice] = confidence, ["Other"] = 1 - confidence },
        confidence,
    };

    [Fact]
    public async Task Classify_parses_the_documented_answer_and_records_the_served_model_and_tokens()
    {
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, Envelope("Msa", 0.87)));

        var result = await client.ClassifyAsync(
            new AiClassificationRequest("This MASTER SERVICES AGREEMENT is entered into as of..."),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AiDocumentType.Msa, result.Value.DocumentType);
        Assert.Equal(0.87, result.Value.Confidence);
        Assert.Equal("typesafe/jev-1.13", result.Value.Metadata.ModelId);
        Assert.Equal("typesafe/jev-1.13-20260917", result.Value.Metadata.ModelVersion);
        Assert.Equal(JevClassifyClient.Version, result.Value.Metadata.PromptVersion);
        Assert.Equal(Now, result.Value.Metadata.RespondedAtUtc);
        Assert.Equal(321, result.Value.Metadata.Usage?.PromptTokens);
        Assert.Equal(7, result.Value.Metadata.Usage?.CompletionTokens);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("fake-openrouter-key", request.Headers.Authorization?.Parameter);

        using var bodyJson = JsonDocument.Parse(Assert.Single(handler.RequestBodies)!);
        Assert.Equal("typesafe/jev-1.13", bodyJson.RootElement.GetProperty("model").GetString());
        Assert.Equal(JsonValueKind.String, bodyJson.RootElement.GetProperty("state").ValueKind);
        var question = bodyJson.RootElement.GetProperty("questions").GetProperty("documentType");
        Assert.Equal("choice", question.GetProperty("type").GetString());
        Assert.True(question.GetProperty("criteria").TryGetProperty("Msa", out _));
        Assert.True(question.GetProperty("criteria").TryGetProperty("Other", out _));
    }

    [Fact]
    public async Task Classify_asks_the_same_choice_again_with_the_options_in_the_opposite_order()
    {
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, Envelope("Msa", 0.9)));

        await client.ClassifyAsync(new AiClassificationRequest("Some text."), CancellationToken.None);

        using var bodyJson = JsonDocument.Parse(Assert.Single(handler.RequestBodies)!);
        var questions = bodyJson.RootElement.GetProperty("questions");
        var forward = questions.GetProperty("documentType").GetProperty("criteria").EnumerateObject().Select(p => p.Name).ToList();
        var backward = questions.GetProperty("documentTypeReversed").GetProperty("criteria").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(forward.AsEnumerable().Reverse(), backward);
        Assert.Equal(
            questions.GetProperty("documentType").GetProperty("instructions").GetString(),
            questions.GetProperty("documentTypeReversed").GetProperty("instructions").GetString());
    }

    [Fact]
    public async Task Classify_takes_the_lower_confidence_when_both_orders_agree()
    {
        var (client, _) = CreateClient(FakeHttpMessageHandler.Json(
            HttpStatusCode.OK, Envelope("Quote", 0.9, reversedChoice: "Quote", reversedConfidence: 0.7)));

        var result = await client.ClassifyAsync(new AiClassificationRequest("Some text."), CancellationToken.None);

        Assert.Equal(AiDocumentType.Quote, result.Value.DocumentType);
        Assert.Equal(0.7, result.Value.Confidence);
    }

    [Fact]
    public async Task Classify_reports_zero_confidence_when_the_two_orders_disagree()
    {
        var (client, _) = CreateClient(FakeHttpMessageHandler.Json(
            HttpStatusCode.OK, Envelope("Msa", 0.95, reversedChoice: "OrderForm", reversedConfidence: 0.9)));

        var result = await client.ClassifyAsync(new AiClassificationRequest("Some text."), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Confidence);
    }

    [Fact]
    public async Task Classify_sends_one_question_when_the_order_check_is_off()
    {
        var options = new AiGatewayJevOptions { Enabled = true, ApiKey = "fake-openrouter-key", CheckOptionOrder = false };
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, Envelope("Msa", 0.8)), options);

        var result = await client.ClassifyAsync(new AiClassificationRequest("Some text."), CancellationToken.None);

        Assert.Equal(0.8, result.Value.Confidence);
        using var bodyJson = JsonDocument.Parse(Assert.Single(handler.RequestBodies)!);
        Assert.False(bodyJson.RootElement.GetProperty("questions").TryGetProperty("documentTypeReversed", out _));
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
        var (client, _) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, Envelope("SomethingNotInTheEnum", 0.5)));

        var result = await client.ClassifyAsync(new AiClassificationRequest("Some text."), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Classify_fails_when_the_answer_carries_no_choice()
    {
        var (client, _) = CreateClient(FakeHttpMessageHandler.Json(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new { answers = new Dictionary<string, object> { ["documentType"] = new { type = "noul", noul = 0.4 } } })));

        var result = await client.ClassifyAsync(new AiClassificationRequest("Some text."), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("no usable", result.Error, StringComparison.OrdinalIgnoreCase);
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
    public async Task Classify_retries_a_529_overload_like_any_other_transient_outcome()
    {
        var calls = 0;
        var (client, handler) = CreateClient(_ =>
        {
            calls++;
            return calls < 3
                ? new HttpResponseMessage((HttpStatusCode)529) { Content = new StringContent("{}") }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Envelope("Nda", 0.95), System.Text.Encoding.UTF8, "application/json"),
                };
        });

        var result = await client.ClassifyAsync(new AiClassificationRequest("Some text."), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AiDocumentType.Nda, result.Value.DocumentType);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Classify_reads_only_the_configured_prefix_of_a_long_document()
    {
        var options = new AiGatewayJevOptions
        {
            Enabled = true, ApiKey = "fake-openrouter-key", ClassifyMaxInputChars = 100,
        };
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, Envelope("Msa", 0.9)), options);
        var longText = new string('x', 100) + "TAIL-THAT-MUST-NOT-BE-SENT";

        var result = await client.ClassifyAsync(new AiClassificationRequest(longText), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("TAIL-THAT-MUST-NOT-BE-SENT", handler.RequestBodies[0]!, StringComparison.Ordinal);
    }
}
