using Raffa.AiFlows.Ask.Answering;
using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.Tests.TestSupport;
using Raffa.AiGateway;
using Raffa.AiGateway.Agents;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Telemetry;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.Ask.Answering;

/// <summary>Plan A-05 (F1 Ask split PR-12): the answer role's calls as runner steps behind
/// <see cref="AskTurnOptions.AnswerOnRunner"/>. Off is the direct gateway call; on, the request that
/// reaches the gateway is the same and the call is audited under a run id and the step name.</summary>
public sealed class AnswerComposerOnRunnerTests
{
    private const string Question = "Where can I save the most this quarter?";

    private static readonly AiCallMetadata Metadata =
        new("scripted", "v1", AnswerPromptV2.Version, new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero), "00");

    private static AiAnswerResult Grounded() =>
        new(true, "Oracle is the biggest lever: EUR 15365 to EUR 30730 inside the window [1].", [], Metadata,
            "Oracle is the biggest lever: EUR 15365 to EUR 30730 inside the window [1].", ["calc:candidate[1]"], [], null, []);

    private static AiAnswerResult Fabricated() =>
        new(true, "You can save EUR 99999 with Oracle [1].", [], Metadata,
            "You can save EUR 99999 with Oracle [1].", ["calc:candidate[1]"], [], null, []);

    private static AiAnswerResult Decline() =>
        new(false, null, [], Metadata, null, [], [], "Not enough data.", []);

    /// <summary>What the gateway was asked, field by field (the request record compares its evidence list by reference).</summary>
    private static IEnumerable<(string Question, int Evidence, string? System, string? Pack, string? Version)> Shape(
        IEnumerable<AiAnswerRequest> requests) =>
        requests.Select(r => (r.Question, r.Evidence.Count, r.SystemPrompt, r.PackJson, r.PromptVersion));

    private static AnswerComposer Composer(IAiGateway gateway, bool onRunner) =>
        new(gateway, new StepRunner(gateway), new AskTurnOptions { AnswerOnRunner = onRunner });

    [Fact]
    public async Task The_flag_is_off_by_default_and_the_gateway_sees_no_step()
    {
        Assert.False(new AskTurnOptions().AnswerOnRunner);

        var gateway = new ScriptedGateway(Grounded());
        using var run = RunContext.BeginRun("run-off");
        var result = await new AnswerComposer(gateway, new StepRunner(gateway), new AskTurnOptions())
            .AnswerAsync(Question, ScreenshotSavingsPack.Build(), []);

        Assert.True(result.IsSuccess);
        Assert.Equal([null], gateway.Steps);
    }

    [Fact]
    public async Task On_the_request_is_identical_and_the_call_runs_in_the_answer_composer_step()
    {
        var pack = ScreenshotSavingsPack.Build();
        var off = new ScriptedGateway(Grounded());
        var on = new ScriptedGateway(Grounded());

        var offResult = await Composer(off, onRunner: false).AnswerAsync(Question, pack, []);
        using var run = RunContext.BeginRun("run-on");
        var onResult = await Composer(on, onRunner: true).AnswerAsync(Question, pack, []);

        Assert.Equal(off.Requests.Count, on.Requests.Count);
        Assert.Equal(Shape(off.Requests), Shape(on.Requests));
        Assert.Equal([AnswerComposerStepDefinition.Name], on.Steps);
        Assert.Equal(["run-on"], on.Runs);
        Assert.Equal(offResult.Value.Result.AnswerMarkdown, onResult.Value.Result.AnswerMarkdown);
        Assert.Equal(offResult.Value.GuardIntervened, onResult.Value.GuardIntervened);
    }

    [Fact]
    public async Task On_the_regenerate_once_retry_keeps_its_instruction_and_the_same_policy_outcome()
    {
        var pack = ScreenshotSavingsPack.Build();
        var off = new ScriptedGateway(Fabricated(), Fabricated());
        var on = new ScriptedGateway(Fabricated(), Fabricated());

        var offResult = await Composer(off, onRunner: false).AnswerAsync(Question, pack, []);
        var onResult = await Composer(on, onRunner: true).AnswerAsync(Question, pack, []);

        Assert.Equal(2, on.Requests.Count);
        Assert.Equal(Shape(off.Requests), Shape(on.Requests));
        Assert.Contains("rejected by an automated grounding check", on.Requests[1].SystemPrompt, StringComparison.Ordinal);
        Assert.Equal(offResult.Value.FallbackUsed, onResult.Value.FallbackUsed);
        Assert.Equal(offResult.Value.GuardViolation, onResult.Value.GuardViolation);
        Assert.Equal(offResult.Value.Result.AnswerMarkdown, onResult.Value.Result.AnswerMarkdown);
    }

    [Fact]
    public async Task On_a_decline_is_regenerated_once_exactly_as_off()
    {
        var pack = ScreenshotSavingsPack.Build();
        var off = new ScriptedGateway(Decline(), Grounded());
        var on = new ScriptedGateway(Decline(), Grounded());

        var offResult = await Composer(off, onRunner: false).AnswerAsync(Question, pack, []);
        var onResult = await Composer(on, onRunner: true).AnswerAsync(Question, pack, []);

        Assert.Equal(Shape(off.Requests), Shape(on.Requests));
        Assert.Equal(offResult.Value.Result.CanDetermine, onResult.Value.Result.CanDetermine);
    }

    [Fact]
    public async Task On_a_gateway_failure_is_the_same_failure_result()
    {
        var off = new ScriptedGateway();
        var on = new ScriptedGateway();

        var offResult = await Composer(off, onRunner: false).AnswerAsync(Question, ScreenshotSavingsPack.Build(), []);
        var onResult = await Composer(on, onRunner: true).AnswerAsync(Question, ScreenshotSavingsPack.Build(), []);

        Assert.True(offResult.IsFailure);
        Assert.True(onResult.IsFailure);
        Assert.Equal(offResult.Error, onResult.Error);
    }

    [Fact]
    public async Task On_a_caller_cancellation_still_propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var gateway = new ScriptedGateway(Grounded());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Composer(gateway, onRunner: true).AnswerAsync(Question, ScreenshotSavingsPack.Build(), [], null, cts.Token));
    }

    [Fact]
    public void The_step_definition_carries_the_persona_prompt_and_version_and_validates()
    {
        var definition = new AnswerComposerStep(AnswerPromptV2.SystemPrompt, TimeSpan.FromMinutes(10)).Definition;

        Assert.Equal(AgentRole.Answer, definition.Role);
        Assert.Equal(AnswerPromptV2.Version, definition.Version);
        Assert.Equal(AnswerPromptV2.SystemPrompt, definition.SystemPrompt);
        Assert.Empty(definition.Validate());
    }

    private sealed class ScriptedGateway(params AiAnswerResult[] results) : IAiGateway
    {
        private int calls;

        public List<AiAnswerRequest> Requests { get; } = [];

        public List<string?> Steps { get; } = [];

        public List<string?> Runs { get; } = [];

        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            Steps.Add(RunContext.Current?.StepName);
            Runs.Add(RunContext.Current?.RunId);
            var index = calls++;
            return Task.FromResult(index < results.Length
                ? Result<AiAnswerResult>.Success(results[index])
                : Result<AiAnswerResult>.Failure("scripted gateway: no more answers"));
        }

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
