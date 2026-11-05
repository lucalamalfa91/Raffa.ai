using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Raffa.AiFlows.Ask.Answering;
using Raffa.AiFlows.Ask.Guards;
using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.Ask.Reply;
using Raffa.AiFlows.Negotiation.Orchestration;
using Raffa.AiFlows.Shared.Guards;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Reply;
using Raffa.AiFlows.Shared.Routing;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Telemetry;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Domain;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Ask.Agentic;

/// <summary>What the orchestrator produced, with what the host needs for the state reducer and the audit.</summary>
internal sealed record AgenticTurnResult(
    CopilotReply Reply,
    bool GuardIntervened,
    bool FallbackUsed,
    string? FocusSupplier,
    PortfolioListItemRef? FocusContract,
    AskIntent Intent,
    IReadOnlyList<string> ToolsCalled);

internal sealed record PortfolioListItemRef(Guid ContractId);

/// <summary>
/// The orchestrator (ADR-024 amendment 2026-11-05, plan PR 3): one Ask turn as a bounded loop over
/// the `agent` role and the <see cref="IAskTool"/> catalog. The model reads the conversation, the
/// server-owned state and the tool descriptions; it asks for tool calls (run in parallel when it
/// asks for several), reads the results, and writes the final answer as strict JSON. The final
/// answer passes the same guards as the legacy path (every citation key in the union of the tool
/// packs, every figure verbatim; one regeneration with the violation named, then the grounded
/// fallback or an honest abstain) and becomes the same <see cref="CopilotReply"/>. A tool that
/// ends the turn (a web-consent interview) returns at once. The role unavailable before any tool
/// ran (no deployment, no fixture script) returns <see langword="null"/>: the host falls back to
/// the deterministic path, so turning the flag on can never break a turn.
/// </summary>
public sealed class AgenticAskTurn(
    IAiGateway aiGateway,
    IEnumerable<IAskTool> tools,
    CapabilityRouting capabilityRouting,
    AbstainRecovery abstainRecovery,
    AskTurnOptions? turnOptions = null,
    ILogger<AgenticAskTurn>? logger = null)
{
    public const string AgentName = "ask-orchestrator";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly string FinalSchema = JsonSerializer.Serialize(new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "canDetermine", "answerMarkdown", "citationKeys", "actionKeys", "followUps", "abstainReason", "focusSupplier" },
        properties = new
        {
            canDetermine = new { type = "boolean" },
            answerMarkdown = new { type = "string" },
            citationKeys = new { type = "array", items = new { type = "string" } },
            actionKeys = new { type = "array", items = new { type = "string" } },
            followUps = new { type = "array", items = new { type = "string" } },
            abstainReason = new { type = new[] { "string", "null" } },
            focusSupplier = new { type = new[] { "string", "null" } },
        },
    });

    private readonly AskTurnOptions settings = turnOptions ?? new AskTurnOptions();
    private readonly IReadOnlyList<IAskTool> toolList = tools.ToList();

    private const int MaxHistoryTurns = 12;
    private const int MaxTurnChars = 2500;
    private const int MaxToolResultChars = 24000;

    internal async Task<AgenticTurnResult?> RunAsync(AskToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var definitions = toolList.Select(t => new AiToolDefinition(t.Name, t.Description, t.ParametersSchemaJson)).ToList();
        var byName = toolList.ToDictionary(t => t.Name, StringComparer.Ordinal);

        var messages = BuildHistory(context);
        messages.Add(AiConversationMessage.User(context.Question));
        var systemPrompt = BuildSystemPrompt(context);

        var pack = new List<PackItem>();
        var stepsRun = new List<string>();
        var failures = new List<string>();
        var marketQueries = 0;
        ArtifactPayload? artifact = null;
        var uiActions = new List<UiActionPayload>();
        var toolActions = new List<CopilotAction>();
        var calls = 0;
        AiConversationResult? final = null;
        AiCallMetadata? metadata = null;

        using var deadline = TurnDeadline.Start(settings, cancellationToken);
        var token = deadline.ForAgents();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                var forceFinal = calls >= settings.AgenticMaxToolCalls;
                var request = new AiConversationRequest(AgentName, systemPrompt, messages, definitions, FinalSchema, AgentPrompt.Version, forceFinal);
                var step = await aiGateway.ConverseAsync(request, token).ConfigureAwait(false);
                if (step.IsFailure)
                {
                    if (calls == 0 && stepsRun.Count == 0)
                    {
                        logger?.LogWarning("Agent role unavailable, falling back to the deterministic path: {Error}", step.Error);
                        return null;
                    }

                    failures.Add("agent: " + step.Error);
                    break;
                }

                metadata = step.Value.Metadata;
                if (step.Value.IsFinal)
                {
                    final = step.Value;
                    break;
                }

                // The model asked for tools: run them together, append its own message and one tool
                // message per call, in the order it asked.
                var toolCalls = step.Value.ToolCalls.Take(Math.Max(1, settings.AgenticMaxToolCalls - calls)).ToList();
                messages.Add(AiConversationMessage.AssistantToolCalls(toolCalls));
                var executions = toolCalls.Select(call => ExecuteAsync(byName, call, context, token)).ToList();
                await Task.WhenAll(executions).ConfigureAwait(false);
                AskToolResult? endTurn = null;
                for (var i = 0; i < toolCalls.Count; i++)
                {
                    var call = toolCalls[i];
                    var outcome = executions[i].Result;
                    calls++;
                    stepsRun.Add(call.Name);
                    if (outcome.Failure is not null)
                    {
                        failures.Add($"{call.Name}: {outcome.Failure}");
                    }

                    marketQueries += outcome.MarketQueries;
                    foreach (var item in outcome.PackItems.Where(item => pack.All(p => p.CitationKey != item.CitationKey)))
                    {
                        pack.Add(item);
                    }

                    artifact = outcome.Artifact ?? artifact;
                    if (outcome.UiActions is { } ui) uiActions.AddRange(ui);
                    if (outcome.Actions is { } acts) toolActions.AddRange(acts);
                    endTurn ??= outcome.EndTurnReply is null ? null : outcome;
                    var json = outcome.ResultJson.Length > MaxToolResultChars ? outcome.ResultJson[..MaxToolResultChars] + "…\"}" : outcome.ResultJson;
                    messages.Add(AiConversationMessage.ToolResult(call.Id, json));
                }

                if (endTurn?.EndTurnReply is { } ending)
                {
                    WriteDetail(stepsRun, failures, marketQueries, stopwatch);
                    return new AgenticTurnResult(ending, false, false, context.FocusSupplier, Ref(context), IntentOf(stepsRun), stepsRun);
                }
            }
        }
        catch (OperationCanceledException) when (deadline.Active && deadline.Expired(token))
        {
            deadline.Mark("agents");
            failures.Add($"{AskFlowOutcome.DeadlineFailureStep}: agents");
        }

        deadline.WriteDetail();
        WriteDetail(stepsRun, failures, marketQueries, stopwatch);

        var routingContext = context.RoutingContext();
        var intent = IntentOf(stepsRun);
        var recoveryActions = abstainRecovery.ResolveAbstainRecoveryActions(context.Portfolio, routingContext, intent);
        var recoveryFollowUps = AbstainRecovery.AbstainFollowUps(context.Question, context.Portfolio, intent);

        if (final is null)
        {
            // The loop ended without an answer (a failed step, the deadline): the pack already read
            // still makes a grounded answer when it can; otherwise the honest "service busy".
            var busyMetadata = metadata ?? new AiCallMetadata("agent-unavailable", "1", AgentPrompt.Version, DateTimeOffset.UtcNow, string.Empty);
            var grounded = pack.Count > 0 ? GroundedFallbackAnswer.Compose(context.Question, pack, busyMetadata) : null;
            if (grounded is not null && Validate(grounded, pack).Passed)
            {
                return Finish(context, grounded, pack, toolActions, recoveryActions, recoveryFollowUps, artifact, uiActions, routingContext, intent, stepsRun, true, true);
            }

            return new AgenticTurnResult(
                new CopilotReply(ReplyKind.Abstain, HelpfulFallbackAnswer.ServiceBusy(context.Question), [], recoveryActions, ReplyProvenance.NoModelCall([]), recoveryFollowUps),
                false, true, context.FocusSupplier, Ref(context), intent, stepsRun);
        }

        var parsedFinal = Parse(final.FinalJson!, final.Metadata);
        var focusFromModel = parsedFinal?.Focus;
        if (parsedFinal is null)
        {
            failures.Add("agent: final answer was not the expected JSON");
            return new AgenticTurnResult(
                new CopilotReply(ReplyKind.Abstain, HelpfulFallbackAnswer.ServiceBusy(context.Question), [], recoveryActions, ReplyProvenance.NoModelCall([]), recoveryFollowUps),
                false, true, context.FocusSupplier, Ref(context), intent, stepsRun);
        }

        var result = parsedFinal.Value.Result;
        var guardIntervened = false;
        var fallbackUsed = false;
        var verdict = Validate(result, pack);
        if (!verdict.Passed)
        {
            // R-ASK-06: regenerate once with the violation named, no more tools; then downgrade.
            guardIntervened = true;
            messages.Add(AiConversationMessage.Assistant(final.FinalJson!));
            messages.Add(AiConversationMessage.User(RegenerateOnce.BuildRetryInstruction(verdict.Violation!)));
            var retry = await aiGateway.ConverseAsync(
                    new AiConversationRequest(AgentName, systemPrompt, messages, definitions, FinalSchema, AgentPrompt.Version, ForceFinal: true), cancellationToken)
                .ConfigureAwait(false);
            var retried = retry.IsSuccess && retry.Value.IsFinal ? Parse(retry.Value.FinalJson!, retry.Value.Metadata) : null;
            if (retried is not null && Validate(retried.Value.Result, pack).Passed)
            {
                result = retried.Value.Result;
                focusFromModel = retried.Value.Focus ?? focusFromModel;
            }
            else
            {
                var grounded = GroundedFallbackAnswer.Compose(context.Question, pack, result.Metadata);
                result = grounded is not null && Validate(grounded, pack).Passed
                    ? grounded
                    : RegenerateOnce.DowngradeToAbstain(result.Metadata, pack, verdict.Violation!);
                fallbackUsed = grounded is not null;
            }
        }

        return Finish(context, result, pack, toolActions, recoveryActions, recoveryFollowUps, artifact, uiActions, routingContext, intent, stepsRun, guardIntervened, fallbackUsed, focusFromModel);
    }

    private AgenticTurnResult Finish(
        AskToolContext context, AiAnswerResult result, List<PackItem> pack, List<CopilotAction> toolActions,
        IReadOnlyList<CopilotAction> recoveryActions, IReadOnlyList<string> recoveryFollowUps, ArtifactPayload? artifact,
        List<UiActionPayload> uiActions, RoutingContext routingContext, AskIntent intent, List<string> stepsRun, bool guardIntervened, bool fallbackUsed,
        string? focusFromModel = null)
    {
        var actionKeys = ActionKeyNormalizer.Routable(ActionKeyNormalizer.Normalize(result.ActionKeys), routingContext);
        var resolved = actionKeys.Count > 0
            ? capabilityRouting.ResolveActions(actionKeys.Select(CapabilityIntent.HowTo).ToList(), routingContext)
            : [];
        var actions = toolActions.Concat(resolved).Distinct().ToList();

        var reply = CopilotReplyBuilder.FromGuardedResult(result, pack, actions, recoveryActions, recoveryFollowUps);
        if (artifact is not null || uiActions.Count > 0)
        {
            reply = reply with
            {
                Payload = (reply.Payload ?? new ReplyPayload()) with
                {
                    Artifact = artifact,
                    UiActions = uiActions.Count > 0 ? uiActions.Distinct().ToList() : null,
                },
            };
        }

        var focus = (focusFromModel is null ? null : context.ResolveSupplierName(focusFromModel)) ?? context.FocusSupplier;
        return new AgenticTurnResult(reply, guardIntervened, fallbackUsed, focus, Ref(context), intent, stepsRun);
    }

    private static PortfolioListItemRef? Ref(AskToolContext context) =>
        context.FocusContractItem is { } f ? new PortfolioListItemRef(f.ContractId) : null;

    private async Task<AskToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, IAskTool> byName, AiToolCall call, AskToolContext context, CancellationToken cancellationToken)
    {
        if (!byName.TryGetValue(call.Name, out var tool))
        {
            return AskToolResult.Error($"Unknown tool '{call.Name}'.");
        }

        JsonElement arguments;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson);
            arguments = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return AskToolResult.Error($"Arguments were not valid JSON: {ex.Message}");
        }

        using var step = RunContext.BeginStep(tool.Name);
        try
        {
            return await tool.ExecuteAsync(arguments, context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Ask tool {Tool} failed; the turn continues without it.", tool.Name);
            return AskToolResult.Error($"The tool failed: {ex.GetType().Name}. Answer from what the other tools returned.");
        }
    }

    private static void WriteDetail(List<string> stepsRun, List<string> failures, int marketQueries, Stopwatch stopwatch)
    {
        // Step names in the audit row use hyphens, the only separator AskFlowOutcome's step-name
        // pattern accepts (tool names use underscores for the model).
        RunContext.AddTurnDetail("agentic=true");
        RunContext.AddTurnDetail(new AskFlowOutcome(
                [], [],
                stepsRun.Select(s => s.Replace('_', '-')).ToList(),
                failures.Select(f => f.Replace('_', '-')).ToList(),
                [], RunContext.Current?.RunId, 0, null)
            .ToAuditDetail()
            .Replace("marketQueries=0", $"marketQueries={marketQueries}", StringComparison.Ordinal));
        RunContext.AddTurnDetail($"agentMs={stopwatch.ElapsedMilliseconds}");
    }

    /// <summary>The intent the state reducer and the recovery actions reason on, from the tools used.</summary>
    private static AskIntent IntentOf(IReadOnlyList<string> steps)
    {
        if (steps.Contains("run_negotiation_council") || steps.Contains("get_savings")) return AskIntent.Savings;
        if (steps.Contains("get_market_benchmark")) return AskIntent.MarketCompare;
        if (steps.Contains("get_renewal_strategy")) return AskIntent.RenewalStrategy;
        if (steps.Contains("search_clauses")) return AskIntent.Clause;
        if (steps.Contains("get_document_status")) return AskIntent.DocumentStatus;
        if (steps.Contains("web_research")) return AskIntent.WebResearch;
        if (steps.Contains("navigate")) return AskIntent.Navigate;
        if (steps.Contains("list_portfolio") && !steps.Contains("get_contract")) return AskIntent.PortfolioStrategy;
        return AskIntent.StructuredFact;
    }

    private static GuardVerdict Validate(AiAnswerResult result, IReadOnlyList<PackItem> pack)
    {
        var grounding = GroundingGuard.Validate(result, pack, allowUncitedGuidance: true);
        return grounding.Passed ? NumericGuard.Validate(result.AnswerMarkdown, pack, result.CitationKeys) : grounding;
    }

    private static (AiAnswerResult Result, string? Focus)? Parse(string json, AiCallMetadata metadata)
    {
        FinalAnswer? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<FinalAnswer>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (parsed is null)
        {
            return null;
        }

        var markdown = parsed.AnswerMarkdown?.Trim() ?? string.Empty;
        return (new AiAnswerResult(
            parsed.CanDetermine && markdown.Length > 0,
            markdown.Length > 0 ? markdown : null,
            [],
            metadata,
            markdown.Length > 0 ? markdown : null,
            parsed.CitationKeys ?? [],
            parsed.ActionKeys ?? [],
            parsed.AbstainReason,
            parsed.FollowUps ?? []), string.IsNullOrWhiteSpace(parsed.FocusSupplier) ? null : parsed.FocusSupplier.Trim());
    }

    private static List<AiConversationMessage> BuildHistory(AskToolContext context)
    {
        var messages = new List<AiConversationMessage>();
        foreach (var (role, markdown) in context.RecentTurns.TakeLast(MaxHistoryTurns))
        {
            var text = markdown.Length > MaxTurnChars ? markdown[..MaxTurnChars] + "…" : markdown;
            if (role.Equals("you", StringComparison.OrdinalIgnoreCase) || role.Equals("user", StringComparison.OrdinalIgnoreCase))
            {
                messages.Add(AiConversationMessage.User(text));
            }
            else if (role.Equals("raffa", StringComparison.OrdinalIgnoreCase) || role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
            {
                messages.Add(AiConversationMessage.Assistant(text));
            }
        }

        return messages;
    }

    private static string BuildSystemPrompt(AskToolContext context)
    {
        var sb = new System.Text.StringBuilder(AgentPrompt.SystemPrompt);
        sb.AppendLine();
        if (Raffa.SharedKernel.Language.ResponseLanguage.Directive is { } directive)
        {
            sb.AppendLine(directive);
        }

        sb.AppendLine($"Today is {DateTime.UtcNow:yyyy-MM-dd}.");
        sb.AppendLine("Suppliers with a validated contract in this workspace: " +
            (context.KnownSupplierNames.Count == 0 ? "(none yet - nothing to read; say so and point to Documents)" : string.Join("; ", context.KnownSupplierNames)) + ".");
        if (context.ScopedContractItem is not null)
        {
            sb.AppendLine("This chat is opened on one specific contract: 'this contract' means it (pass supplier 'this').");
        }

        if (context.ConversationState is { } state)
        {
            sb.AppendLine("Server-owned conversation state (authoritative for ellipsis): " +
                $"primarySupplier={state.PrimarySupplier ?? "none"}; previousPrimarySupplier={state.PreviousPrimarySupplier ?? "none"}; " +
                $"comparisons={string.Join(", ", state.ComparisonSuppliers ?? [])}; lastIntent={state.LastIntent ?? "none"}; goal={state.Goal ?? "none"}; " +
                $"constraints={string.Join(" | ", state.Constraints ?? [])}; currentArtifact={(context.CurrentArtifact is { } a ? $"{a.Kind} '{a.Title}' v{a.Version} on {a.FocusSupplier ?? "portfolio"}" : "none")}.");
        }

        if (context.UserMemory is { } memory && (memory.FrequentWorkflows.Count > 0 || memory.PreferredOutput is not null))
        {
            sb.AppendLine("User preferences (presentation only, never facts): " +
                $"preferredOutput={memory.PreferredOutput ?? "none"}; frequentWorkflows={string.Join(", ", memory.FrequentWorkflows)}.");
        }

        foreach (var (role, markdown) in context.RecentTurns.Where(t => t.Role == "preference"))
        {
            sb.AppendLine(markdown);
        }

        return sb.ToString();
    }

    private sealed record FinalAnswer(
        bool CanDetermine,
        string? AnswerMarkdown,
        IReadOnlyList<string>? CitationKeys,
        IReadOnlyList<string>? ActionKeys,
        IReadOnlyList<string>? FollowUps,
        string? AbstainReason,
        string? FocusSupplier);
}
