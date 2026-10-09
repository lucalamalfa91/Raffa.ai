using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Foundry;
using Raffa.AiGateway.Tests.TestSupport;

namespace Raffa.AiGateway.Tests.Foundry;

/// <summary>
/// F3-T02 / F3-T03 — the research role's source reconciliation (marker -> the right source, 100%
/// of the small golden web set), the <c>completed</c> status requirement, and the role's own retry
/// count and attempt timeout.
/// </summary>
public class FoundryResearchReconciliationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri FoundryBaseAddress = new("https://fake-foundry.example.com/");
    private static readonly Regex Marker = new(@"\[(\d+)\]", RegexOptions.Compiled);

    public sealed record GoldenSource(int N, string Url, string Title);

    public sealed record GoldenAnnotation(string Url, string Title);

    public sealed record GoldenCase(
        string Name,
        int MaxSources,
        string Summary,
        GoldenSource[] PayloadSources,
        GoldenAnnotation[] Annotations,
        string ExpectedSummary,
        string[] ExpectedSources);

    private static readonly JsonSerializerOptions GoldenJson = new(JsonSerializerDefaults.Web);

    public static TheoryData<string> GoldenNames()
    {
        var data = new TheoryData<string>();
        foreach (var golden in LoadGolden())
        {
            data.Add(golden.Name);
        }

        return data;
    }

    private static GoldenCase[] LoadGolden() =>
        JsonSerializer.Deserialize<GoldenCase[]>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "web-research-golden.json")), GoldenJson)!;

    private static string Envelope(string status, string text, IEnumerable<(string Url, string Title)> citations, string? incompleteReason = null) =>
        JsonSerializer.Serialize(new
        {
            status,
            model = "gpt-5.4-research-dev",
            incomplete_details = incompleteReason is null ? null : new { reason = incompleteReason },
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
                            text,
                            annotations = citations.Select(c => new { type = "url_citation", url = c.Url, title = c.Title }).ToArray(),
                        },
                    },
                },
            },
            usage = new { input_tokens = 300, output_tokens = 150 },
        });

    private static FoundryResearchClient CreateClient(
        HttpMessageHandler handler,
        AiGatewayResilienceOptions? resilience = null,
        int extractionMaxRetries = 3)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = FoundryBaseAddress };
        var foundryOptions = new AiGatewayFoundryOptions { Endpoint = FoundryBaseAddress.ToString() };
        var httpJsonClient = new FoundryHttpJsonClient(
            httpClient, new FoundryTokenProvider(new FakeTokenCredential()), foundryOptions, TestRetryPolicies.NoDelay(extractionMaxRetries));
        return new FoundryResearchClient(
            httpJsonClient,
            foundryOptions,
            new AiGatewayModelOptions { Research = new AiModelSelection("gpt-5.4-research-dev", "2026-03-17") { MaxCompletionTokens = 2048 } },
            new FixedClock(Now),
            resilience);
    }

    private static AiResearchRequest Request(int maxSources = 5) =>
        new("saas renewal uplift market practice", "MarketPractice", "en", maxSources, "You research procurement topics only.", "research-v1");

    [Theory]
    [MemberData(nameof(GoldenNames))]
    public async Task Golden_web_case_resolves_every_marker_to_the_right_source(string name)
    {
        var golden = LoadGolden().Single(g => g.Name == name);
        var text = JsonSerializer.Serialize(new
        {
            summaryMarkdown = golden.Summary,
            offTopic = false,
            sources = golden.PayloadSources.Select(s => new { n = s.N, url = s.Url, title = s.Title }).ToArray(),
        });
        var handler = new FakeHttpMessageHandler(FakeHttpMessageHandler.Json(
            HttpStatusCode.OK, Envelope("completed", text, golden.Annotations.Select(a => (a.Url, a.Title)))));
        var client = CreateClient(handler);

        var result = await client.ResearchAsync(Request(golden.MaxSources), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(golden.ExpectedSummary, result.Value.SummaryMarkdown);
        Assert.Equal(golden.ExpectedSources, result.Value.Sources.Select(s => s.Url).ToArray());

        // Every resolved marker lands on the source the model meant by that number: its URL, looked up
        // in the model's own list by the original n, equals the final source at the new number.
        var originalMarkers = Marker.Matches(golden.Summary).Select(m => int.Parse(m.Groups[1].Value)).ToArray();
        var newMarkers = Marker.Matches(result.Value.SummaryMarkdown).Select(m => int.Parse(m.Groups[1].Value)).ToArray();
        Assert.Equal(originalMarkers.Length, newMarkers.Length);
        for (var i = 0; i < originalMarkers.Length; i++)
        {
            if (newMarkers[i] == 0)
            {
                continue;
            }

            var meant = WebSourceUrl.Normalize(golden.PayloadSources.First(s => s.N == originalMarkers[i]).Url);
            Assert.Equal(meant, result.Value.Sources[newMarkers[i] - 1].Url);
        }
    }

    [Fact]
    public void Golden_set_covers_the_cases_that_matter()
    {
        var names = LoadGolden().Select(g => g.Name).ToArray();
        Assert.True(names.Length >= 10);
        Assert.Contains(names, n => n.Contains("annotation-position", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("max-sources", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("incomplete", "max_output_tokens")]
    [InlineData("failed", null)]
    [InlineData("in_progress", null)]
    public async Task A_response_that_did_not_complete_is_a_handled_failure_not_a_truncated_json(string status, string? reason)
    {
        // The text is valid JSON on purpose: a status other than "completed" must still not be used.
        var text = JsonSerializer.Serialize(new
        {
            summaryMarkdown = "Caps are common [1].",
            offTopic = false,
            sources = new[] { new { n = 1, url = "https://example.com/a", title = "A" } },
        });
        var handler = new FakeHttpMessageHandler(FakeHttpMessageHandler.Json(
            HttpStatusCode.OK, Envelope(status, text, [("https://example.com/a", "A")], reason)));
        var client = CreateClient(handler);

        var result = await client.ResearchAsync(Request(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith(AiGatewayErrors.ResearchOutputPrefix, result.Error, StringComparison.Ordinal);
        Assert.Contains(status, result.Error, StringComparison.Ordinal);
        if (reason is not null)
        {
            Assert.Contains(reason, result.Error, StringComparison.Ordinal);
        }

        // The provider did the work: the budget unit is not handed back.
        Assert.False(AiGatewayErrors.ResearchFailureReleasesBudget(result.Error));
    }

    [Fact]
    public async Task A_truncated_json_body_under_a_completed_status_is_an_output_failure_too()
    {
        var handler = new FakeHttpMessageHandler(FakeHttpMessageHandler.Json(
            HttpStatusCode.OK, Envelope("completed", "{\"summaryMarkdown\":\"Caps are com", [("https://example.com/a", "A")])));
        var client = CreateClient(handler);

        var result = await client.ResearchAsync(Request(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith(AiGatewayErrors.ResearchOutputPrefix, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Research_retries_on_its_own_count_while_extraction_keeps_its_own()
    {
        const string unavailable = """{"error":{"code":"503","message":"busy"}}""";

        var researchHandler = new FakeHttpMessageHandler(FakeHttpMessageHandler.Json(HttpStatusCode.ServiceUnavailable, unavailable));
        var research = CreateClient(researchHandler, new AiGatewayResilienceOptions { MaxRetries = 3, ResearchMaxRetries = 1 });
        var failed = await research.ResearchAsync(Request(), CancellationToken.None);

        Assert.True(failed.IsFailure);
        Assert.StartsWith(AiGatewayErrors.UnavailablePrefix, failed.Error, StringComparison.Ordinal);
        Assert.Equal(2, researchHandler.Requests.Count);
        Assert.True(AiGatewayErrors.ResearchFailureReleasesBudget(failed.Error));

        // No research retries at all: one attempt.
        var noRetryHandler = new FakeHttpMessageHandler(FakeHttpMessageHandler.Json(HttpStatusCode.ServiceUnavailable, unavailable));
        var noRetry = CreateClient(noRetryHandler, new AiGatewayResilienceOptions { MaxRetries = 3, ResearchMaxRetries = 0 });
        await noRetry.ResearchAsync(Request(), CancellationToken.None);
        Assert.Single(noRetryHandler.Requests);

        // The shared extraction path is untouched: the same HTTP client still retries MaxRetries times.
        var extractionHandler = new FakeHttpMessageHandler(FakeHttpMessageHandler.Json(HttpStatusCode.ServiceUnavailable, unavailable));
        using var httpClient = new HttpClient(extractionHandler) { BaseAddress = FoundryBaseAddress };
        var json = new FoundryHttpJsonClient(
            httpClient, new FoundryTokenProvider(new FakeTokenCredential()),
            new AiGatewayFoundryOptions { Endpoint = FoundryBaseAddress.ToString() }, TestRetryPolicies.NoDelay(3));
        var extraction = await json.PostAsync<object, object>("openai/v1/chat/completions", new { }, CancellationToken.None);
        Assert.True(extraction.IsFailure);
        Assert.Equal(4, extractionHandler.Requests.Count);
    }

    [Fact]
    public async Task Research_has_its_own_attempt_timeout_and_a_timeout_is_a_releasable_transport_failure()
    {
        var handler = new SlowHandler(TimeSpan.FromSeconds(30));
        var client = CreateClient(
            handler, new AiGatewayResilienceOptions { MaxRetries = 3, ResearchMaxRetries = 0, ResearchRequestTimeoutSeconds = 1 });

        var result = await client.ResearchAsync(Request(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith(AiGatewayErrors.UnavailablePrefix, result.Error, StringComparison.Ordinal);
        Assert.Contains("timed out after 1s", result.Error, StringComparison.Ordinal);
        Assert.Equal(1, handler.Calls);
        Assert.True(AiGatewayErrors.ResearchFailureReleasesBudget(result.Error));
    }

    [Fact]
    public async Task The_callers_own_cancellation_is_not_mistaken_for_the_research_timeout()
    {
        var handler = new SlowHandler(TimeSpan.FromSeconds(30));
        var client = CreateClient(handler, new AiGatewayResilienceOptions { ResearchMaxRetries = 2, ResearchRequestTimeoutSeconds = 60 });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ResearchAsync(Request(), cts.Token));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task A_policy_attempt_timeout_retries_up_to_the_override_then_gives_up_unavailable()
    {
        var handler = new SlowHandler(TimeSpan.FromSeconds(30));
        using var httpClient = new HttpClient(handler) { BaseAddress = FoundryBaseAddress };
        var policy = TestRetryPolicies.NoDelay(maxRetries: 5);

        var result = await policy.SendAsync(
            httpClient,
            _ => Task.FromResult(new HttpRequestMessage(HttpMethod.Get, "probe")),
            CancellationToken.None,
            maxRetriesOverride: 2,
            attemptTimeout: TimeSpan.FromMilliseconds(50));

        Assert.True(result.IsFailure);
        Assert.StartsWith(AiGatewayErrors.UnavailablePrefix, result.Error, StringComparison.Ordinal);
        Assert.Equal(3, handler.Calls);
    }

    [Theory]
    [InlineData("Foundry request to 'openai/v1/responses' failed with 400 BadRequest: content_filter: The response was filtered", false)]
    [InlineData("Foundry request failed with 400 BadRequest: ResponsibleAIPolicyViolation (content_filter): blocked", false)]
    [InlineData("AI provider unavailable: 'x' still failing after 1 retry (last outcome: 503 ServiceUnavailable)", true)]
    [InlineData("AiGateway:Models:Research is not configured; the research role is unavailable", true)]
    [InlineData("Foundry request to 'openai/v1/responses' failed with 401 Unauthorized: 401: token", true)]
    [InlineData("Research requires a non-empty query.", true)]
    [InlineData("AI research output unusable: Foundry research output parsed to null.", false)]
    public void Budget_release_classification_keeps_content_filter_and_unusable_output_spent(string error, bool releases) =>
        Assert.Equal(releases, AiGatewayErrors.ResearchFailureReleasesBudget(error));

    private sealed class SlowHandler(TimeSpan delay) : HttpMessageHandler
    {
        private int _calls;

        public int Calls => _calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
