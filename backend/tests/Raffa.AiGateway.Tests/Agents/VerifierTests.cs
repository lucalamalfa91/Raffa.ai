using System.Diagnostics.Metrics;
using Raffa.AiGateway.Agents;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Telemetry;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Tests.Agents;

/// <summary>Plan A-03: reusable verifiers, the regenerate-once helper and the per-agent
/// "verifier intervened" metric.</summary>
public sealed class VerifierTests
{
    private sealed class NoEmptyTitle : IVerifier<EchoOutput>
    {
        public string Name => "no-empty-title";

        public StepVerdict Verify(EchoOutput output) =>
            string.IsNullOrWhiteSpace(output.Title) ? StepVerdict.Reject("the title is empty.") : StepVerdict.Pass;
    }

    private static readonly IStepVerifier<EchoInput, EchoOutput> KeysKnown = Verifier.From<EchoInput, EchoOutput>(
        "keys-known",
        (input, output) => output.CitationKeys.Where(k => !input.Keys.Contains(k)).ToArray() is { Length: > 0 } bad
            ? StepVerdict.Reject(bad.Select(k => $"key {k} is not in the input.").ToArray())
            : StepVerdict.Pass);

    [Fact]
    public void An_output_only_verifier_lifts_to_the_step_seam()
    {
        var verifier = Verifier.ForStep<EchoInput, EchoOutput>(new NoEmptyTitle());

        Assert.Equal("no-empty-title", verifier.Name);
        Assert.True(verifier.Verify(AgentTestKit.Input, new EchoOutput("t", [])).Ok);
        var verdict = verifier.Verify(AgentTestKit.Input, new EchoOutput(" ", []));
        Assert.Equal(["the title is empty."], verdict.Violations);
        Assert.Contains("the title is empty.", verifier.RetryInstruction(verdict), StringComparison.Ordinal);
    }

    [Fact]
    public void AllOf_collects_every_violation_or_stops_at_the_first()
    {
        var empty = Verifier.ForStep<EchoInput, EchoOutput>(new NoEmptyTitle());
        var bad = new EchoOutput("", ["x", "y"]);

        var all = Verifier.AllOf("both", false, null, empty, KeysKnown).Verify(AgentTestKit.Input, bad);
        var first = Verifier.AllOf("both", true, null, empty, KeysKnown).Verify(AgentTestKit.Input, bad);
        var pass = Verifier.AllOf("both", true, null, empty, KeysKnown).Verify(AgentTestKit.Input, new EchoOutput("t", ["fact:1"]));

        Assert.Equal(["the title is empty.", "key x is not in the input.", "key y is not in the input."], all.Violations);
        Assert.Equal(["the title is empty."], first.Violations);
        Assert.True(pass.Ok);
    }

    [Fact]
    public void AllOf_uses_the_given_retry_wording()
    {
        var composite = Verifier.AllOf("c", true, v => "FIX: " + v.Violations[0], KeysKnown);

        Assert.Equal("FIX: a", composite.RetryInstruction(StepVerdict.Reject("a")));
    }

    [Fact]
    public async Task A_step_with_a_composed_verifier_retries_once_with_the_violation_named()
    {
        var calls = 0;
        var gateway = new ScriptedGateway(analyze: (r, _) =>
        {
            var payload = Interlocked.Increment(ref calls) == 1 ? AgentTestKit.Payload("t", "ghost") : AgentTestKit.Payload("t", "fact:1");
            return Task.FromResult(Result<AiAnalysisResult>.Success(new AiAnalysisResult(payload, AgentTestKit.Meta(r.PromptVersion))));
        });

        var result = await new StepRunner(gateway).RunAsync(AgentTestKit.Step(verifier: KeysKnown), AgentTestKit.Input);

        Assert.Equal(StepStatus.Succeeded, result.Status);
        Assert.True(result.VerifierIntervened);
        Assert.Contains("key ghost is not in the input.", gateway.AnalyzeRequests[1].SystemPrompt, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- VerifiedRegeneration

    [Fact]
    public async Task Regenerate_once_accepts_a_clean_first_attempt_without_a_retry()
    {
        var instructions = new List<string?>();

        var outcome = await VerifiedRegeneration.RunAsync(
            (instruction, _) => { instructions.Add(instruction); return Task.FromResult("fine"); },
            _ => StepVerdict.Pass, v => "unused", "agent-x", "v");

        Assert.True(outcome.Accepted);
        Assert.Equal(1, outcome.Attempts);
        Assert.False(outcome.Intervened);
        Assert.Equal([(string?)null], instructions);
    }

    [Fact]
    public async Task Regenerate_once_retries_exactly_once_naming_the_violation_then_stops_whatever_the_verdict()
    {
        var instructions = new List<string?>();

        var outcome = await VerifiedRegeneration.RunAsync(
            (instruction, _) => { instructions.Add(instruction); return Task.FromResult("bad" + instructions.Count); },
            _ => StepVerdict.Reject("number 9 is not in the pack."),
            v => "Fix: " + v.Violations[0], "agent-x", "v");

        Assert.False(outcome.Accepted);
        Assert.Equal("bad2", outcome.Value);
        Assert.Equal(2, outcome.Attempts);
        Assert.True(outcome.Intervened);
        Assert.Equal([null, "Fix: number 9 is not in the pack."], instructions);
    }

    [Fact]
    public async Task Regenerate_once_accepts_a_second_attempt_that_passes()
    {
        var n = 0;

        var outcome = await VerifiedRegeneration.RunAsync(
            (_, _) => Task.FromResult(++n),
            value => value == 2 ? StepVerdict.Pass : StepVerdict.Reject("odd"),
            _ => "again", "agent-x", "v");

        Assert.True(outcome.Accepted);
        Assert.Equal(2, outcome.Value);
        Assert.True(outcome.Intervened);
    }

    // ---------------------------------------------------------------- metric

    private sealed class Capture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Agent, string Verifier, string Outcome)> _seen = [];

        public Capture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == AgentMetrics.MeterName && instrument.Name == AgentMetrics.VerifierInterventionsName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                string agent = "", verifier = "", outcome = "";
                foreach (var tag in tags)
                {
                    switch (tag.Key)
                    {
                        case AgentMetrics.TagAgent: agent = tag.Value?.ToString() ?? ""; break;
                        case AgentMetrics.TagVerifier: verifier = tag.Value?.ToString() ?? ""; break;
                        case AgentMetrics.TagOutcome: outcome = tag.Value?.ToString() ?? ""; break;
                    }
                }

                lock (_seen)
                {
                    _seen.Add((agent, verifier, outcome));
                }
            });
            _listener.Start();
        }

        public List<(string Agent, string Verifier, string Outcome)> For(string agent)
        {
            lock (_seen)
            {
                return _seen.Where(s => s.Agent == agent).ToList();
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    [Fact]
    public async Task The_runner_counts_each_verifier_intervention_per_agent_with_its_outcome()
    {
        using var capture = new Capture();
        var definition = AgentTestKit.Definition("metric-agent-retry");
        var always = new RejectingVerifier(rejections: 5);

        await new StepRunner(new ScriptedGateway()).RunAsync(AgentTestKit.Step(definition, new RejectingVerifier(1)), AgentTestKit.Input);
        await new StepRunner(new ScriptedGateway()).RunAsync(
            AgentTestKit.Step(AgentTestKit.Definition("metric-agent-reject"), always), AgentTestKit.Input);

        Assert.Equal([("metric-agent-retry", "reject", "retried")], capture.For("metric-agent-retry"));
        Assert.Equal(
            [("metric-agent-reject", "reject", "retried"), ("metric-agent-reject", "reject", "rejected")],
            capture.For("metric-agent-reject"));
    }

    [Fact]
    public async Task A_passing_verifier_records_nothing_and_the_helper_records_its_interventions()
    {
        using var capture = new Capture();

        await new StepRunner(new ScriptedGateway()).RunAsync(
            AgentTestKit.Step(AgentTestKit.Definition("metric-agent-clean"), new RejectingVerifier(0)), AgentTestKit.Input);
        await VerifiedRegeneration.RunAsync(
            (_, _) => Task.FromResult(1), _ => StepVerdict.Reject("no"), _ => "x", "metric-agent-helper", "guards");

        Assert.Empty(capture.For("metric-agent-clean"));
        Assert.Equal(
            [("metric-agent-helper", "guards", "retried"), ("metric-agent-helper", "guards", "rejected")],
            capture.For("metric-agent-helper"));
    }
}
