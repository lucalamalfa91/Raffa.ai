using Raffa.AiFlows.Shared.Pack;
using Raffa.AiGateway.Agents;
using Raffa.AiGateway.Contracts;

namespace Raffa.AiFlows.Shared.Guards;

/// <summary>
/// Plan A-03: <see cref="GroundingGuard"/> and <see cref="NumericGuard"/> as a runner verifier, a pure
/// wrapper. It applies exactly the order and arguments Ask's answer composer uses (grounding with
/// <c>allowUncitedGuidance</c>, then the numeric guard over the markdown and the returned citation
/// keys) and reports the guards' own first violation unchanged; the guards are not modified. The
/// retry instruction is supplied by the caller (Ask passes its own <c>RegenerateOnce</c> wording), so
/// no flow's text moves.
/// </summary>
public sealed class AnswerGuardVerifier(
    IReadOnlyList<PackItem> pack,
    Func<string, string>? retryInstruction = null,
    string? language = null,
    bool allowUncitedGuidance = true) : IStepVerifier<AnswerStepInput, AiAnswerResult>
{
    public string Name => "answer-guards";

    public StepVerdict Verify(AnswerStepInput input, AiAnswerResult output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return Check(output).ToStepVerdict();
    }

    public string RetryInstruction(StepVerdict verdict) =>
        retryInstruction is not null
            ? retryInstruction(verdict.Violations[0])
            : string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                AgentDefinition.DefaultRetryInstruction,
                string.Join(' ', verdict.Violations));

    /// <summary>The guards' verdict itself, for a caller that wants the original type.</summary>
    public GuardVerdict Check(AiAnswerResult result)
    {
        var grounding = GroundingGuard.Validate(result, pack, allowUncitedGuidance);
        return grounding.Passed
            ? NumericGuard.Validate(result.AnswerMarkdown, pack, result.CitationKeys, language)
            : grounding;
    }
}

/// <summary>Maps the guards' verdict onto the runner's.</summary>
public static class GuardVerdictExtensions
{
    public static StepVerdict ToStepVerdict(this GuardVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        return verdict.Passed ? StepVerdict.Pass : StepVerdict.Reject(verdict.Violation ?? "The output was rejected.");
    }
}
