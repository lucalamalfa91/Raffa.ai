using Raffa.AiFlows.CapabilityGaps.Agents;
using Raffa.AiFlows.CapabilityGaps.Guards;
using Raffa.AiFlows.Shared.Guards;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Planning;
using Raffa.AiGateway;
using Raffa.AiGateway.Agents;
using Raffa.AiGateway.Contracts;

namespace Raffa.AiFlows.CapabilityGaps.Drafting;

/// <summary>Where the draft came from — audited: a template fallback counts as a guard
/// intervention on the turn, so the golden set catches a fixture or prompt regression.</summary>
public enum DraftSource
{
    Model,
    Template,
}

/// <summary>What the composition root asks for: the question, its language, the supplier's
/// display name, the already-budgeted pack (the same Q3 pack a renewal-strategy turn gets, with
/// the council's plays inserted) and the parsed goal.</summary>
public sealed record DraftRequest(
    string Question,
    string Language,
    string SupplierName,
    IReadOnlyList<PackItem> Pack,
    SavingsGoal? Goal);

/// <summary>The drafted email plus everything the reply builder and the audit need.</summary>
/// <param name="UsedCitationKeys">Pack keys the email rests on — already filtered to keys that
/// exist in the pack.</param>
/// <param name="Attempts">Writer calls made (0 for a template-only draft).</param>
/// <param name="Metadata">The last writer call's reproducibility metadata (ADR-011), null for a
/// template draft.</param>
/// <param name="Failures">Agent failures along the way — logged, never shown to the user.</param>
/// <param name="Type">What kind of communication was asked for (<see cref="DraftTypeClassifier"/>);
/// a label only — every type is drafted by the same writer and template.</param>
/// <param name="ToneFindings">The formal / legal-tone check's findings on the returned text
/// (<see cref="DraftToneChecker"/>); null or empty when it reads as a formal, non-binding letter.</param>
/// <param name="ReusedCouncilPlays">True when the offer planner was skipped because the turn's
/// council plays were already in the pack (<see cref="DraftingOptions.ReuseCouncilPlays"/>, F4-T05).</param>
public sealed record DraftOutcome(
    string Subject,
    string Body,
    IReadOnlyList<string> UsedCitationKeys,
    DraftSource Source,
    int Attempts,
    AiCallMetadata? Metadata,
    IReadOnlyList<string> Failures,
    DraftType Type = DraftType.Renegotiation,
    IReadOnlyList<string>? ToneFindings = null,
    bool ReusedCouncilPlays = false);

/// <summary>
/// The agentic workflow behind a drafted negotiation email (ADR-030 D3): round one, the offer
/// planner reads the pack (contract facts, lever calculations, council plays, market records,
/// playbook) and returns an offer plan; round two, the negotiation writer turns the plan into a
/// subject and a body; <see cref="DraftGuard"/> polices the result; one retry names the
/// violation; and a second failure — or a disabled workflow, a thin pack, a gateway outage —
/// falls back to <see cref="NegotiationEmailTemplate"/>. It never returns nothing: the user
/// always leaves with an email they can copy.
///
/// <para>
/// A-08: the two agents are runner steps (<see cref="OfferPlannerStep"/>,
/// <see cref="NegotiationWriterStep"/>, same shape as the council's), their requests byte-identical
/// to the hand-written calls they replace (prompts <c>draft-v1</c>, template
/// <c>draft-template-v1</c> untouched). Around them: the draft type
/// (<see cref="DraftTypeClassifier"/>), the formal / legal-tone check
/// (<see cref="DraftToneChecker"/>, report-only unless <see cref="DraftingOptions.EnforceTone"/>) and,
/// opt-in, reuse of the turn's council plays instead of a planner call
/// (<see cref="DraftingOptions.ReuseCouncilPlays"/>, F4-T05). The result is a copyable draft and
/// nothing else (decision D4): this workflow sends nothing and persists nothing.
/// </para>
/// </summary>
public sealed class NegotiationDraftingWorkflow
{
    private const string CouncilPlayPrefix = "calc:council:play[";

    private readonly DraftingOptions options;
    private readonly StepRunner runner;

    /// <param name="aiGateway">The gateway every agent call goes through (the runner never bypasses it).</param>
    /// <param name="options">The workflow's bounds and switches.</param>
    /// <param name="runner">The request's Scoped step runner; one over <paramref name="aiGateway"/> when omitted.</param>
    public NegotiationDraftingWorkflow(IAiGateway aiGateway, DraftingOptions options, StepRunner? runner = null)
    {
        ArgumentNullException.ThrowIfNull(aiGateway);
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
        this.runner = runner ?? new StepRunner(aiGateway);
    }

    public async Task<DraftOutcome> DraftAsync(DraftRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Question);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SupplierName);
        ArgumentNullException.ThrowIfNull(request.Pack);

        var type = DraftTypeClassifier.Classify(request.Question);
        var pack = request.Pack;
        var failures = new List<string>();

        if (!options.Enabled || pack.Count < options.MinPackItems)
        {
            return Finalize(BuildTemplate(request, pack, null, type), type, reused: false);
        }

        var items = pack.Take(options.MaxItemsPerAgent).Select(ToItemDto).ToList();
        var goal = ToGoalDto(request.Goal);

        // ----- Round one: the offer planner, or the turn's own council plays (F4-T05, opt-in) -----
        DraftPlan plan;
        var reused = false;
        if (options.ReuseCouncilPlays && pack.Any(i => i.CitationKey.StartsWith(CouncilPlayPrefix, StringComparison.Ordinal)))
        {
            plan = DraftPlan.FromPack(pack);
            reused = true;
        }
        else
        {
            plan = await PlanAsync(request, items, goal, pack, failures, cancellationToken).ConfigureAwait(false)
                ?? DraftPlan.FromPack(pack);
        }

        // ----- Round two: the writer, guarded, one retry -----
        var writerInput = new DraftWriterInput(request.Question, request.Language, request.SupplierName, goal, plan, items);
        var verifier = new DraftWriterVerifier(pack, options.MaxBodyChars, options.EnforceTone);
        var step = new NegotiationWriterStep(options.AgentDeadline, verifier);

        var result = await runner.RunAsync(step, writerInput, cancellationToken).ConfigureAwait(false);
        AddWriterFailures(failures, verifier, result);

        if (result.Value is { } payload)
        {
            return Finalize(Outcome(payload, result.Metadata!, pack, result.Attempts, failures), type, reused);
        }

        return Finalize(
            BuildTemplate(request, pack, plan, type) with
            {
                Attempts = result.Attempts >= 2 ? 2 : 0,
                Failures = failures,
            },
            type,
            reused);
    }

    /// <summary>The deterministic draft: <see cref="NegotiationEmailTemplate"/>, or with
    /// <see cref="DraftingOptions.LocalizedTemplates"/> the one for the draft type and language.</summary>
    private DraftOutcome BuildTemplate(DraftRequest request, IReadOnlyList<PackItem> pack, DraftPlan? plan, DraftType type) =>
        options.LocalizedTemplates
            ? LocalizedDraftTemplate.Build(request.Language, request.SupplierName, pack, plan, type)
            : NegotiationEmailTemplate.Build(request.Language, request.SupplierName, pack, plan);

    /// <summary>One failure line per rejected attempt, as before ("negotiation-writer: ..." then
    /// "negotiation-writer (retry): ..."), then the step's own failure when it ended on something
    /// other than the verifier (gateway error, unusable payload, deadline).</summary>
    private static void AddWriterFailures(
        List<string> failures, DraftWriterVerifier verifier, StepResult<DraftWriterPayload> result)
    {
        for (var i = 0; i < verifier.Violations.Count; i++)
        {
            failures.Add(Line(i > 0, verifier.Violations[i]));
        }

        if (result.Status == StepStatus.Failed && result.Reason != StepFailureReason.VerifierRejected)
        {
            failures.Add(Line(result.Attempts >= 2, result.Error ?? result.Reason.ToString()));
        }

        static string Line(bool retry, string text) =>
            $"{DraftingAgents.NegotiationWriterName}{(retry ? " (retry)" : string.Empty)}: {text}";
    }

    private static DraftOutcome Finalize(DraftOutcome outcome, DraftType type, bool reused) =>
        outcome with
        {
            Type = type,
            ToneFindings = DraftToneChecker.Check(outcome.Subject, outcome.Body),
            ReusedCouncilPlays = reused,
        };

    private async Task<DraftPlan?> PlanAsync(
        DraftRequest request,
        IReadOnlyList<DraftItem> items,
        DraftGoal? goal,
        IReadOnlyList<PackItem> pack,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(
                new OfferPlannerStep(options.AgentDeadline),
                new DraftPlannerInput(request.Question, request.Language, request.SupplierName, goal, items),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Value is not { } payload)
        {
            failures.Add(result.Describe()!);
            return null;
        }

        // Local validation, no model call: an ask must cite the pack and carry only its numbers.
        var packKeys = pack.Select(i => i.CitationKey).ToHashSet(StringComparer.Ordinal);
        var asks = (payload.Asks ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a.Sentence))
            .Select(a => new DraftAsk(
                (a.Lever ?? string.Empty).Trim(),
                a.Sentence!.Trim(),
                (a.CitationKeys ?? []).Where(packKeys.Contains).Distinct(StringComparer.Ordinal).ToList()))
            .Where(a => a.CitationKeys.Count > 0 && NumericGuard.Validate(a.Sentence, pack).Passed)
            .Take(4)
            .ToList();

        if (asks.Count == 0)
        {
            failures.Add($"{DraftingAgents.OfferPlannerName}: no grounded ask survived validation.");
            return null;
        }

        static string Clean(string? text, IReadOnlyList<PackItem> pack) =>
            !string.IsNullOrWhiteSpace(text) && NumericGuard.Validate(text, pack).Passed ? text.Trim() : string.Empty;

        return new DraftPlan(
            Clean(payload.Position, pack),
            asks,
            Clean(payload.Trade, pack),
            Clean(payload.DeadlineAnchor, pack),
            Clean(payload.Closing, pack));
    }

    private static DraftOutcome Outcome(
        DraftWriterPayload payload, AiCallMetadata metadata, IReadOnlyList<PackItem> pack, int attempts, List<string> failures) =>
        new(
            payload.Subject!.Trim(),
            payload.Body!.Trim(),
            DraftGuard.GroundedKeys(payload.UsedCitationKeys, pack),
            DraftSource.Model,
            attempts,
            metadata,
            failures);

    private static DraftGoal? ToGoalDto(SavingsGoal? goal) =>
        goal is null || !goal.HasTarget
            ? null
            : new DraftGoal(goal.TargetAmount, goal.TargetPercent, goal.Currency, goal.WindowDays, goal.WindowLabel);

    private static DraftItem ToItemDto(PackItem item) =>
        new(item.CitationKey, item.Corpus, item.Title, item.Subtitle, item.Snippet,
            item.Values.Select(v => new DraftValue(v.Key, v.Value, v.Kind.ToString(), v.Currency)).ToList());
}
