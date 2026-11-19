using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.AiGateway.Logging;
using Raffa.AiGateway.Telemetry;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Tests;

/// <summary>The playground's trace recorder: silent without a <see cref="PlaygroundTrace"/>, a full record with one.</summary>
public sealed class TraceRecordingAiGatewayTests
{
    private static TraceRecordingAiGateway Gateway() => new(new FixtureAiGateway(
        new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions(), new AskAgentFixtureScript()));

    private static AiConversationRequest Request() => new(
        "ask-orchestrator", "SYSTEM PROMPT TEXT",
        [AiConversationMessage.User("Ciao!")],
        [new AiToolDefinition("list_portfolio", "Lists the portfolio.", """{"type":"object","properties":{}}""")],
        """{"type":"object"}""", "agent-test");

    [Fact]
    public async Task Without_a_trace_nothing_is_recorded_and_the_answer_is_unchanged()
    {
        Assert.Null(PlaygroundTrace.Current);

        var result = await Gateway().ConverseAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Null(PlaygroundTrace.Current);
    }

    [Fact]
    public async Task With_a_trace_the_exact_request_and_the_raw_answer_are_recorded()
    {
        using var scope = PlaygroundTrace.Begin(out var trace);

        var result = await Gateway().ConverseAsync(Request());

        Assert.True(result.IsSuccess);
        var call = Assert.Single(trace.Events);
        Assert.Equal("model_call", call.Kind);
        Assert.Equal("converse:ask-orchestrator", call.Name);
        Assert.True(call.Ok);
        Assert.Contains("SYSTEM PROMPT TEXT", call.Input, StringComparison.Ordinal);
        Assert.Contains("Ciao!", call.Input, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(call.Output));
    }

    [Fact]
    public void A_trace_ends_with_its_scope_and_oversized_payloads_are_truncated()
    {
        PlaygroundTraceCollector collector;
        using (PlaygroundTrace.Begin(out collector))
        {
            PlaygroundTrace.Record("tool_call", "big", output: new string('x', PlaygroundTraceCollector.MaxPayloadChars + 500));
            Assert.Same(collector, PlaygroundTrace.Current);
        }

        Assert.Null(PlaygroundTrace.Current);
        var recorded = Assert.Single(collector.Events);
        Assert.EndsWith("…[truncated]", recorded.Output, StringComparison.Ordinal);
        Assert.Equal(1, recorded.Seq);
    }
}
