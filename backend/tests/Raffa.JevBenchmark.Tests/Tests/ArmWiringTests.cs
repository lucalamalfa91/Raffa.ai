using System.Net;
using Raffa.AiFlows.CapabilityGaps.Investigation;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.JevBenchmark.Tests.Harness;
using static Raffa.JevBenchmark.Tests.Tests.Samples;

namespace Raffa.JevBenchmark.Tests.Tests;

/// <summary>
/// The arms, wired with a fake Foundry and a fake Jev transport (no socket anywhere): arm A must be
/// structurally unable to reach Jev, arm B must go through the real <c>JevAiGateway</c> /
/// <c>CapabilityInvestigator</c> Jev path and expose when it fell back to Foundry.
/// </summary>
public sealed class ArmWiringTests
{
    private static readonly AiGatewayResilienceOptions NoRetry = new() { MaxRetries = 0, RequestTimeoutSeconds = 5 };

    private static AiGatewayJevOptions JevConfigured() => new() { ApiKey = "test-key" };

    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    [Fact]
    public async Task Classify_baseline_uses_Foundry_only_and_B_is_served_by_Jev()
    {
        var clock = new FakeBenchClock();
        var foundry = new FakeFoundryGateway(clock, 100, _ => (AiDocumentType.Quote, 0.9));
        var transport = new FakeJevTransport(clock, 40, HttpStatusCode.OK, FakeJevTransport.ClassifyBody("Invoice", 0.95));
        var pair = ArmWiring.CreateClassifyPair(foundry, JevConfigured(), transport, NoRetry, clock, NoDelay);

        var a = await pair.Baseline.RunAsync(ClassifyCase(), 0, default);
        Assert.Equal(0, transport.Calls);
        Assert.Equal("Quote", a.Predicted);
        Assert.Equal(100, a.LatencyMs, 6);
        Assert.False(a.FellBack);
        Assert.Equal("foundry", a.ServedBy);
        var foundryCall = Assert.Single(a.Calls);
        Assert.Equal("foundry", foundryCall.Backend);
        Assert.Equal("classify", foundryCall.Operation);
        Assert.Equal(100, foundryCall.InputTokens);
        Assert.Equal(10, foundryCall.OutputTokens);
        Assert.Equal(100, foundryCall.LatencyMs, 6);

        var b = await pair.WithJev.RunAsync(ClassifyCase(), 0, default);
        Assert.Equal(1, transport.Calls);
        Assert.Equal(1, foundry.ClassifyCalls); // still only A's call: Jev answered, Foundry was not consulted
        Assert.Equal("Invoice", b.Predicted);
        Assert.Equal("jev", b.ServedBy);
        Assert.Equal(40, b.LatencyMs, 6);
        Assert.Null(b.FallbackReason);
        var jevCall = Assert.Single(b.Calls);
        Assert.True(jevCall.Usable);
        Assert.Equal(500, jevCall.InputTokens);
        Assert.Equal(0.0002, jevCall.CostUsd);
        Assert.True(b.Correct);
        Assert.False(a.Correct);
    }

    [Fact]
    public async Task Classify_falls_back_to_Foundry_and_records_why_when_Jev_errors()
    {
        var clock = new FakeBenchClock();
        var foundry = new FakeFoundryGateway(clock, 100, _ => (AiDocumentType.Invoice, 0.9));
        var transport = new FakeJevTransport(clock, 10, HttpStatusCode.ServiceUnavailable, "{}");
        var pair = ArmWiring.CreateClassifyPair(foundry, JevConfigured(), transport, NoRetry, clock, NoDelay);

        var b = await pair.WithJev.RunAsync(ClassifyCase(), 0, default);

        Assert.Equal("jev-error", b.FallbackReason);
        Assert.Equal("foundry", b.ServedBy);
        Assert.Equal("Invoice", b.Predicted);
        Assert.False(b.Error);
        Assert.Equal(110, b.LatencyMs, 6); // the failed Jev attempt is part of the price of the fallback
        Assert.Contains(b.Calls, c => c.Backend == "jev" && !c.Success && c.HttpStatus == 503);
        Assert.Contains(b.Calls, c => c.Backend == "foundry");
    }

    [Fact]
    public async Task Classify_below_the_confidence_gate_is_a_low_confidence_fallback()
    {
        var clock = new FakeBenchClock();
        var foundry = new FakeFoundryGateway(clock, 100, _ => (AiDocumentType.Quote, 0.9));
        var transport = new FakeJevTransport(clock, 40, HttpStatusCode.OK, FakeJevTransport.ClassifyBody("Invoice", 0.3));
        var pair = ArmWiring.CreateClassifyPair(foundry, JevConfigured(), transport, NoRetry, clock, NoDelay);

        var b = await pair.WithJev.RunAsync(ClassifyCase(expected: "Quote"), 0, default);

        Assert.Equal("low-confidence", b.FallbackReason);
        Assert.Equal("Quote", b.Predicted);
        Assert.Equal("foundry", b.ServedBy);
        Assert.Equal(140, b.LatencyMs, 6);
    }

    [Fact]
    public async Task A_failing_model_is_an_error_observation_not_an_exception()
    {
        var clock = new FakeBenchClock();
        var foundry = new FakeFoundryGateway(clock, 100, failWith: "Foundry request timed out after 180s");
        var pair = ArmWiring.CreateClassifyPair(
            foundry, JevConfigured(), new FakeJevTransport(clock, 1, HttpStatusCode.OK, "{}"), NoRetry, clock, NoDelay);

        var a = await pair.Baseline.RunAsync(ClassifyCase(), 0, default);

        Assert.True(a.Error);
        Assert.True(a.TimedOut);
        Assert.Null(a.Predicted);
        Assert.False(a.Correct);
        Assert.Contains("timed out", a.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Arm_B_options_force_Jev_on_and_arm_A_off_whatever_the_configured_value_is()
    {
        var configured = new AiGatewayJevOptions { Enabled = false, ApiKey = "k", Model = "typesafe/jev-x", ClassifyMinConfidence = 0.8, CheckOptionOrder = false };

        var on = ArmWiring.EnabledCopy(configured);
        var off = ArmWiring.DisabledCopy(new AiGatewayJevOptions { Enabled = true, ApiKey = "k" });

        Assert.True(on.Enabled);
        Assert.Equal(("typesafe/jev-x", 0.8, false, "k"), (on.Model, on.ClassifyMinConfidence, on.CheckOptionOrder, on.ApiKey));
        Assert.False(off.Enabled);
    }

    [Fact]
    public async Task Gap_baseline_decides_with_Foundry_alone_and_B_with_Jev_without_calling_Foundry_for_a_question()
    {
        var clock = new FakeBenchClock();
        var foundry = new FakeFoundryGateway(clock, 100, analyzePayload: GapPayload());
        var transport = new FakeJevTransport(clock, 40, HttpStatusCode.OK, FakeJevTransport.OperationBody(JevVerdictClient.OperationQuestion));
        var pair = ArmWiring.CreateGapPair(foundry, JevConfigured(), new GapInvestigationOptions(), transport, NoRetry, clock, NoDelay);

        var a = await pair.Baseline.RunAsync(GapCase(), 0, default);
        Assert.Equal("gap", a.Predicted);
        Assert.Equal(0, transport.Calls);
        Assert.Equal(1, foundry.AnalyzeCalls);
        Assert.True(a.Correct);

        var b = await pair.WithJev.RunAsync(GapCase(), 0, default);
        Assert.Equal("no-gap", b.Predicted);
        Assert.Equal(1, foundry.AnalyzeCalls); // unchanged: Jev decided "question" alone
        Assert.Equal(1, transport.Calls);
        Assert.Null(b.FallbackReason);
        Assert.False(b.Correct);
        Assert.Equal(40, b.LatencyMs, 6);
        Assert.All(b.Calls, c => Assert.Equal("jev", c.Backend));
    }

    [Fact]
    public async Task Gap_via_Jev_none_uses_Foundry_only_to_write_the_description()
    {
        var clock = new FakeBenchClock();
        var foundry = new FakeFoundryGateway(clock, 100, analyzePayload: GapPayload());
        var transport = new FakeJevTransport(clock, 40, HttpStatusCode.OK, FakeJevTransport.OperationBody(JevVerdictClient.OperationNone));
        var pair = ArmWiring.CreateGapPair(foundry, JevConfigured(), new GapInvestigationOptions(), transport, NoRetry, clock, NoDelay);

        var b = await pair.WithJev.RunAsync(GapCase(), 0, default);

        Assert.Equal("gap", b.Predicted);
        Assert.Equal(140, b.LatencyMs, 6);
        Assert.Null(b.FallbackReason);
        Assert.Contains(b.Calls, c => c.Backend == "jev" && c.Usable);
        Assert.Contains(b.Calls, c => c.Backend == "foundry" && c.Operation == "analyze");
    }

    [Fact]
    public async Task Gap_falls_back_to_the_Foundry_only_path_when_Jev_errors()
    {
        var clock = new FakeBenchClock();
        var foundry = new FakeFoundryGateway(clock, 100, analyzePayload: GapPayload());
        var transport = new FakeJevTransport(clock, 10, HttpStatusCode.TooManyRequests, "{}");
        var pair = ArmWiring.CreateGapPair(foundry, JevConfigured(), new GapInvestigationOptions(), transport, NoRetry, clock, NoDelay);

        var b = await pair.WithJev.RunAsync(GapCase(), 0, default);

        Assert.Equal("jev-error", b.FallbackReason);
        Assert.Equal("gap", b.Predicted);
        Assert.False(b.Error);
        Assert.Contains(b.Calls, c => c.Backend == "jev" && !c.Success);
    }

    [Fact]
    public async Task Gap_not_cleared_by_the_confidence_bar_counts_as_no_gap_and_a_failed_investigation_as_an_error()
    {
        var clock = new FakeBenchClock();
        var lowConfidence = ArmWiring.CreateGapPair(
            new FakeFoundryGateway(clock, 100, analyzePayload: GapPayload()),
            JevConfigured(),
            new GapInvestigationOptions(),
            new FakeJevTransport(clock, 5, HttpStatusCode.OK, FakeJevTransport.OperationBody(JevVerdictClient.OperationNone, confidence: 0.2)),
            NoRetry, clock, NoDelay);
        var failing = ArmWiring.CreateGapPair(
            new FakeFoundryGateway(clock, 100, failWith: "boom"),
            JevConfigured(),
            new GapInvestigationOptions(),
            new FakeJevTransport(clock, 5, HttpStatusCode.OK, "{}"),
            NoRetry, clock, NoDelay);

        var low = await lowConfidence.WithJev.RunAsync(GapCase(), 0, default);
        var failed = await failing.Baseline.RunAsync(GapCase(), 0, default);

        Assert.Equal("no-gap", low.Predicted);
        Assert.Equal("low-confidence", low.Outcome);
        Assert.True(failed.Error);
        Assert.Null(failed.Predicted);
    }

    [Fact]
    public async Task The_baseline_arm_cannot_reach_Jev_even_by_mistake()
    {
        var clock = new FakeBenchClock();
        var pair = ArmWiring.CreateGapPair(
            new FakeFoundryGateway(clock, 100, analyzePayload: QuestionPayload()),
            JevConfigured(),
            new GapInvestigationOptions(),
            new FakeJevTransport(clock, 1, HttpStatusCode.OK, "{}"),
            NoRetry, clock, NoDelay);

        var a = await pair.Baseline.RunAsync(GapCase(), 0, default);

        Assert.Equal("no-gap", a.Predicted);
        Assert.DoesNotContain(a.Calls, c => c.Backend == "jev");
        Assert.False(pair.Baseline.JevEnabled);
        Assert.True(pair.WithJev.JevEnabled);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new HttpClient(new ForbiddenJevTransport()).GetAsync("https://example.invalid/"));
    }
}
