using System.Diagnostics.Metrics;
using Raffa.AiGateway.Agents;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Telemetry;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Tests.Agents;

/// <summary>Plan A-04 (runner part): an opt-in flow deadline and an explicit "skipped: deadline"
/// degrade. With neither, behaviour is exactly the per-step deadline of old.</summary>
public sealed class StepRunnerDeadlineTests
{
    private static ScriptedGateway Hung() => new(analyze: async (_, token) =>
    {
        await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
        return Result<AiAnalysisResult>.Failure("unreachable");
    });

    private static AgentDefinition Def(string name, AgentFailurePolicy policy, TimeSpan deadline, bool skip) =>
        AgentDefinition.ForOutput<EchoOutput>(
            name, "echo-v1", "You are a test agent. Respond with strict JSON only.", deadline, policy, skipOnDeadline: skip);

    [Fact]
    public async Task Without_the_option_a_missed_step_deadline_degrades_by_the_policy_as_before()
    {
        var step = AgentTestKit.Step(Def("dl-default", AgentFailurePolicy.Fail, TimeSpan.FromMilliseconds(80), skip: false));

        var result = await new StepRunner(Hung()).RunAsync(step, AgentTestKit.Input);

        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Equal(StepFailureReason.DeadlineExceeded, result.Reason);
        Assert.DoesNotContain("skipped", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AgentFailurePolicy.Fail)]
    [InlineData(AgentFailurePolicy.Skip)]
    [InlineData(AgentFailurePolicy.Fallback)]
    public async Task With_skip_on_deadline_a_missed_step_deadline_is_an_explicit_skipped_whatever_the_policy(AgentFailurePolicy policy)
    {
        var step = AgentTestKit.Step(
            Def("dl-skip", policy, TimeSpan.FromMilliseconds(80), skip: true),
            fallback: i => new EchoOutput("template", i.Keys));

        var result = await new StepRunner(Hung()).RunAsync(step, AgentTestKit.Input);

        Assert.Equal(StepStatus.Skipped, result.Status);
        Assert.False(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal(StepFailureReason.DeadlineExceeded, result.Reason);
        Assert.StartsWith("skipped: deadline of", result.Error, StringComparison.Ordinal);
        Assert.Equal("dl-skip: skipped: deadline of 0.08s exceeded.", result.Describe());
    }

    [Fact]
    public async Task A_flow_deadline_bounds_a_step_whose_own_deadline_is_longer()
    {
        var runner = new StepRunner(Hung());
        var step = AgentTestKit.Step(Def("dl-flow", AgentFailurePolicy.Fail, TimeSpan.FromSeconds(30), skip: true));

        using var flow = runner.BeginFlow(TimeSpan.FromMilliseconds(100));
        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = await runner.RunAsync(step, AgentTestKit.Input);

        Assert.True(started.Elapsed < TimeSpan.FromSeconds(10));
        Assert.Equal(StepStatus.Skipped, result.Status);
        Assert.Equal(StepFailureReason.FlowDeadlineExceeded, result.Reason);
        Assert.Equal("skipped: flow deadline exceeded before dl-flow finished.", result.Error);
    }

    [Fact]
    public async Task A_step_that_starts_after_the_flow_budget_ran_out_is_not_called_at_all()
    {
        var gateway = new ScriptedGateway();
        var runner = new StepRunner(gateway);
        var step = AgentTestKit.Step(Def("dl-late", AgentFailurePolicy.Skip, TimeSpan.FromSeconds(5), skip: false));

        using var flow = runner.BeginFlow(TimeSpan.FromMilliseconds(30));
        await Task.Delay(120);
        var result = await runner.RunAsync(step, AgentTestKit.Input);

        Assert.Empty(gateway.AnalyzeRequests);
        Assert.Equal(StepStatus.Skipped, result.Status);
        Assert.Equal(StepFailureReason.FlowDeadlineExceeded, result.Reason);
        Assert.Equal(0, result.Attempts);
    }

    [Fact]
    public async Task A_flow_budget_with_time_left_changes_nothing_and_leaving_it_removes_it()
    {
        var gateway = new ScriptedGateway();
        var runner = new StepRunner(gateway);

        using (runner.BeginFlow(TimeSpan.FromMinutes(5)))
        {
            Assert.NotNull(RunContext.FlowRemaining);
            var ok = await runner.RunAsync(AgentTestKit.Step(), AgentTestKit.Input);
            Assert.Equal(StepStatus.Succeeded, ok.Status);
        }

        Assert.Null(RunContext.FlowRemaining);
    }

    [Fact]
    public void A_nested_flow_budget_can_only_tighten_the_outer_one()
    {
        using var outer = RunContext.BeginFlowDeadline(TimeSpan.FromSeconds(10));
        using (RunContext.BeginFlowDeadline(TimeSpan.FromHours(1)))
        {
            Assert.True(RunContext.FlowRemaining < TimeSpan.FromSeconds(11));
        }

        using (RunContext.BeginFlowDeadline(TimeSpan.FromSeconds(1)))
        {
            Assert.True(RunContext.FlowRemaining < TimeSpan.FromSeconds(2));
        }

        Assert.True(RunContext.FlowRemaining > TimeSpan.FromSeconds(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => RunContext.BeginFlowDeadline(TimeSpan.Zero));
    }

    [Fact]
    public async Task No_flow_deadline_exists_unless_asked_for()
    {
        Assert.Null(RunContext.FlowRemaining);
        Assert.Null(RunContext.Current?.FlowDeadlineTimestamp);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_missed_deadline_is_counted_per_agent_and_scope()
    {
        var seen = new List<(string Agent, string Scope)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == AgentMetrics.MeterName && instrument.Name == AgentMetrics.DeadlineExceededName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            string agent = "", scope = "";
            foreach (var tag in tags)
            {
                if (tag.Key == AgentMetrics.TagAgent)
                {
                    agent = tag.Value?.ToString() ?? "";
                }
                else if (tag.Key == AgentMetrics.TagScope)
                {
                    scope = tag.Value?.ToString() ?? "";
                }
            }

            lock (seen)
            {
                seen.Add((agent, scope));
            }
        });
        listener.Start();

        var runner = new StepRunner(Hung());
        await runner.RunAsync(
            AgentTestKit.Step(Def("dl-metric-step", AgentFailurePolicy.Skip, TimeSpan.FromMilliseconds(50), skip: false)), AgentTestKit.Input);
        using (runner.BeginFlow(TimeSpan.FromMilliseconds(50)))
        {
            await runner.RunAsync(
                AgentTestKit.Step(Def("dl-metric-flow", AgentFailurePolicy.Skip, TimeSpan.FromSeconds(30), skip: false)), AgentTestKit.Input);
        }

        lock (seen)
        {
            Assert.Contains(("dl-metric-step", "step"), seen);
            Assert.Contains(("dl-metric-flow", "flow"), seen);
        }
    }
}
