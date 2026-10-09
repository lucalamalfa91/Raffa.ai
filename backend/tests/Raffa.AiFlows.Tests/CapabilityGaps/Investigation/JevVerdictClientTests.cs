using System.Net;
using System.Text;
using System.Text.Json;
using Raffa.AiFlows.CapabilityGaps.Investigation;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Foundry;
using Raffa.AiGateway.Jev;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.CapabilityGaps.Investigation;

/// <summary>Proves <see cref="JevVerdictClient"/>'s own contract directly: what it puts in the
/// request (the turn as state, the operation Choice in both option orders, the nearest-capability
/// Choice when there is something to choose from) and how it reads the answers back.
/// <c>CapabilityInvestigatorTests</c> exercises this client end-to-end through
/// <see cref="CapabilityInvestigator"/>; these tests isolate the client itself.</summary>
public sealed class JevVerdictClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, string> Operations = new()
    {
        [JevVerdictClient.OperationQuestion] = "an ordinary question",
        ["capability:portfolio"] = "Raffa already does this",
        ["known-gap:reminder"] = "Raffa does not do this yet",
        [JevVerdictClient.OperationNone] = "nothing else fits",
    };

    private static readonly Dictionary<string, string> Nearest = new()
    {
        ["portfolio"] = "Portfolio",
        ["ask"] = "An answer in the chat.",
    };

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

    private static object Choice(string choice, double confidence) => new
    {
        type = "choice",
        choice,
        probabilities = new Dictionary<string, double> { [choice] = confidence },
        confidence,
    };

    private static string Body(Dictionary<string, object> answers, string? model = null, int? inputTokens = null) =>
        JsonSerializer.Serialize(new
        {
            model,
            answers,
            usage = inputTokens is { } tokens ? new { input_tokens = tokens, output_tokens = 9 } : null,
        });

    private static (JevVerdictClient Client, FakeHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond, AiGatewayJevOptions? options = null)
    {
        var handler = new FakeHandler(respond);
        options ??= new AiGatewayJevOptions { Enabled = true, ApiKey = "fake-key" };
        var httpClient = new JevHttpJsonClient(
            new HttpClient(handler), options, new FoundryRetryPolicy(new AiGatewayResilienceOptions { MaxRetries = 0 }));
        return (new JevVerdictClient(httpClient, options, new FixedClock(Now)), handler);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    [Fact]
    public async Task DecideAsync_sends_only_the_turn_as_state_and_asks_the_operation_in_both_orders()
    {
        var (client, handler) = Create(_ => Json(HttpStatusCode.OK, Body(
            new Dictionary<string, object>
            {
                ["operation"] = Choice("known-gap:reminder", 0.9),
                ["operationReversed"] = Choice("known-gap:reminder", 0.8),
                ["nearestCapability"] = Choice("portfolio", 0.7),
            },
            model: "typesafe/jev-1.13-20260917",
            inputTokens: 512)));

        var result = await client.DecideAsync("remind me", "en", Operations, Nearest, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("known-gap:reminder", result.Value.Operation);
        Assert.Equal(0.8, result.Value.Confidence); // the lower of the two orders
        Assert.True(result.Value.OrderConsistent);
        Assert.Equal("portfolio", result.Value.NearestCapabilityKey);
        Assert.Equal("typesafe/jev-1.13-20260917", result.Value.Metadata.ModelVersion);
        Assert.Equal(512, result.Value.Metadata.Usage?.PromptTokens);
        Assert.Equal(JevVerdictClient.Version, result.Value.Metadata.PromptVersion);

        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var state = body.RootElement.GetProperty("state");
        Assert.Equal("remind me", state.GetProperty("question").GetString());
        Assert.Equal("en", state.GetProperty("language").GetString());
        Assert.Equal(2, state.EnumerateObject().Count());

        var questions = body.RootElement.GetProperty("questions");
        Assert.Equal(
            Operations.Keys,
            questions.GetProperty("operation").GetProperty("criteria").EnumerateObject().Select(p => p.Name));
        Assert.Equal(
            Operations.Keys.Reverse(),
            questions.GetProperty("operationReversed").GetProperty("criteria").EnumerateObject().Select(p => p.Name));
        Assert.All(
            new[] { "operation", "operationReversed", "nearestCapability" },
            key => Assert.Equal("choice", questions.GetProperty(key).GetProperty("type").GetString()));
    }

    [Fact]
    public async Task DecideAsync_reports_zero_confidence_when_the_two_orders_disagree()
    {
        var (client, _) = Create(_ => Json(HttpStatusCode.OK, Body(new Dictionary<string, object>
        {
            ["operation"] = Choice("none", 0.95),
            ["operationReversed"] = Choice("question", 0.9),
        })));

        var result = await client.DecideAsync("x", "en", Operations, new Dictionary<string, string>(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.OrderConsistent);
        Assert.Equal(0, result.Value.Confidence);
    }

    [Fact]
    public async Task DecideAsync_omits_the_nearest_capability_question_when_there_is_nothing_to_choose_from_and_skips_the_reversed_one_when_off()
    {
        var options = new AiGatewayJevOptions { Enabled = true, ApiKey = "fake-key", CheckOptionOrder = false };
        var (client, handler) = Create(
            _ => Json(HttpStatusCode.OK, Body(new Dictionary<string, object> { ["operation"] = Choice("question", 0.9) })),
            options);

        var result = await client.DecideAsync("x", "en", Operations, new Dictionary<string, string>(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.9, result.Value.Confidence);
        Assert.Null(result.Value.NearestCapabilityKey);
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var questions = body.RootElement.GetProperty("questions");
        Assert.Equal(["operation"], questions.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task DecideAsync_fails_loudly_when_the_response_has_no_usable_operation_answer()
    {
        var (client, _) = Create(_ => Json(HttpStatusCode.OK, """{"answers":{"operation":{"type":"noul","noul":0.4}}}"""));

        var result = await client.DecideAsync("x", "en", Operations, Nearest, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("operation", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DecideAsync_fails_when_Jev_returns_an_error_status()
    {
        var (client, _) = Create(_ => Json(HttpStatusCode.Unauthorized, """{"error":{"code":401,"message":"bad key"}}"""));

        var result = await client.DecideAsync("x", "en", Operations, Nearest, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("401", result.Error, StringComparison.Ordinal);
    }
}
