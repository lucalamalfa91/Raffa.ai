using System.Net;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Foundry;
using Raffa.AiGateway.Tests.TestSupport;

namespace Raffa.AiGateway.Tests.Foundry;

/// <summary>
/// ADR-030 — the inverse of <see cref="FoundryAnswerClientTests"/>'s no-tools proof: the research
/// role is the <em>only</em> request that carries a <c>tools</c> key, it carries exactly the hosted
/// web search and nothing else, it never carries a context pack, and it refuses to run without its
/// own deployment.
/// </summary>
public class FoundryResearchClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri FoundryBaseAddress = new("https://fake-foundry.example.com/");

    private static readonly string[] AllowedRootProperties =
        ["model", "instructions", "input", "tools", "text", "max_output_tokens"];

    private static (FoundryResearchClient Client, FakeHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> response, AiGatewayModelOptions? modelOptions = null)
    {
        var handler = new FakeHttpMessageHandler(response);
        var httpClient = new HttpClient(handler) { BaseAddress = FoundryBaseAddress };
        var foundryOptions = new AiGatewayFoundryOptions { Endpoint = FoundryBaseAddress.ToString() };
        var tokenProvider = new FoundryTokenProvider(new FakeTokenCredential());
        var httpJsonClient = new FoundryHttpJsonClient(httpClient, tokenProvider, foundryOptions, TestRetryPolicies.NoDelay());
        var options = modelOptions ?? new AiGatewayModelOptions
        {
            Research = new AiModelSelection("gpt-5.4-research-dev", "2026-03-17") { MaxCompletionTokens = 2048 },
        };
        var client = new FoundryResearchClient(httpJsonClient, foundryOptions, options, new FixedClock(Now));

        return (client, handler);
    }

    private static AiResearchRequest Request(string query = "saas renewal uplift cap market practice") =>
        new(query, "MarketPractice", "en", MaxSources: 3, SystemPrompt: "You research procurement topics only.", PromptVersion: "research-v1");

    private static string ResponsesEnvelope(object payload, params (string Url, string Title)[] citations)
    {
        var envelope = new
        {
            status = "completed",
            model = "gpt-5.4-research-dev",
            output = new object[]
            {
                new { type = "web_search_call", status = "completed" },
                new
                {
                    type = "message",
                    content = new[]
                    {
                        new
                        {
                            type = "output_text",
                            text = JsonSerializer.Serialize(payload),
                            annotations = citations.Select(c => new { type = "url_citation", url = c.Url, title = c.Title }).ToArray(),
                        },
                    },
                },
            },
            usage = new { input_tokens = 300, output_tokens = 150 },
        };
        return JsonSerializer.Serialize(envelope);
    }

    [Fact]
    public async Task Research_request_carries_exactly_the_web_search_tool_and_no_pack()
    {
        var (client, handler) = CreateClient(
            FakeHttpMessageHandler.Json(
                HttpStatusCode.OK,
                ResponsesEnvelope(
                    new { summaryMarkdown = "Uplift caps of 5-10% are common [1].", offTopic = false, sources = new[] { new { n = 1, url = "https://example.com/a", title = "A" } } },
                    ("https://example.com/a", "A"))));

        var result = await client.ResearchAsync(Request(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/openai/v1/responses", request.RequestUri!.AbsolutePath);

        var body = Assert.Single(handler.RequestBodies)!;
        using var bodyJson = JsonDocument.Parse(body);
        var rootProperties = bodyJson.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.All(rootProperties, name => Assert.Contains(name, AllowedRootProperties));
        var tools = bodyJson.RootElement.GetProperty("tools").EnumerateArray().ToList();
        var tool = Assert.Single(tools);
        Assert.Equal("web_search", tool.GetProperty("type").GetString());
        Assert.Single(tool.EnumerateObject());
        Assert.Equal("gpt-5.4-research-dev", bodyJson.RootElement.GetProperty("model").GetString());
        Assert.Equal(2048, bodyJson.RootElement.GetProperty("max_output_tokens").GetInt32());

        // Never a context pack, never evidence, never a citation key -- the request type has no such slot.
        Assert.DoesNotContain("citationKey", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("packJson", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Context pack", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data_sources", rootProperties);
        Assert.DoesNotContain("tool_choice", rootProperties);
        Assert.Equal(new AiTokenUsage(300, 150), result.Value.Metadata.Usage);
        Assert.Equal("research-v1", result.Value.Metadata.PromptVersion);
    }

    [Fact]
    public async Task Sources_come_from_the_tools_own_citations_never_from_the_models_text()
    {
        var (client, _) = CreateClient(
            FakeHttpMessageHandler.Json(
                HttpStatusCode.OK,
                ResponsesEnvelope(
                    new
                    {
                        summaryMarkdown = "Two findings [1][2].",
                        offTopic = false,
                        sources = new[]
                        {
                            new { n = 1, url = "https://example.com/a", title = "Titled by the model" },
                            new { n = 2, url = "https://made-up.example/never-visited", title = "Invented" },
                        },
                    },
                    ("https://example.com/a", ""),
                    ("https://example.com/a/", "duplicate of a"),
                    ("https://example.org/b", "B"))));

        var result = await client.ResearchAsync(Request(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(["https://example.com/a", "https://example.org/b"], result.Value.Sources.Select(s => s.Url).ToList());
        // An untitled annotation borrows the model's title for the same URL; an invented URL never appears.
        Assert.Equal("Titled by the model", result.Value.Sources[0].Title);
        Assert.False(result.Value.OffTopic);
        Assert.Equal("Two findings [1][2].", result.Value.SummaryMarkdown);
    }

    [Fact]
    public async Task Off_topic_yields_an_empty_summary_and_no_sources()
    {
        var (client, _) = CreateClient(
            FakeHttpMessageHandler.Json(
                HttpStatusCode.OK,
                ResponsesEnvelope(new { summaryMarkdown = "I only research procurement topics.", offTopic = true, sources = Array.Empty<object>() })));

        var result = await client.ResearchAsync(Request("best pizza in naples"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.OffTopic);
        Assert.Empty(result.Value.Sources);
        Assert.Equal(string.Empty, result.Value.SummaryMarkdown);
    }

    [Fact]
    public async Task Without_a_research_deployment_nothing_is_sent_and_the_answer_deployment_is_never_used()
    {
        var (client, handler) = CreateClient(
            FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"),
            new AiGatewayModelOptions { Answer = new AiModelSelection("gpt-answer", "1") });

        var result = await client.ResearchAsync(Request(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("AiGateway:Models:Research", result.Error, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_query_that_carries_pack_content_is_refused_before_any_call()
    {
        var (client, handler) = CreateClient(FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));

        var result = await client.ResearchAsync(
            Request("[{\"citationKey\":\"fact:1\",\"snippet\":\"liability cap CHF 1,000,000\"}]"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_date_based_api_version_is_refused_because_it_has_no_responses_route()
    {
        var handler = new FakeHttpMessageHandler(FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var httpClient = new HttpClient(handler) { BaseAddress = FoundryBaseAddress };
        var foundryOptions = new AiGatewayFoundryOptions { Endpoint = FoundryBaseAddress.ToString(), OpenAiApiVersion = "2024-10-21" };
        var httpJsonClient = new FoundryHttpJsonClient(httpClient, new FoundryTokenProvider(new FakeTokenCredential()), foundryOptions, TestRetryPolicies.NoDelay());
        var client = new FoundryResearchClient(
            httpJsonClient, foundryOptions,
            new AiGatewayModelOptions { Research = new AiModelSelection("gpt-5.4-research-dev", "2026-03-17") },
            new FixedClock(Now));

        var result = await client.ResearchAsync(Request(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("openai/v1", result.Error, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }
}
