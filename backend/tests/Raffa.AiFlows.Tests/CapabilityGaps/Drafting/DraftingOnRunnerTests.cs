using System.Text.Json;
using System.Text.Json.Serialization;
using Raffa.AiFlows.CapabilityGaps.Agents;
using Raffa.AiFlows.CapabilityGaps.Drafting;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Playbook;
using Raffa.AiGateway;
using Raffa.AiGateway.Agents;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.CapabilityGaps.Drafting;

/// <summary>Plan A-08 / F4-T05: the drafting agents as runner steps, their requests byte-identical
/// to the pre-runner workflow's, the draft type, the tone check, and the opt-in reuse of the turn's
/// council plays. The draft is copyable text only (decision D4).</summary>
public sealed class DraftingOnRunnerTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(10);

    private static PackItem Item(string key, string corpus, string title, string snippet, params PackValue[] values) =>
        new(key, corpus, title, null, null, null, snippet, "/contracts/x", null, null, "test", values, "contract-x");

    private static IReadOnlyList<PackItem> Pack(bool withPlay = true)
    {
        var pack = new List<PackItem>
        {
            Item("calc:when-you-must-move", PackCorpus.Calc, "When you must move", "Notice by 2027-10-13, renewal on 2028-04-10.",
                new PackValue("cancellationDeadline", "2027-10-13", PackValueKind.Date, null), new PackValue("renewalDate", "2028-04-10", PackValueKind.Date, null)),
            Item("fact:x:renewal", PackCorpus.Tenant, "ServiceNow · OrderForm", "ServiceNow ends on 2028-04-10.",
                new PackValue("annualSpend", "230000", PackValueKind.Amount, "EUR"), new PackValue("endDate", "2028-04-10", PackValueKind.Date, null)),
            Item("calc:savings-target", PackCorpus.Calc, "ServiceNow — saving target and lever coverage", "Target EUR 20000 is 8.7% of the annual spend of EUR 230000.",
                new PackValue("targetAmount", "20000", PackValueKind.Amount, "EUR")),
            NegotiationPlaybook.ToPackItem(NegotiationPlaybook.All[0]),
        };

        if (withPlay)
        {
            pack.Insert(3, Item("calc:council:play[1]", PackCorpus.Calc, "Play 1 — Market discount",
                "Ask for the 9% discount peers obtained, worth EUR 20700 a year. Timing: before 2027-10-13. Grounded in: calc:lever[market-discount].",
                new PackValue("estimatedHigh", "20700", PackValueKind.Amount, "EUR"), new PackValue("percent", "9", PackValueKind.Percentage, null)));
        }

        return pack;
    }

    private static DraftRequest Request(IReadOnlyList<PackItem>? pack = null, string question = "Puoi scrivermi la mail per il rinnovo ServiceNow?") =>
        new(question, "it", "ServiceNow", pack ?? Pack(), null);

    private static FixtureAiGateway Fixture() =>
        new(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions());

    // ---------------------------------------------------------------- definitions

    [Fact]
    public void The_definitions_pin_names_version_prompts_schemas_and_policies()
    {
        var planner = new OfferPlannerStep(Deadline);
        var writer = new NegotiationWriterStep(Deadline, new DraftWriterVerifier(Pack(), 2500, enforceTone: false));

        Assert.Equal("offer-planner", planner.Definition.Name);
        Assert.Equal("negotiation-writer", writer.Definition.Name);
        Assert.All(new[] { planner.Definition, writer.Definition }, d =>
        {
            Assert.Equal("draft-v1", d.Version);
            Assert.Equal(AgentRole.Analyze, d.Role);
            Assert.Null(d.LanguageDirective);
            Assert.Equal(d.Prompt, d.SystemPrompt);
            Assert.Equal(Deadline, d.Deadline);
            Assert.Empty(d.Validate());
        });

        Assert.Equal(DraftingAgents.OfferPlannerPrompt, planner.Definition.SystemPrompt);
        Assert.Equal(DraftingAgents.NegotiationWriterPrompt, writer.Definition.SystemPrompt);
        Assert.Equal(DraftingAgents.OfferPlanSchema, planner.Definition.JsonSchema);
        Assert.Equal(DraftingAgents.EmailDraftSchema, writer.Definition.JsonSchema);
        Assert.Equal(AgentFailurePolicy.Skip, planner.Definition.FailurePolicy);
        Assert.Equal(AgentFailurePolicy.Fail, writer.Definition.FailurePolicy);
        Assert.Equal("draft-template-v1", DraftingAgents.TemplateVersion);
    }

    // ---------------------------------------------------------------- byte-identical requests

    // A frozen copy of the wire shapes the pre-runner workflow serialized by hand.
    private sealed record OldGoal(decimal? TargetAmount, decimal? TargetPercent, string? Currency, int? WindowDays, string? WindowLabel);

    private sealed record OldValue(string Key, string Value, string Kind, string? Currency);

    private sealed record OldItem(string CitationKey, string Corpus, string Title, string? Subtitle, string Snippet, IReadOnlyList<OldValue> Values);

    private sealed record OldPlannerInput(string Question, string Language, string Supplier, OldGoal? Goal, IReadOnlyList<OldItem> Items);

    private sealed record OldWriterInput(string Question, string Language, string Supplier, OldGoal? Goal, DraftPlan Plan, IReadOnlyList<OldItem> Items);

    private static readonly JsonSerializerOptions OldJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    [Fact]
    public async Task The_requests_reaching_the_gateway_are_byte_identical_to_the_hand_built_ones()
    {
        var recording = new RecordingGateway(Fixture());
        var pack = Pack();

        await new NegotiationDraftingWorkflow(recording, new DraftingOptions()).DraftAsync(Request(pack));

        var items = pack.Select(i => new OldItem(
            i.CitationKey, i.Corpus, i.Title, i.Subtitle, i.Snippet,
            i.Values.Select(v => new OldValue(v.Key, v.Value, v.Kind.ToString(), v.Currency)).ToList())).ToList();
        var question = Request().Question;

        var planner = recording.Requests.First(r => r.AgentName == DraftingAgents.OfferPlannerName);
        Assert.Equal(JsonSerializer.Serialize(new OldPlannerInput(question, "it", "ServiceNow", null, items), OldJson), planner.InputJson);
        Assert.Equal(DraftingAgents.OfferPlannerPrompt, planner.SystemPrompt);
        Assert.Equal(DraftingAgents.OfferPlanSchema, planner.JsonSchema);
        Assert.Equal(DraftingAgents.Version, planner.PromptVersion);

        var writer = recording.Requests.First(r => r.AgentName == DraftingAgents.NegotiationWriterName);
        Assert.Equal(DraftingAgents.NegotiationWriterPrompt, writer.SystemPrompt);
        Assert.Equal(DraftingAgents.EmailDraftSchema, writer.JsonSchema);
        Assert.Equal(DraftingAgents.Version, writer.PromptVersion);
        Assert.Contains("\"plan\":{", writer.InputJson, StringComparison.Ordinal);
        Assert.StartsWith("{\"question\":", writer.InputJson, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- F4-T05

    [Fact]
    public async Task By_default_the_planner_still_runs_even_with_council_plays_in_the_pack()
    {
        var recording = new RecordingGateway(Fixture());

        var draft = await new NegotiationDraftingWorkflow(recording, new DraftingOptions()).DraftAsync(Request());

        Assert.Equal([DraftingAgents.OfferPlannerName, DraftingAgents.NegotiationWriterName], recording.Agents);
        Assert.False(draft.ReusedCouncilPlays);
    }

    [Fact]
    public async Task With_ReuseCouncilPlays_the_turn_plays_replace_the_planner_call()
    {
        var recording = new RecordingGateway(Fixture());

        var draft = await new NegotiationDraftingWorkflow(recording, new DraftingOptions { ReuseCouncilPlays = true }).DraftAsync(Request());

        Assert.Equal([DraftingAgents.NegotiationWriterName], recording.Agents);
        Assert.True(draft.ReusedCouncilPlays);
        Assert.Equal(DraftSource.Model, draft.Source);
        Assert.Contains("Ask for the 9% discount peers obtained, worth EUR 20700 a year.", recording.Requests[0].InputJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReuseCouncilPlays_without_plays_in_the_pack_still_plans()
    {
        var recording = new RecordingGateway(Fixture());

        var draft = await new NegotiationDraftingWorkflow(recording, new DraftingOptions { ReuseCouncilPlays = true })
            .DraftAsync(Request(Pack(withPlay: false)));

        Assert.Equal(DraftingAgents.OfferPlannerName, recording.Agents[0]);
        Assert.False(draft.ReusedCouncilPlays);
    }

    // ---------------------------------------------------------------- failure lines and attempts

    [Fact]
    public async Task A_writer_gateway_failure_falls_back_to_the_template_with_one_failure_line_and_no_attempts()
    {
        var failing = new ScriptedWriterGateway(Fixture(), null);

        var draft = await new NegotiationDraftingWorkflow(failing, new DraftingOptions()).DraftAsync(Request());

        Assert.Equal(DraftSource.Template, draft.Source);
        Assert.Equal(0, draft.Attempts);
        Assert.Equal([$"{DraftingAgents.NegotiationWriterName}: simulated outage"], draft.Failures.Where(f => f.StartsWith("negotiation-writer", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_guard_violation_retries_once_with_the_violation_named_and_reports_both_lines()
    {
        const string Bad = """{"subject":"Rinnovo","body":"Gentile team, vedi [1] e https://x.example","usedCitationKeys":[]}""";
        var scripted = new ScriptedWriterGateway(Fixture(), Bad);

        var draft = await new NegotiationDraftingWorkflow(scripted, new DraftingOptions()).DraftAsync(Request());

        Assert.Equal(2, scripted.WriterCalls);
        Assert.Contains("rejected by an automated grounding check", scripted.LastWriterPrompt, StringComparison.Ordinal);
        Assert.Equal(DraftSource.Template, draft.Source);
        Assert.Equal(2, draft.Attempts);
        var lines = draft.Failures.Where(f => f.StartsWith("negotiation-writer", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, lines.Count);
        Assert.StartsWith("negotiation-writer (retry): ", lines[1], StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- tone

    private const string Informal = """{"subject":"Rinnovo ServiceNow","body":"Ciao ragazzi!! Accettiamo il vostro prezzo, grazie.","usedCitationKeys":["fact:x:renewal"]}""";

    [Fact]
    public async Task Tone_findings_are_reported_but_change_nothing_by_default()
    {
        var scripted = new ScriptedWriterGateway(Fixture(), Informal);

        var draft = await new NegotiationDraftingWorkflow(scripted, new DraftingOptions()).DraftAsync(Request());

        Assert.Equal(1, scripted.WriterCalls);
        Assert.Equal(DraftSource.Model, draft.Source);
        Assert.Contains("informal-greeting", draft.ToneFindings!);
        Assert.Contains("exclamation", draft.ToneFindings!);
        Assert.Contains("commitment", draft.ToneFindings!);
        Assert.Contains("no-signature-placeholder", draft.ToneFindings!);
    }

    [Fact]
    public async Task With_EnforceTone_an_informal_draft_is_sent_back_once_and_then_falls_back_to_the_template()
    {
        var scripted = new ScriptedWriterGateway(Fixture(), Informal);

        var draft = await new NegotiationDraftingWorkflow(scripted, new DraftingOptions { EnforceTone = true }).DraftAsync(Request());

        Assert.Equal(2, scripted.WriterCalls);
        Assert.Contains("automated tone check", scripted.LastWriterPrompt, StringComparison.Ordinal);
        Assert.Equal(DraftSource.Template, draft.Source);
        Assert.Empty(draft.ToneFindings!);
    }

    [Fact]
    public void The_template_and_the_fixture_draft_read_as_formal_letters()
    {
        Assert.Empty(DraftToneChecker.Check(null, NegotiationEmailTemplate.Build("it", "ServiceNow", Pack(), null).Body));
        Assert.Empty(DraftToneChecker.Check(null, NegotiationEmailTemplate.Build("en", "ServiceNow", Pack(), null).Body));
    }

    [Theory]
    [InlineData("Dear Acme team,\n\nPlease send a revised proposal.\n\nKind regards,\n[Name]", "")]
    [InlineData("Hey team, quick one", "informal-greeting")]
    [InlineData("Please send a revised proposal.\n[Name]", "no-greeting")]
    [InlineData("Dear team,\n\nWe agree to the renewal.\n[Name]", "commitment")]
    [InlineData("Dear team,\n\nTODO check price\n[Name]", "internal-note")]
    [InlineData("Dear team,\n\nGreat 😀\n[Name]", "emoji")]
    [InlineData("Dear team,\n\nPlease reply.", "no-signature-placeholder")]
    public void The_tone_checker_names_what_is_wrong(string body, string expected)
    {
        var findings = DraftToneChecker.Check("Renewal", body);

        if (expected.Length == 0)
        {
            Assert.Empty(findings);
        }
        else
        {
            Assert.Contains(expected, findings);
        }
    }

    // ---------------------------------------------------------------- draft type

    [Theory]
    [InlineData("Puoi scrivermi la mail per il rinnovo ServiceNow?", DraftType.Renegotiation)]
    [InlineData("Write an email to renegotiate the price", DraftType.Renegotiation)]
    [InlineData("manda la disdetta via PEC", DraftType.CancellationNonRenewal)]
    [InlineData("Draft the notice that we will not renew", DraftType.CancellationNonRenewal)]
    [InlineData("Écris la résiliation du contrat", DraftType.CancellationNonRenewal)]
    [InlineData("Kündigung schreiben", DraftType.CancellationNonRenewal)]
    [InlineData("scrivi una controproposta alla loro offerta", DraftType.CounterProposal)]
    [InlineData("Reply with a counter-proposal", DraftType.CounterProposal)]
    [InlineData("", DraftType.Renegotiation)]
    public void The_classifier_labels_the_communication(string question, DraftType expected)
    {
        Assert.Equal(expected, DraftTypeClassifier.Classify(question));
    }

    [Fact]
    public async Task The_outcome_carries_the_draft_type_and_drafting_never_changes_with_it()
    {
        var recording = new RecordingGateway(Fixture());

        var draft = await new NegotiationDraftingWorkflow(recording, new DraftingOptions())
            .DraftAsync(Request(question: "manda la disdetta via PEC a ServiceNow"));

        Assert.Equal(DraftType.CancellationNonRenewal, draft.Type);
        Assert.Equal(DraftingAgents.NegotiationWriterPrompt, recording.Requests.Last().SystemPrompt);
    }

    // ---------------------------------------------------------------- D4

    [Fact]
    public void The_workflow_takes_only_the_gateway_the_options_and_the_runner()
    {
        var constructor = Assert.Single(typeof(NegotiationDraftingWorkflow).GetConstructors());

        Assert.Equal(
            [typeof(IAiGateway), typeof(DraftingOptions), typeof(StepRunner)],
            constructor.GetParameters().Select(p => p.ParameterType));
    }

    // ---------------------------------------------------------------- doubles

    private sealed class RecordingGateway(IAiGateway inner) : IAiGateway
    {
        public List<AiAnalysisRequest> Requests { get; } = [];

        public IReadOnlyList<string> Agents => Requests.Select(r => r.AgentName).ToList();

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) => inner.ClassifyAsync(request, cancellationToken);
        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) => inner.ExtractAsync(request, cancellationToken);
        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) => inner.EmbedAsync(request, cancellationToken);
        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) => inner.AnswerAsync(request, cancellationToken);
        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) => inner.OcrAsync(request, cancellationToken);

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return inner.AnalyzeAsync(request, cancellationToken);
        }
    }

    /// <summary>The planner runs on the fixture; every writer call returns the scripted payload
    /// (a gateway failure when it is null).</summary>
    private sealed class ScriptedWriterGateway(IAiGateway inner, string? writerPayload) : IAiGateway
    {
        public int WriterCalls { get; private set; }

        public string LastWriterPrompt { get; private set; } = string.Empty;

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) => inner.ClassifyAsync(request, cancellationToken);
        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) => inner.ExtractAsync(request, cancellationToken);
        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) => inner.EmbedAsync(request, cancellationToken);
        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) => inner.AnswerAsync(request, cancellationToken);
        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) => inner.OcrAsync(request, cancellationToken);

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            if (request.AgentName != DraftingAgents.NegotiationWriterName)
            {
                return inner.AnalyzeAsync(request, cancellationToken);
            }

            WriterCalls++;
            LastWriterPrompt = request.SystemPrompt;
            if (writerPayload is null)
            {
                return Task.FromResult(Result<AiAnalysisResult>.Failure("simulated outage"));
            }

            return Task.FromResult(Result<AiAnalysisResult>.Success(new AiAnalysisResult(
                writerPayload,
                new AiCallMetadata("scripted", "1", request.PromptVersion, DateTimeOffset.UtcNow, "hash"))));
        }
    }
}
