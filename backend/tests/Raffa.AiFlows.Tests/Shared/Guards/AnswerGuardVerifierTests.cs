using Raffa.AiFlows.Ask.Guards;
using Raffa.AiFlows.Shared.Guards;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiGateway.Agents;
using Raffa.AiGateway.Contracts;

namespace Raffa.AiFlows.Tests.Shared.Guards;

/// <summary>Plan A-03: the guards as a runner verifier are a pure wrapper: same verdict, same first
/// violation, and Ask's own retry wording when the caller passes it.</summary>
public sealed class AnswerGuardVerifierTests
{
    private static readonly AiCallMetadata Metadata = new("m", "v1", "answer-v2.5", DateTimeOffset.UnixEpoch, "h");

    private static readonly PackItem[] Pack =
    [
        new("tenant:c1", PackCorpus.Tenant, "Contract", null, null, null, "Liability is capped at EUR 1000.",
            "/contracts/1", null, null, "validated contract",
            [new PackValue("cap", "1000", PackValueKind.Amount, "EUR")]),
    ];

    private static readonly AnswerStepInput Input = new("q", []);

    private static AiAnswerResult Answer(string markdown, params string[] keys) =>
        new(true, markdown, [], Metadata, markdown, keys, [], null, []);

    [Theory]
    [InlineData("The cap is EUR 1000 [1].", "tenant:c1")]
    [InlineData("The cap is EUR 9999 [1].", "tenant:c1")]
    [InlineData("The cap is EUR 1000 [1].", "foreign")]
    public void The_verdict_is_exactly_the_guards_own_in_the_composers_order(string markdown, string key)
    {
        var result = Answer(markdown, key);
        var grounding = GroundingGuard.Validate(result, Pack, allowUncitedGuidance: true);
        var expected = grounding.Passed ? NumericGuard.Validate(result.AnswerMarkdown, Pack, result.CitationKeys) : grounding;

        var verdict = new AnswerGuardVerifier(Pack).Verify(Input, result);

        Assert.Equal(expected.Passed, verdict.Ok);
        if (!expected.Passed)
        {
            Assert.Equal([expected.Violation!], verdict.Violations);
        }
    }

    [Fact]
    public void The_retry_instruction_is_the_callers_own_wording_naming_the_violation()
    {
        var verifier = new AnswerGuardVerifier(Pack, RegenerateOnce.BuildRetryInstruction);
        var verdict = verifier.Verify(Input, Answer("The cap is EUR 9999 [1].", "tenant:c1"));

        Assert.False(verdict.Ok);
        Assert.Equal(RegenerateOnce.BuildRetryInstruction(verdict.Violations[0]), verifier.RetryInstruction(verdict));
    }

    [Fact]
    public void Without_a_caller_wording_the_default_instruction_names_the_violation()
    {
        var verifier = new AnswerGuardVerifier(Pack);
        var verdict = verifier.Verify(Input, Answer("The cap is EUR 9999 [1].", "tenant:c1"));

        Assert.Contains(verdict.Violations[0], verifier.RetryInstruction(verdict), StringComparison.Ordinal);
    }

    [Fact]
    public void A_guard_verdict_maps_to_a_step_verdict()
    {
        Assert.Same(StepVerdict.Pass, GuardVerdict.Ok.ToStepVerdict());
        Assert.Equal(["why"], GuardVerdict.Fail("why").ToStepVerdict().Violations);
    }
}
