using System.Text.Json;
using Raffa.AiFlows.Playground;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.Playground;

/// <summary>The playground judge: blind labels, one or two passes, validated scores, preference from the weighted scores.</summary>
public sealed class PlaygroundJudgeTests
{
    private static readonly JudgeCriterion[] Criteria =
    [
        new("grounding", "Grounding", "From the evidence", 4),
        new("tone", "Tone", "Direct", 1),
    ];

    private static readonly JudgeAnswer[] Answers =
    [
        new("run-1", "First answer", "evidence 1", null),
        new("run-2", "Second answer", "evidence 2", null),
    ];

    private static AiCallMetadata Meta() => new("gpt-test", "1", PlaygroundJudge.Version, DateTimeOffset.UtcNow, "hash");

    /// <summary>Answers each judge call with whatever the script returns for the labels it saw, in the order shown.</summary>
    private sealed class ScriptedGateway(Func<IReadOnlyList<string>, int, string> script) : IAiGateway
    {
        public List<string> Requests { get; } = [];

        private int _calls;

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request.InputJson);
            using var input = JsonDocument.Parse(request.InputJson);
            var contents = input.RootElement.GetProperty("answers").EnumerateArray().Select(a => a.GetProperty("content").GetString()!).ToList();
            var call = Interlocked.Increment(ref _calls) - 1;
            return Task.FromResult(Result<AiAnalysisResult>.Success(new AiAnalysisResult(script(contents, call), Meta())));
        }

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>A verdict giving each shown answer the score picked by its content ("First answer" etc.).</summary>
    private static string Verdict(IReadOnlyList<string> shown, Func<string, int> grounding, Func<string, int> tone, string? preferred = null, int? scoreOverride = null)
    {
        var answers = shown.Select((content, i) => new
        {
            label = ((char)('A' + i)).ToString(),
            scores = new object[]
            {
                new { criterionId = "grounding", score = scoreOverride ?? grounding(content), rationale = "r1" },
                new { criterionId = "tone", score = scoreOverride ?? tone(content), rationale = "r2" },
            },
            summary = "s",
        });
        var best = preferred ?? ((char)('A' + shown.ToList().IndexOf(shown.OrderByDescending(grounding).First()))).ToString();
        return JsonSerializer.Serialize(new { answers, preferred = best, reasoning = "because" }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    [Fact]
    public async Task The_judge_sees_anonymous_labels_and_the_preference_follows_the_weighted_scores()
    {
        var gateway = new ScriptedGateway((shown, _) => Verdict(shown, c => c == "First answer" ? 5 : 2, _ => 3));
        var judge = new PlaygroundJudge(gateway);

        var result = await judge.JudgeAsync(new JudgeRequest("ask", "Question: x", Criteria, Answers, Seed: 1), CancellationToken.None);

        Assert.Empty(result.Failures);
        Assert.Equal("run-1", result.PreferredAnswerId);
        Assert.False(result.Tie);
        var first = result.Answers.Single(a => a.AnswerId == "run-1");
        var second = result.Answers.Single(a => a.AnswerId == "run-2");
        Assert.Equal(4.6, first.Weighted);   // (4*5 + 1*3) / 5
        Assert.Equal(2.2, second.Weighted);  // (4*2 + 1*3) / 5
        Assert.Equal(new[] { "grounding", "tone" }, first.Scores.Select(s => s.CriterionId).ToArray());
        // Labels only: no answer id reaches the judge.
        Assert.DoesNotContain("run-1", gateway.Requests.Single(), StringComparison.Ordinal);
        Assert.Contains("\"label\":\"A\"", gateway.Requests.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_passes_show_the_answers_in_reverse_order_and_average_the_scores()
    {
        var gateway = new ScriptedGateway((shown, call) => Verdict(shown, c => c == "First answer" ? (call == 0 ? 5 : 3) : 2, _ => 3));
        var judge = new PlaygroundJudge(gateway);

        var result = await judge.JudgeAsync(new JudgeRequest("ask", "Question: x", Criteria, Answers, Passes: 2, Seed: 7), CancellationToken.None);

        Assert.Equal(2, result.Passes.Count);
        Assert.Equal(result.Passes[0].Order.Reverse().ToArray(), result.Passes[1].Order.ToArray());
        // First answer grounding: mean(5, 3) = 4; tone 3 -> (4*4 + 3) / 5 = 3.8.
        Assert.Equal(3.8, result.Answers.Single(a => a.AnswerId == "run-1").Weighted);
        Assert.Equal("run-1", result.PreferredAnswerId);
        Assert.True(result.PassesAgreed);
    }

    [Fact]
    public async Task A_judge_that_flips_with_the_order_is_reported_as_not_agreeing()
    {
        // It always prefers whichever answer is shown first.
        var gateway = new ScriptedGateway((shown, _) => Verdict(shown, _ => 3, _ => 3, preferred: "A"));
        var judge = new PlaygroundJudge(gateway);

        var result = await judge.JudgeAsync(new JudgeRequest("ask", "Question: x", Criteria, Answers, Passes: 2, Seed: 3), CancellationToken.None);

        Assert.False(result.PassesAgreed);
        Assert.True(result.Tie);
        Assert.Null(result.PreferredAnswerId);
    }

    [Fact]
    public async Task Scores_outside_one_to_five_or_a_missing_criterion_make_the_pass_fail()
    {
        var outOfRange = new PlaygroundJudge(new ScriptedGateway((shown, _) => Verdict(shown, _ => 3, _ => 3, scoreOverride: 9)));
        var failed = await outOfRange.JudgeAsync(new JudgeRequest("ask", "Question: x", Criteria, Answers), CancellationToken.None);
        Assert.Empty(failed.Answers);
        Assert.Contains("outside 1-5", Assert.Single(failed.Failures));

        var missing = new PlaygroundJudge(new ScriptedGateway((_, _) => """{"answers":[{"label":"A","scores":[],"summary":"s"}],"preferred":"A","reasoning":"r"}"""));
        var noScores = await missing.JudgeAsync(new JudgeRequest("ask", "Question: x", Criteria, Answers), CancellationToken.None);
        Assert.Empty(noScores.Answers);
        Assert.NotEmpty(noScores.Failures);

        var notJson = new PlaygroundJudge(new ScriptedGateway((_, _) => "not json"));
        Assert.NotEmpty((await notJson.JudgeAsync(new JudgeRequest("ask", "Question: x", Criteria, Answers), CancellationToken.None)).Failures);
    }

    [Fact]
    public async Task One_failed_pass_of_two_still_yields_a_result_from_the_other()
    {
        var gateway = new ScriptedGateway((shown, call) => call == 0 ? Verdict(shown, _ => 4, _ => 4) : "garbage");
        var judge = new PlaygroundJudge(gateway);

        var result = await judge.JudgeAsync(new JudgeRequest("ask", "Question: x", Criteria, Answers, Passes: 2, Seed: 5), CancellationToken.None);

        Assert.Equal(2, result.Answers.Count);
        Assert.Single(result.Failures);
        Assert.Single(result.Passes);
    }

    [Fact]
    public void The_fixture_judge_double_and_the_judge_share_the_agent_name_and_every_workflow_has_a_default_rubric()
    {
        Assert.Equal(PlaygroundJudge.AgentName, FixtureAiGateway.PlaygroundJudgeAgentName);
        Assert.Equal(PlaygroundRubrics.Workflows.Order().ToArray(), PlaygroundRubrics.Defaults.Select(r => r.Workflow).Order().ToArray());
        Assert.All(PlaygroundRubrics.Defaults, r =>
        {
            Assert.Equal(PlaygroundRubrics.DefaultPrefix + r.Workflow, r.Id);
            Assert.All(r.Criteria, c => Assert.InRange(c.Weight, 1, 5));
            Assert.Equal(r.Criteria.Count, r.Criteria.Select(c => c.Id).Distinct().Count());
        });
    }
}
