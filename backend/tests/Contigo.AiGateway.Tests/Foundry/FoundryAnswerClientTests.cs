using System.Net;
using System.Text.Json;
using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Contracts;
using Contigo.AiGateway.Foundry;
using Contigo.AiGateway.Tests.TestSupport;

namespace Contigo.AiGateway.Tests.Foundry;

/// <summary>
/// Proves the compliance objective for the `answer` role — ADR-024: "the answer request sent to
/// Foundry carries no tools, tool_choice, grounding or browsing payload (asserted on a fake HTTP
/// handler)" and "temperature &lt;= 0.2" whenever a temperature is sent — plus the structured
/// response shape (canDetermine/answerMarkdown/citationKeys/actionKeys/abstainReason/followUps)
/// and the evidence-only vs. pack-based grounding paths <see cref="AiAnswerRequest"/>'s own doc
/// comment describes.
/// </summary>
public class FoundryAnswerClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri FoundryBaseAddress = new("https://fake-foundry.example.com/");

    private static readonly string[] AllowedRootProperties =
        ["model", "messages", "response_format", "max_completion_tokens", "temperature", "reasoning_effort"];

    private static (FoundryAnswerClient Client, FakeHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> response, AiGatewayModelOptions? modelOptions = null)
    {
        var handler = new FakeHttpMessageHandler(response);
        var httpClient = new HttpClient(handler) { BaseAddress = FoundryBaseAddress };
        var foundryOptions = new AiGatewayFoundryOptions { Endpoint = FoundryBaseAddress.ToString() };
        var tokenProvider = new FoundryTokenProvider(new FakeTokenCredential());
        var httpJsonClient = new FoundryHttpJsonClient(httpClient, tokenProvider, foundryOptions, TestRetryPolicies.NoDelay());
        var chatClient = new FoundryChatCompletionsClient(httpJsonClient, foundryOptions);
        var client = new FoundryAnswerClient(chatClient, modelOptions ?? new AiGatewayModelOptions(), new FixedClock(Now));

        return (client, handler);
    }

    private static string ChatEnvelope(object payload)
    {
        var contentJson = JsonSerializer.Serialize(payload);
        var envelope = new
        {
            choices = new[] { new { message = new { role = "assistant", content = contentJson }, finish_reason = "stop" } },
            usage = new { prompt_tokens = 900, completion_tokens = 120, total_tokens = 1020 },
        };
        return JsonSerializer.Serialize(envelope);
    }

    [Fact]
    public async Task Answer_abstains_with_no_evidence_and_no_pack_without_calling_Foundry()
    {
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));

        var result = await client.AnswerAsync(
            new AiAnswerRequest("What liability do we have with AWS?", Evidence: []), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.CanDetermine);
        Assert.Null(result.Value.Answer);
        Assert.Empty(result.Value.Citations);
        Assert.NotNull(result.Value.AbstainReason);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Answer_request_carries_no_tools_tool_choice_or_grounding_fields()
    {
        var evidence = new AiEvidenceSnippet("doc-123", Page: 12, Section: "8.4", Text: "Liability capped at $1,000,000.");
        var (client, handler) = CreateClient(
            FakeHttpMessageHandler.Json(
                HttpStatusCode.OK,
                ChatEnvelope(new
                {
                    canDetermine = true,
                    answerMarkdown = "Liability is capped at $1,000,000.",
                    citationKeys = new[] { "doc-123" },
                    actionKeys = Array.Empty<string>(),
                    abstainReason = (string?)null,
                    followUps = Array.Empty<string>(),
                })));

        var result = await client.AnswerAsync(
            new AiAnswerRequest("What liability do we have with AWS?", Evidence: [evidence]), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var body = Assert.Single(handler.RequestBodies)!;
        using var bodyJson = JsonDocument.Parse(body);
        var rootProperties = bodyJson.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        // ADR-024: no tools, no tool_choice, no grounding/browsing payload of any kind — asserted on
        // the actual top-level JSON *keys* sent, not a raw substring search (the system prompt's own
        // English text legitimately instructs the model not to browse/ground, which would give a
        // substring search a false positive). Wire.ChatCompletionRequest's own doc comment: these
        // keys cannot appear because the .NET type has no such property at all. The optional knobs
        // (temperature, reasoning_effort) are absent unless configured.
        Assert.All(rootProperties, name => Assert.Contains(name, AllowedRootProperties));
        Assert.Contains("model", rootProperties);
        Assert.Contains("messages", rootProperties);
        Assert.Contains("response_format", rootProperties);
        Assert.DoesNotContain("temperature", rootProperties);
        Assert.DoesNotContain("reasoning_effort", rootProperties);
        Assert.DoesNotContain("tools", rootProperties);
        Assert.DoesNotContain("tool_choice", rootProperties);
        Assert.DoesNotContain("functions", rootProperties);
        Assert.DoesNotContain("data_sources", rootProperties);
        Assert.Equal(4096, bodyJson.RootElement.GetProperty("max_completion_tokens").GetInt32());
        Assert.Equal(new AiTokenUsage(900, 120), result.Value.Metadata.Usage);
    }

    [Fact]
    public async Task Answer_temperature_is_sent_only_when_configured_and_clamped_to_the_ADR_024_ceiling()
    {
        var evidence = new AiEvidenceSnippet("doc-1", null, null, "Some evidence text.");
        var options = new AiGatewayModelOptions
        {
            Answer = new AiModelSelection("gpt-5.4-nano-dev", "2026-03-17") { Temperature = 0.9, ReasoningEffort = "none" },
        };
        var (client, handler) = CreateClient(
            FakeHttpMessageHandler.Json(
                HttpStatusCode.OK,
                ChatEnvelope(new
                {
                    canDetermine = true,
                    answerMarkdown = "Answer.",
                    citationKeys = new[] { "doc-1" },
                    actionKeys = Array.Empty<string>(),
                    abstainReason = (string?)null,
                    followUps = Array.Empty<string>(),
                })),
            options);

        await client.AnswerAsync(new AiAnswerRequest("Question?", Evidence: [evidence]), CancellationToken.None);

        var body = Assert.Single(handler.RequestBodies)!;
        using var bodyJson = JsonDocument.Parse(body);
        Assert.Equal(0.2, bodyJson.RootElement.GetProperty("temperature").GetDouble());
        Assert.Equal("none", bodyJson.RootElement.GetProperty("reasoning_effort").GetString());
        Assert.Equal("gpt-5.4-nano-dev", bodyJson.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Answer_grounds_in_evidence_and_populates_legacy_citations_when_there_is_no_pack()
    {
        var evidence = new AiEvidenceSnippet("doc-123", Page: 12, Section: "8.4", Text: "Liability capped at $1,000,000.");
        var (client, _) = CreateClient(
            FakeHttpMessageHandler.Json(
                HttpStatusCode.OK,
                ChatEnvelope(new
                {
                    canDetermine = true,
                    answerMarkdown = "Liability is capped at $1,000,000.",
                    citationKeys = new[] { "doc-123" },
                    actionKeys = Array.Empty<string>(),
                    abstainReason = (string?)null,
                    followUps = Array.Empty<string>(),
                })));

        var result = await client.AnswerAsync(
            new AiAnswerRequest("What liability do we have with AWS?", Evidence: [evidence]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.CanDetermine);
        Assert.Equal("Liability is capped at $1,000,000.", result.Value.Answer);
        Assert.Equal("Liability is capped at $1,000,000.", result.Value.AnswerMarkdown);

        var citation = Assert.Single(result.Value.Citations);
        Assert.Equal("doc-123", citation.DocumentId);
        Assert.Equal(12, citation.Page);
        Assert.Equal("8.4", citation.Section);

        Assert.Equal(["doc-123"], result.Value.CitationKeys);
    }

    [Fact]
    public async Task Answer_with_a_pack_leaves_legacy_citations_empty_and_uses_citation_keys_instead()
    {
        var (client, handler) = CreateClient(
            FakeHttpMessageHandler.Json(
                HttpStatusCode.OK,
                ChatEnvelope(new
                {
                    canDetermine = true,
                    answerMarkdown = "Contigo's own market feed shows a P25-P75 band of...",
                    citationKeys = new[] { "market:deal-42", "contract:doc-9#p3" },
                    actionKeys = new[] { "renewals" },
                    abstainReason = (string?)null,
                    followUps = new[] { "Want a renewal strategy for this contract?" },
                })));

        var result = await client.AnswerAsync(
            new AiAnswerRequest(
                "How does our AWS contract compare to market?",
                Evidence: [],
                SystemPrompt: "versioned persona v3",
                PackJson: """{"items":[{"citationKey":"market:deal-42"}]}"""),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.CanDetermine);
        Assert.Empty(result.Value.Citations);
        Assert.Equal(["market:deal-42", "contract:doc-9#p3"], result.Value.CitationKeys);
        Assert.Equal(["renewals"], result.Value.ActionKeys);
        Assert.Single(result.Value.FollowUps!);

        var body = Assert.Single(handler.RequestBodies)!;
        Assert.Contains("versioned persona v3", body, StringComparison.Ordinal);
        Assert.Contains("market:deal-42", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Answer_reports_the_models_own_abstain_reason_when_it_cannot_determine()
    {
        var evidence = new AiEvidenceSnippet("doc-1", null, null, "Unrelated clause text.");
        var (client, _) = CreateClient(
            FakeHttpMessageHandler.Json(
                HttpStatusCode.OK,
                ChatEnvelope(new
                {
                    canDetermine = false,
                    answerMarkdown = (string?)null,
                    citationKeys = Array.Empty<string>(),
                    actionKeys = Array.Empty<string>(),
                    abstainReason = "The evidence does not mention AWS liability terms.",
                    followUps = Array.Empty<string>(),
                })));

        var result = await client.AnswerAsync(
            new AiAnswerRequest("What liability do we have with AWS?", Evidence: [evidence]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.CanDetermine);
        Assert.Null(result.Value.Answer);
        Assert.Empty(result.Value.Citations);
        Assert.Equal("The evidence does not mention AWS liability terms.", result.Value.AbstainReason);
    }

    [Fact]
    public async Task Answer_fails_without_a_question()
    {
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));

        var result = await client.AnswerAsync(new AiAnswerRequest("", Evidence: []), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Answer_uses_the_default_persona_prompt_when_the_caller_supplies_none()
    {
        var evidence = new AiEvidenceSnippet("doc-1", null, null, "Some evidence.");
        var (client, handler) = CreateClient(
            FakeHttpMessageHandler.Json(
                HttpStatusCode.OK,
                ChatEnvelope(new
                {
                    canDetermine = true,
                    answerMarkdown = "Answer.",
                    citationKeys = new[] { "doc-1" },
                    actionKeys = Array.Empty<string>(),
                    abstainReason = (string?)null,
                    followUps = Array.Empty<string>(),
                })));

        await client.AnswerAsync(new AiAnswerRequest("Question?", Evidence: [evidence]), CancellationToken.None);

        var body = Assert.Single(handler.RequestBodies)!;
        Assert.Contains("Ask Contigo", body, StringComparison.Ordinal);
        Assert.Contains("language of the question", body, StringComparison.Ordinal);
    }
}
