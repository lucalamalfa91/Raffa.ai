using System.Text.Json;
using Raffa.AiFlows.Ask.Agentic;
using Raffa.AiFlows.Ask.Interview;
using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.Ask.Reply;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Routing;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Telemetry;
using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Domain;
using Raffa.Documents.Contracts.Application;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiFlows.Tests.Ask.Agentic;

/// <summary>
/// The orchestrator loop (ADR-024 amendment 2026-11-05) over a scripted `agent` role and fake tools:
/// tool calls run and their results go back to the model in order; the final answer passes the
/// guards over the union of the tool packs, with one regeneration on a violation; a tool that ends
/// the turn returns at once; the call budget forces the final answer; a role that is unavailable
/// before any tool ran hands the turn back (null) so the host falls back to the router.
/// </summary>
public sealed class AgenticAskTurnTests
{
    private static readonly AiCallMetadata Metadata = new("scripted", "1", AgentPrompt.Version, new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero), "00");

    private static readonly PackItem CoupaEnd = new(
        "fact:coupa:end", PackCorpus.Tenant, "Coupa · OrderForm", "end date", 1, "1", "The OrderForm ends on 15 April 2026.", null, null, null, "contract",
        [new PackValue("endDate", "2026-04-15", PackValueKind.Date)]);

    private sealed class ScriptedGateway(params Func<AiConversationRequest, Result<AiConversationResult>>[] steps) : IAiGateway
    {
        public List<AiConversationRequest> Requests { get; } = [];

        public Task<Result<AiConversationResult>> ConverseAsync(AiConversationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var index = Math.Min(Requests.Count - 1, steps.Length - 1);
            return Task.FromResult(steps[index](request));
        }

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeTool(string name, Func<JsonElement, AskToolContext, AskToolResult> run) : IAskTool
    {
        public List<string> Arguments { get; } = [];
        public string Name => name;
        public string Description => name;
        public string ParametersSchemaJson => """{"type":"object","additionalProperties":false,"required":["supplier"],"properties":{"supplier":{"type":"string"}}}""";

        public Task<AskToolResult> ExecuteAsync(JsonElement arguments, AskToolContext context, CancellationToken cancellationToken)
        {
            Arguments.Add(arguments.GetRawText());
            return Task.FromResult(run(arguments, context));
        }
    }

    private static Result<AiConversationResult> ToolCall(string name, string args) =>
        Result<AiConversationResult>.Success(new AiConversationResult([new AiToolCall("c1", name, args)], null, Metadata));

    private static Result<AiConversationResult> Final(string markdown, params string[] keys) =>
        Result<AiConversationResult>.Success(new AiConversationResult([], JsonSerializer.Serialize(new
        {
            canDetermine = true, answerMarkdown = markdown, citationKeys = keys, actionKeys = Array.Empty<string>(),
            followUps = new[] { "E il preavviso?" }, abstainReason = (string?)null, focusSupplier = "Coupa",
        }), Metadata));

    private static AskToolContext Context(string question = "Quando scade Coupa?", ConversationState? state = null)
    {
        var supplierId = EntityId.New();
        var item = new PortfolioListItem(Guid.NewGuid(), supplierId.Value, Raffa.Documents.Contracts.Domain.ContractDocumentType.OrderForm, 281000m, "CHF",
            new DateOnly(2025, 4, 16), new DateOnly(2026, 4, 15), null, new DateOnly(2026, 1, 15), true, "completed", null, null, null);
        return new AskToolContext(
            TenantId.New(), "alice", question, "it", [("you", "Riassumi il contratto Coupa"), ("raffa", "Ecco il riassunto.")],
            new PortfolioPage([item], 1, 50, 1), new Dictionary<EntityId, string> { [supplierId] = "Coupa" }, null, null,
            AskTurnHints.None with { ConversationState = state });
    }

    private static AgenticAskTurn Turn(IAiGateway gateway, params IAskTool[] tools) =>
        new(gateway, tools, new CapabilityRouting(), new AbstainRecovery(new CapabilityRouting()), new AskTurnOptions { Agentic = true, AgenticMaxToolCalls = 3 });

    [Fact]
    public async Task A_tool_call_runs_and_its_pack_grounds_the_final_answer()
    {
        var gateway = new ScriptedGateway(
            _ => ToolCall("get_contract", """{"supplier":"coupa"}"""),
            request =>
            {
                Assert.Equal(AiConversationMessage.ToolRole, request.Messages[^1].Role);
                Assert.Equal("c1", request.Messages[^1].ToolCallId);
                Assert.Contains("fact:coupa:end", request.Messages[^1].Content);
                return Final("Coupa · OrderForm termina il 15 April 2026 [1].", "fact:coupa:end");
            });
        var tool = new FakeTool("get_contract", (_, ctx) => { ctx.ResolveContract("coupa"); return AskToolResult.FromPack([CoupaEnd]); });

        using var turn = RunContext.BeginTurn();
        var result = await Turn(gateway, tool).RunAsync(Context(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ReplyKind.Answer, result.Reply.Kind);
        Assert.Contains("[1]", result.Reply.AnswerMarkdown);
        Assert.Single(result.Reply.Citations);
        Assert.Equal("Coupa", result.FocusSupplier);
        Assert.Equal(["get_contract"], result.ToolsCalled);
        Assert.False(result.GuardIntervened);
        Assert.Contains("agentic=true", RunContext.TurnDetail);
        Assert.Matches(@"\bstepsRun=1 steps=get-contract failures=0 failedSteps=none marketQueries=0", RunContext.TurnDetail!);
        Assert.Equal(2, gateway.Requests.Count);
        Assert.False(gateway.Requests[0].ForceFinal);
    }

    [Fact]
    public async Task The_role_unavailable_before_any_tool_hands_the_turn_back_to_the_router()
    {
        var gateway = new ScriptedGateway(_ => Result<AiConversationResult>.Failure("no deployment"));

        using var turn = RunContext.BeginTurn();
        var result = await Turn(gateway, new FakeTool("get_contract", (_, _) => AskToolResult.FromPack([]))).RunAsync(Context(), CancellationToken.None);

        Assert.Null(result);
        Assert.Contains("agenticFallback=provider-error", RunContext.TurnDetail);
    }

    [Fact]
    public async Task A_figure_the_pack_does_not_hold_is_regenerated_once_with_the_violation_named()
    {
        var gateway = new ScriptedGateway(
            _ => ToolCall("get_contract", """{"supplier":"Coupa"}"""),
            _ => Final("Coupa · OrderForm termina il 15 April 2026 e costa CHF 999999 [1].", "fact:coupa:end"),
            request =>
            {
                Assert.True(request.ForceFinal);
                Assert.Equal(AiConversationMessage.UserRole, request.Messages[^1].Role);
                Assert.Contains("999999", request.Messages[^1].Content);
                return Final("Coupa · OrderForm termina il 15 April 2026 [1].", "fact:coupa:end");
            });

        var result = await Turn(gateway, new FakeTool("get_contract", (_, _) => AskToolResult.FromPack([CoupaEnd]))).RunAsync(Context(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ReplyKind.Answer, result.Reply.Kind);
        Assert.True(result.GuardIntervened);
        Assert.DoesNotContain("999999", result.Reply.AnswerMarkdown);
        Assert.Equal(3, gateway.Requests.Count);
    }

    [Fact]
    public async Task A_figure_still_unverified_after_the_regeneration_is_shown_and_marked_not_hidden()
    {
        var gateway = new ScriptedGateway(
            _ => ToolCall("get_contract", """{"supplier":"Coupa"}"""),
            _ => Final("Coupa · OrderForm termina il 15 April 2026 [1] e costa CHF 999999.", "fact:coupa:end"),
            _ => Final("Coupa · OrderForm termina il 15 April 2026 [1]; il costo è CHF 999999.", "fact:coupa:end"));

        var result = await Turn(gateway, new FakeTool("get_contract", (_, _) => AskToolResult.FromPack([CoupaEnd]))).RunAsync(Context(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ReplyKind.Answer, result.Reply.Kind);
        Assert.Contains("999999", result.Reply.AnswerMarkdown);
        Assert.True(result.Reply.Provenance.Unverified);
        Assert.True(result.GuardIntervened);
        Assert.False(result.FallbackUsed);
        Assert.Single(result.Reply.Citations);
    }

    [Fact]
    public async Task A_tool_that_ends_the_turn_returns_its_reply_without_a_final_call()
    {
        var consent = new CopilotReply(ReplyKind.Interview, "Posso cercare sul web?", [], [], ReplyProvenance.NoModelCall([]), []);
        var gateway = new ScriptedGateway(_ => ToolCall("web_research", """{"supplier":"x"}"""));
        var tool = new FakeTool("web_research", (_, _) => new AskToolResult("{\"items\":[]}", [], EndTurnReply: consent));

        var result = await Turn(gateway, tool).RunAsync(Context("cerca sul web"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Same(consent, result.Reply);
        Assert.Single(gateway.Requests);
    }

    [Fact]
    public async Task An_artifact_built_by_a_tool_rides_the_reply_payload_with_its_open_action()
    {
        var payload = new ArtifactPayload("art-1", "kpi", "KPI Coupa", "", "Coupa", false, false, [], [], [], "");
        var gateway = new ScriptedGateway(
            _ => ToolCall("compose_artifact", """{"supplier":"Coupa"}"""),
            _ => Final("Ho creato “KPI Coupa” e l'ho aperto nel pannello azioni."));
        var tool = new FakeTool("compose_artifact", (_, _) => new AskToolResult("{\"items\":[],\"summary\":{\"built\":true}}", [], Artifact: payload, UiActions: [UiActionPayload.OpenArtifact("art-1")]));

        var result = await Turn(gateway, tool).RunAsync(Context("crea un kpi"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ReplyKind.Answer, result.Reply.Kind);
        Assert.Equal("art-1", result.Reply.Payload?.Artifact?.Id);
        Assert.Equal(UiActionPayload.OpenActionPanel, Assert.Single(result.Reply.Payload!.UiActions!).Type);
    }

    [Fact]
    public async Task The_call_budget_forces_the_final_answer()
    {
        var calls = 0;
        var gateway = new ScriptedGateway(request =>
        {
            if (request.ForceFinal)
            {
                return Final("Ecco [1].", "fact:coupa:end");
            }

            calls++;
            return ToolCall("get_contract", $$"""{"supplier":"Coupa {{calls}}"}""");
        });

        var result = await Turn(gateway, new FakeTool("get_contract", (_, _) => AskToolResult.FromPack([CoupaEnd]))).RunAsync(Context(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(3, result.ToolsCalled.Count);
        Assert.Equal(ReplyKind.Answer, result.Reply.Kind);
    }

    [Fact]
    public async Task A_failed_tool_does_not_end_the_turn_and_is_named_in_the_audit()
    {
        var gateway = new ScriptedGateway(
            _ => ToolCall("get_market_benchmark", """{"supplier":"Coupa"}"""),
            request =>
            {
                Assert.Contains("error", request.Messages[^1].Content);
                return Final("Nessun benchmark disponibile per Coupa.");
            });
        var tool = new FakeTool("get_market_benchmark", (_, _) => throw new InvalidOperationException("rag down"));

        using var turn = RunContext.BeginTurn();
        var result = await Turn(gateway, tool).RunAsync(Context("come sta Coupa rispetto al mercato?"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ReplyKind.Answer, result.Reply.Kind);
        Assert.Matches(@"failures=1 failedSteps=get-market-benchmark", RunContext.TurnDetail!);
    }

    [Fact]
    public void The_system_prompt_is_exactly_the_reviewed_markdown()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "Raffa.AiFlows", "Ask", "Prompts", "agent", "v1.1.md")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var body = File.ReadAllText(Path.Combine(directory.FullName, "src", "Raffa.AiFlows", "Ask", "Prompts", "agent", "v1.1.md"));
        var start = body.IndexOf("\n\n", StringComparison.Ordinal) + 2;
        var end = body.LastIndexOf("\nBump the version", StringComparison.Ordinal);
        var expected = body[start..end].TrimEnd();
        Assert.Equal(expected.ReplaceLineEndings("\n"), AgentPrompt.SystemPrompt.ReplaceLineEndings("\n"));
    }
}
