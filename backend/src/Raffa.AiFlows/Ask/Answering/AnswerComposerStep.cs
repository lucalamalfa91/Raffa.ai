using Raffa.AiGateway.Agents;
using Raffa.AiGateway.Contracts;

namespace Raffa.AiFlows.Ask.Answering;

// The answer role as a runner step (plan A-05, F1 Ask split PR-12; decision D8: prompt and behaviour
// unchanged). Each gateway call AnswerComposer used to make by hand is one step run: the request that
// reaches IAiGateway.AnswerAsync is the one it built before (same question, empty evidence, system
// prompt, pack JSON and prompt version), now audited under a run id and the step name "answer-composer".
// AnswerComposer keeps its own policy (guards, regenerate-once, the decline retry, the grounded fallback);
// the runner here is the call, not the policy, so there is no verifier on the step.

/// <summary>The answer composer's definition. The system prompt is a parameter because the one permitted
/// retry appends its instruction to the persona prompt, exactly as before.</summary>
internal static class AnswerComposerStepDefinition
{
    public const string Name = "answer-composer";

    public static AgentDefinition Create(string systemPrompt, TimeSpan deadline) => new(
        Name, AnswerPromptV2.Version, systemPrompt, string.Empty, deadline, AgentFailurePolicy.Fail, AgentRole.Answer);
}

/// <summary>The grounded answer call as an <see cref="IAgentStep{TIn,TOut}"/>.</summary>
internal sealed class AnswerComposerStep(string systemPrompt, TimeSpan deadline) : IAgentStep<AnswerStepInput, AiAnswerResult>
{
    public AgentDefinition Definition { get; } = AnswerComposerStepDefinition.Create(systemPrompt, deadline);
}
