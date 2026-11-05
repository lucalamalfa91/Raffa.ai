using System.Net;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Foundry;
using Raffa.AiGateway.Tests.TestSupport;

namespace Raffa.AiGateway.Tests.Foundry;

/// <summary>
/// The `agent` role (ADR-004 amendment 2026-11-05): the one chat-completions request that carries
/// function tools. Proves the inverse of <see cref="FoundryAnswerClientTests"/>: every tool is
/// <c>type: "function"</c> with a strict schema, <c>tool_choice</c> is <c>auto</c> (or absent when
/// the final answer is forced), no hosted <c>web_search</c> tool and no grounding payload; a
/// <c>tool_calls</c> reply becomes <see cref="AiConversationResult.ToolCalls"/>, a content reply the
/// final JSON, and a cap / filter / refusal stop is a named failure.
/// </summary>
public class FoundryAgentClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri FoundryBaseAddress = new("https://fake-foundry.example.com/");

    private static readonly string[] AllowedRootProperties =
        ["model", "messages", "tools", "tool_choice", "parallel_tool_calls", "response_format", "max_completion_tokens", "temperature", "reasoning_effort"];

    private const string FinalSchema =
        """{"type":"object","additionalProperties":false,"required":["answerMarkdown","citationKeys"],"properties":{"answerMarkdown":{"type":"string"},"citationKeys":{"type":"array","items":{"type":"string"}}}}""";

    private const string GetContractSchema =
        """{"type":"object","additionalProperties":false,"required":["supplier"],"properties":{"supplier":{"type":"string"}}}""";

    private static (FoundryAgentClient Client, FakeHttpMessageHandler Handler) CreateClient(
        AiGatewayModelOptions? modelOptions = null, params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
    {
        var handler = new FakeHttpMessageHandler(responses);
        var httpClient = new HttpClient(handler) { BaseAddress = FoundryBaseAddress };
        var foundryOptions = new AiGatewayFoundryOptions { Endpoint = FoundryBaseAddress.ToString() };
        var tokenProvider = new FoundryTokenProvider(new FakeTokenCredential());
        var httpJsonClient = new FoundryHttpJsonClient(httpClient, tokenProvider, foundryOptions, TestRetryPolicies.NoDelay());
        var client = new FoundryAgentClient(httpJsonClient, foundryOptions, modelOptions ?? new AiGatewayModelOptions(), new FixedClock(Now));
        return (client, handler);
    }

    private static AiConversationRequest Request(bool forceFinal = false, params AiConversationMessage[] messages) => new(
        "ask-orchestrator",
        "You are Raffa's procurement consultant.",
        messages.Length == 0 ? [AiConversationMessage.User("Quando scade Coupa?")] : messages,
        [new AiToolDefinition("get_contract", "The validated facts of one supplier's contract.", GetContractSchema)],
        FinalSchema,
        "agent-v1",
        forceFinal);

    private static string ToolCallEnvelope(string id, string name, string arguments) => JsonSerializer.Serialize(new
    {
        choices = new[]
        {
            new
            {
                message = new { role = "assistant", content = (string?)null, tool_calls = new[] { new { id, type = "function", function = new { name, arguments } } } },
                finish_reason = "tool_calls",
            },
        },
        usage = new { prompt_tokens = 700, completion_tokens = 40, total_tokens = 740 },
    });

    private static string FinalEnvelope(object payload, string finishReason = "stop") => JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { role = "assistant", content = JsonSerializer.Serialize(payload) }, finish_reason = finishReason } },
        usage = new { prompt_tokens = 900, completion_tokens = 120, total_tokens = 1020 },
    });

    [Fact]
    public async Task Request_carries_function_tools_with_strict_schemas_and_no_web_or_grounding_payload()
    {
        var (client, handler) = CreateClient(null, FakeHttpMessageHandler.Json(HttpStatusCode.OK, ToolCallEnvelope("call_1", "get_contract", """{"supplier":"Coupa"}""")));

        var result = await client.ConverseAsync(Request(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var body = Assert.Single(handler.RequestBodies)!;
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var rootProperties = root.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.All(rootProperties, name => Assert.Contains(name, AllowedRootProperties));
        Assert.DoesNotContain("functions", rootProperties);
        Assert.DoesNotContain("data_sources", rootProperties);

        var tool = Assert.Single(root.GetProperty("tools").EnumerateArray());
        Assert.Equal("function", tool.GetProperty("type").GetString());
        Assert.Equal("get_contract", tool.GetProperty("function").GetProperty("name").GetString());
        Assert.True(tool.GetProperty("function").GetProperty("strict").GetBoolean());
        Assert.False(tool.GetProperty("function").GetProperty("parameters").GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("auto", root.GetProperty("tool_choice").GetString());
        Assert.True(root.GetProperty("parallel_tool_calls").GetBoolean());
        Assert.Equal("json_schema", root.GetProperty("response_format").GetProperty("type").GetString());
        Assert.DoesNotContain("web_search", body, StringComparison.Ordinal);

        var messages = root.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
    }

    [Fact]
    public async Task A_tool_calls_reply_is_returned_as_tool_calls_to_run()
    {
        var (client, _) = CreateClient(null, FakeHttpMessageHandler.Json(HttpStatusCode.OK, ToolCallEnvelope("call_1", "get_contract", """{"supplier":"Coupa"}""")));

        var result = await client.ConverseAsync(Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsFinal);
        Assert.Null(result.Value.FinalJson);
        var call = Assert.Single(result.Value.ToolCalls);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("get_contract", call.Name);
        Assert.Equal("""{"supplier":"Coupa"}""", call.ArgumentsJson);
        Assert.Equal(new AiTokenUsage(700, 40), result.Value.Metadata.Usage);
        Assert.Equal("agent-v1", result.Value.Metadata.PromptVersion);
    }

    [Fact]
    public async Task A_content_reply_is_the_final_json_and_tool_results_are_sent_back_in_order()
    {
        var (client, handler) = CreateClient(null, FakeHttpMessageHandler.Json(HttpStatusCode.OK, FinalEnvelope(new { answerMarkdown = "Coupa scade il 15 April 2026 [1].", citationKeys = new[] { "contract:coupa:end" } })));

        var result = await client.ConverseAsync(
            Request(false,
                AiConversationMessage.User("Quando scade Coupa?"),
                AiConversationMessage.AssistantToolCalls([new AiToolCall("call_1", "get_contract", """{"supplier":"Coupa"}""")]),
                AiConversationMessage.ToolResult("call_1", """{"end":"2026-04-15","citationKey":"contract:coupa:end"}""")),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.True(result.Value.IsFinal);
        Assert.Contains("citationKeys", result.Value.FinalJson);

        using var json = JsonDocument.Parse(handler.RequestBodies[0]!);
        var messages = json.RootElement.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(4, messages.Count);
        var assistant = messages[2];
        Assert.Equal("assistant", assistant.GetProperty("role").GetString());
        Assert.False(assistant.TryGetProperty("content", out _));
        Assert.Equal("call_1", assistant.GetProperty("tool_calls")[0].GetProperty("id").GetString());
        var tool = messages[3];
        Assert.Equal("tool", tool.GetProperty("role").GetString());
        Assert.Equal("call_1", tool.GetProperty("tool_call_id").GetString());
    }

    [Fact]
    public async Task Forcing_the_final_answer_sends_no_tools_and_no_tool_choice()
    {
        var (client, handler) = CreateClient(null, FakeHttpMessageHandler.Json(HttpStatusCode.OK, FinalEnvelope(new { answerMarkdown = "x", citationKeys = Array.Empty<string>() })));

        var result = await client.ConverseAsync(Request(forceFinal: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var json = JsonDocument.Parse(handler.RequestBodies[0]!);
        Assert.False(json.RootElement.TryGetProperty("tools", out _));
        Assert.False(json.RootElement.TryGetProperty("tool_choice", out _));
    }

    [Fact]
    public async Task The_agent_deployment_falls_back_to_the_answer_deployment_and_clamps_temperature()
    {
        var options = new AiGatewayModelOptions
        {
            Answer = new AiModelSelection("gpt-6-astra-demo", "2026-09-03") { Temperature = 0.9, MaxCompletionTokens = 8000 },
        };
        var (client, handler) = CreateClient(options, FakeHttpMessageHandler.Json(HttpStatusCode.OK, FinalEnvelope(new { answerMarkdown = "x", citationKeys = Array.Empty<string>() })));

        var result = await client.ConverseAsync(Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("gpt-6-astra-demo", result.Value.Metadata.ModelId);
        using var json = JsonDocument.Parse(handler.RequestBodies[0]!);
        Assert.Equal(FoundryChatCompletionsClient.MaxTemperature, json.RootElement.GetProperty("temperature").GetDouble());
        Assert.Equal(8000, json.RootElement.GetProperty("max_completion_tokens").GetInt32());
    }

    [Theory]
    [InlineData("length", "max_completion_tokens")]
    [InlineData("content_filter", "content filter")]
    public async Task A_cap_or_filter_stop_is_a_named_failure(string finishReason, string expected)
    {
        var (client, _) = CreateClient(null, FakeHttpMessageHandler.Json(HttpStatusCode.OK, FinalEnvelope(new { answerMarkdown = "x", citationKeys = Array.Empty<string>() }, finishReason)));

        var result = await client.ConverseAsync(Request(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains(expected, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tool_schema_azure_would_reject_fails_before_any_call()
    {
        var (client, handler) = CreateClient(null, FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var request = Request() with
        {
            Tools = [new AiToolDefinition("get_contract", "x", """{"type":"object","properties":{"supplier":{"type":"string"}}}""")],
        };

        var result = await client.ConverseAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("get_contract", result.Error, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }
}
