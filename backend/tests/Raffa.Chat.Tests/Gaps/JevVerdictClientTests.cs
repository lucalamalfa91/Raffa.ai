using System.Net;
using System.Text;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Foundry;
using Raffa.AiGateway.Jev;
using Raffa.Chat.Application.Gaps;
using Raffa.SharedKernel;

namespace Raffa.Chat.Tests.Gaps;

/// <summary>Proves <see cref="JevVerdictClient"/>'s own contract directly: which questions it
/// asks (verdict always; knownGapKey/nearestCapabilityKey only when criteria are given), and how
/// it reads the three answers back. <c>CapabilityInvestigatorTests</c> exercises this client
/// end-to-end through <see cref="CapabilityInvestigator"/>; these tests isolate the client
/// itself.</summary>
public sealed class JevVerdictClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static (JevVerdictClient Client, FakeHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeHandler(respond);
        var options = new AiGatewayJevOptions { Enabled = true, ApiKey = "fake-key" };
        var httpClient = new JevHttpJsonClient(
            new HttpClient(handler), options, new FoundryRetryPolicy(new AiGatewayResilienceOptions { MaxRetries = 0 }));
        return (new JevVerdictClient(httpClient, options, new FixedClock(Now)), handler);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    [Fact]
    public async Task DecideAsync_asks_only_the_verdict_question_when_the_other_criteria_are_empty()
    {
        var (client, handler) = Create(_ => Json(
            HttpStatusCode.OK, """{"answers":{"verdict":{"choice":"question","confidence":0.9}}}"""));

        var result = await client.DecideAsync(
            "{}", new Dictionary<string, string> { ["question"] = "..." },
            new Dictionary<string, string>(), new Dictionary<string, string>(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("question", result.Value.Verdict);
        Assert.Equal(0.9, result.Value.Confidence);
        Assert.Null(result.Value.KnownGapKey);
        Assert.Null(result.Value.NearestCapabilityKey);

        using var body = JsonDocument.Parse(handler.RequestBodies[0]);
        var questions = body.RootElement.GetProperty("questions");
        Assert.True(questions.TryGetProperty("verdict", out _));
        Assert.False(questions.TryGetProperty("knownGapKey", out _));
        Assert.False(questions.TryGetProperty("nearestCapabilityKey", out _));
    }

    [Fact]
    public async Task DecideAsync_asks_all_three_questions_when_criteria_are_given_and_reads_all_three_answers()
    {
        var (client, handler) = Create(_ => Json(
            HttpStatusCode.OK,
            """
            {"answers":{
              "verdict":{"choice":"gap","confidence":0.88},
              "knownGapKey":{"choice":"reminder","confidence":0.5},
              "nearestCapabilityKey":{"choice":"portfolio","confidence":0.7}
            }}
            """));

        var result = await client.DecideAsync(
            "{}",
            new Dictionary<string, string> { ["gap"] = "..." },
            new Dictionary<string, string> { ["reminder"] = "..." },
            new Dictionary<string, string> { ["portfolio"] = "..." },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("gap", result.Value.Verdict);
        Assert.Equal("reminder", result.Value.KnownGapKey);
        Assert.Equal("portfolio", result.Value.NearestCapabilityKey);
        Assert.Equal("typesafe/jev-1.13", result.Value.Metadata.ModelId);

        using var body = JsonDocument.Parse(handler.RequestBodies[0]);
        var questions = body.RootElement.GetProperty("questions");
        Assert.True(questions.TryGetProperty("verdict", out _));
        Assert.True(questions.TryGetProperty("knownGapKey", out _));
        Assert.True(questions.TryGetProperty("nearestCapabilityKey", out _));
    }

    [Fact]
    public async Task DecideAsync_fails_when_the_response_carries_no_verdict_answer()
    {
        var (client, _) = Create(_ => Json(HttpStatusCode.OK, """{"answers":{}}"""));

        var result = await client.DecideAsync(
            "{}", new Dictionary<string, string> { ["question"] = "..." },
            new Dictionary<string, string>(), new Dictionary<string, string>(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("verdict", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DecideAsync_propagates_a_gateway_failure()
    {
        var (client, _) = Create(_ => Json(HttpStatusCode.InternalServerError, """{"error":{"message":"boom"}}"""));

        var result = await client.DecideAsync(
            "{}", new Dictionary<string, string> { ["question"] = "..." },
            new Dictionary<string, string>(), new Dictionary<string, string>(), CancellationToken.None);

        Assert.True(result.IsFailure);
    }
}
