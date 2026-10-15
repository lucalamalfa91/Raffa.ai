using Raffa.AiFlows.CapabilityGaps.Agents;
using Raffa.AiFlows.CapabilityGaps.Guards;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiGateway.Agents;

namespace Raffa.AiFlows.CapabilityGaps.Drafting;

// The drafting workflow's two agents as runner steps (plan A-08, same shape as
// Negotiation/Agents/CouncilSteps.cs). The prompts, the version (draft-v1), the agent names and the
// hand-written strict schemas are the DraftingAgents constants exactly as before, so the requests
// that reach IAiGateway are byte-identical to the ones NegotiationDraftingWorkflow built by hand; the
// wire records below mirror the old private DTOs property for property (the runner serializes with
// the same options, so InputJson is identical).

internal sealed record DraftValue(string Key, string Value, string Kind, string? Currency);

internal sealed record DraftItem(
    string CitationKey, string Corpus, string Title, string? Subtitle, string Snippet, IReadOnlyList<DraftValue> Values);

internal sealed record DraftGoal(decimal? TargetAmount, decimal? TargetPercent, string? Currency, int? WindowDays, string? WindowLabel);

internal sealed record DraftPlannerInput(string Question, string Language, string Supplier, DraftGoal? Goal, IReadOnlyList<DraftItem> Items);

internal sealed record DraftWriterInput(
    string Question, string Language, string Supplier, DraftGoal? Goal, DraftPlan Plan, IReadOnlyList<DraftItem> Items);

internal sealed record DraftPlannerAsk(string? Lever, string? Sentence, IReadOnlyList<string>? CitationKeys);

internal sealed record DraftPlannerPayload(
    string? Position, IReadOnlyList<DraftPlannerAsk>? Asks, string? Trade, string? DeadlineAnchor, string? Closing);

internal sealed record DraftWriterPayload(string? Subject, string? Body, IReadOnlyList<string>? UsedCitationKeys);

/// <summary>The two drafting definitions. The planner is optional (<see cref="AgentFailurePolicy.Skip"/>:
/// a failed planner degrades to <see cref="DraftPlan.FromPack"/>, never the draft); the writer is
/// required of the model path (<see cref="AgentFailurePolicy.Fail"/>: the workflow then writes the
/// deterministic <see cref="NegotiationEmailTemplate"/> itself). The workflow had no deadline of its
/// own (only the gateway's per-call timeout and retries), so the deadline is a backstop well above
/// that, <see cref="DraftingOptions.AgentDeadline"/>.</summary>
internal static class DraftingStepDefinitions
{
    // SHA-256 of each prompt (AgentDefinition.HashPrompt), registered next to the version: a prompt
    // edited without a version bump fails the definition's Validate() in a test.
    private const string OfferPlannerHash = "8c4ecbb59747f9ae14ebec7fac3112667e115df0f8dedd21e9fa375b478f7b62";
    private const string NegotiationWriterHash = "3b8c8ddfba17adca8d2dd7ab54c9bb23b4080e71ba8b143ee3f1ae0c697255ad";

    public static AgentDefinition OfferPlanner(TimeSpan deadline) => new(
        DraftingAgents.OfferPlannerName, DraftingAgents.Version, DraftingAgents.OfferPlannerPrompt,
        DraftingAgents.OfferPlanSchema, deadline, AgentFailurePolicy.Skip,
        registeredPromptHash: OfferPlannerHash);

    public static AgentDefinition NegotiationWriter(TimeSpan deadline) => new(
        DraftingAgents.NegotiationWriterName, DraftingAgents.Version, DraftingAgents.NegotiationWriterPrompt,
        DraftingAgents.EmailDraftSchema, deadline, AgentFailurePolicy.Fail,
        registeredPromptHash: NegotiationWriterHash);
}

/// <summary>Round one: the offer planner.</summary>
internal sealed class OfferPlannerStep(TimeSpan deadline) : IAgentStep<DraftPlannerInput, DraftPlannerPayload>
{
    public AgentDefinition Definition { get; } = DraftingStepDefinitions.OfferPlanner(deadline);
}

/// <summary>Round two: the negotiation writer, held to <see cref="DraftGuard"/> by its verifier (one
/// retry with the violation named, exactly the old regenerate-once policy). A step is built per
/// draft because the verifier needs that draft's pack; it is not shared between requests.</summary>
internal sealed class NegotiationWriterStep(TimeSpan deadline, DraftWriterVerifier verifier)
    : IAgentStep<DraftWriterInput, DraftWriterPayload>
{
    public AgentDefinition Definition { get; } = DraftingStepDefinitions.NegotiationWriter(deadline);

    public IStepVerifier<DraftWriterInput, DraftWriterPayload>? Verifier { get; } = verifier;
}

/// <summary>
/// <see cref="DraftGuard"/> (and, when enforced, the formal/legal-tone check) as the writer's step
/// verifier. It also records the violation of each attempt, in order, because the old workflow
/// reported one failure line per rejected attempt ("negotiation-writer: ..." then
/// "negotiation-writer (retry): ...") and the runner's result carries only the last. Per-draft state:
/// never register or share an instance.
/// </summary>
internal sealed class DraftWriterVerifier(IReadOnlyList<PackItem> pack, int maxBodyChars, bool enforceTone)
    : IStepVerifier<DraftWriterInput, DraftWriterPayload>
{
    private readonly List<string> _violations = [];

    public string Name => "draft-guard";

    /// <summary>The violation of every rejected attempt, oldest first.</summary>
    public IReadOnlyList<string> Violations => _violations;

    /// <summary>Whether the last attempt was rejected for tone rather than grounding.</summary>
    private bool _lastWasTone;

    public StepVerdict Verify(DraftWriterInput input, DraftWriterPayload output)
    {
        var verdict = DraftGuard.Validate(output.Subject, output.Body, output.UsedCitationKeys, pack, maxBodyChars);
        if (!verdict.Passed)
        {
            _lastWasTone = false;
            _violations.Add(verdict.Violation!);
            return StepVerdict.Reject(verdict.Violation!);
        }

        if (enforceTone)
        {
            var findings = DraftToneChecker.Check(output.Subject, output.Body);
            if (findings.Count > 0)
            {
                var violation = DraftToneChecker.Describe(findings);
                _lastWasTone = true;
                _violations.Add(violation);
                return StepVerdict.Reject(violation);
            }
        }

        return StepVerdict.Pass;
    }

    public string RetryInstruction(StepVerdict verdict) =>
        _lastWasTone
            ? DraftToneChecker.BuildRetryInstruction(verdict.Violations[0])
            : DraftingAgents.BuildRetryInstruction(verdict.Violations[0]);
}
