using System.Globalization;
using System.Text.RegularExpressions;
using Raffa.AiFlows.Ask.Planning;
using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Domain;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Ask.Orchestration;

/// <summary>Pure server-side reducer for durable conversational focus; never accepts resource IDs from prose.</summary>
internal static partial class ConversationStateReducer
{
    private static readonly Regex Comparison = new(
        @"\b(rispetto\s+a|confront\w*|versus|vs\.?|compar(?:e|ed|ing|ison)?\s+(?:to|with)|against)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Dismiss = new(
        @"\b(lascia\s+stare|ignora|togli|forget|drop|leave\s+out|never\s*mind)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Constraint = new(
        @"\b(non\s+(?:menzionare|citare|includere|usare)|senza\s+[^.!?]+|do\s+not\s+(?:mention|cite|include|use)|without\s+[^.!?]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EllipticalReference = new(
        @"^(?:e\s+|and\s+)?(?:[eèé]\s|is\s|it\b|quello\b|questo\b|quella\b|l['’]altro\b|l['’]altra\b|this\b|that\b|the\s+other(?:\s+one)?\b|come\b|how\b|dove\b|where\b|perch[eé]\b|why\b|aprilo\b|crealo\b|open\s+it\b|create\s+it\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OtherReference = new(
        @"^\s*(?:(?:e|and)\s+)?(?:l['’]altr[oa]|quell[oa]\s+altr[oa]|the\s+other(?:\s+one)?)(?:\b|[?!.,])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool ShouldInheritPrimary(string question) =>
        !string.IsNullOrWhiteSpace(question) &&
        question.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 14 &&
        EllipticalReference.IsMatch(question.Trim());

    /// <summary>Only switches to a supplier already held in server-owned conversation state.</summary>
    public static bool RefersToPreviousPrimary(string question) =>
        !string.IsNullOrWhiteSpace(question) && OtherReference.IsMatch(question);

    public static ConversationState Reduce(
        ConversationState? current, string question, IntentPlanResult? plan, string? namedSupplier,
        EntityId? authorisedScopeContractId, ReplyPayload? payload)
    {
        var state = current ?? ConversationState.Empty;
        var comparisons = (state.ComparisonSuppliers ?? []).ToList();
        var constraints = (state.Constraints ?? []).ToList();
        var primary = state.PrimarySupplier;
        var previous = state.PreviousPrimarySupplier;

        if (namedSupplier is not null && Dismiss.IsMatch(question))
        {
            comparisons.RemoveAll(x => string.Equals(x, namedSupplier, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(primary, namedSupplier, StringComparison.OrdinalIgnoreCase))
            {
                primary = previous;
                previous = null;
            }
        }
        else if (namedSupplier is not null && Comparison.IsMatch(question) && primary is not null &&
                 !string.Equals(primary, namedSupplier, StringComparison.OrdinalIgnoreCase))
        {
            if (!comparisons.Contains(namedSupplier, StringComparer.OrdinalIgnoreCase)) comparisons.Add(namedSupplier);
        }
        else if (namedSupplier is not null && plan?.Intent is not AskIntent.PortfolioStrategy
                     and not AskIntent.PortfolioMarketPosition and not AskIntent.PortfolioSavingsTarget)
        {
            if (!string.Equals(primary, namedSupplier, StringComparison.OrdinalIgnoreCase)) previous = primary;
            primary = namedSupplier;
            comparisons.Clear();
        }

        foreach (Match match in Constraint.Matches(question))
        {
            var value = match.Value.Trim();
            if (value.Length > 180) value = value[..180];
            if (!constraints.Contains(value, StringComparer.OrdinalIgnoreCase)) constraints.Add(value);
        }

        var goal = plan?.Goal;
        var goalText = goal?.HasTarget == true
            ? string.Join(' ', new[]
            {
                goal.TargetAmount?.ToString(CultureInfo.InvariantCulture), goal.Currency,
                goal.TargetPercent is null ? null : goal.TargetPercent.Value.ToString(CultureInfo.InvariantCulture) + "%",
                goal.WindowLabel,
            }.Where(x => !string.IsNullOrWhiteSpace(x)))
            : state.Goal;
        var artifact = payload?.Artifact;
        var action = payload?.UiActions?.FirstOrDefault(x =>
            x.Type == UiActionPayload.OpenActionPanel && x.ResourceType == UiActionPayload.ProcurementArtifact);

        return state with
        {
            ActiveContractId = artifact?.ContractId ?? authorisedScopeContractId?.Value.ToString() ?? state.ActiveContractId,
            PrimarySupplier = primary,
            PreviousPrimarySupplier = previous,
            ComparisonSuppliers = comparisons,
            LastIntent = plan?.Intent.ToString() ?? state.LastIntent,
            Goal = goalText,
            GoalAmount = goal?.HasTarget == true ? goal.TargetAmount : state.GoalAmount,
            GoalPercent = goal?.HasTarget == true ? goal.TargetPercent : state.GoalPercent,
            GoalCurrency = goal?.HasTarget == true ? goal.Currency : state.GoalCurrency,
            GoalWindowDays = goal?.HasTarget == true ? goal.WindowDays : state.GoalWindowDays,
            Constraints = constraints,
            CurrentArtifactId = artifact?.Id ?? action?.ResourceId ?? state.CurrentArtifactId,
            MissingInputs = artifact?.MissingInputs ?? state.MissingInputs ?? [],
            LastSuccessfulTool = artifact is not null ? "artifact.compose" : action is not null ? "artifact.open" : state.LastSuccessfulTool,
            PendingArtifactInstruction = artifact is not null || payload?.ArtifactChangeCancelled == true
                ? null
                : payload?.ArtifactChangeProposal?.Instruction ?? state.PendingArtifactInstruction,
            PendingAction = artifact is not null || payload?.ArtifactChangeCancelled == true
                ? action?.Type
                : payload?.ArtifactChangeProposal is not null ? "CONFIRM_ARTIFACT_UPDATE" : action?.Type ?? state.PendingAction,
        };
    }
}
