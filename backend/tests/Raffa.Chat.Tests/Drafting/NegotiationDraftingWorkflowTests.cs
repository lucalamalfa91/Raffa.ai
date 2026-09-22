using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.Chat.Application.Drafting;
using Raffa.Chat.Application.Pack;
using Raffa.Chat.Application.Playbook;
using Raffa.SharedKernel;

namespace Raffa.Chat.Tests.Drafting;

/// <summary>ADR-030 D3: planner then writer, guarded, one retry, then the template — and never
/// nothing.</summary>
public sealed class NegotiationDraftingWorkflowTests
{
    private static PackItem Item(string key, string corpus, string title, string snippet, params PackValue[] values) =>
        new(key, corpus, title, null, null, null, snippet, "/contracts/x", null, null, "test", values, "contract-x");

    private static IReadOnlyList<PackItem> Pack() =>
    [
        Item("calc:when-you-must-move", PackCorpus.Calc, "When you must move", "Notice by 2027-10-13, renewal on 2028-04-10.",
            new PackValue("cancellationDeadline", "2027-10-13", PackValueKind.Date, null), new PackValue("renewalDate", "2028-04-10", PackValueKind.Date, null)),
        Item("fact:x:renewal", PackCorpus.Tenant, "ServiceNow · OrderForm", "ServiceNow ends on 2028-04-10.",
            new PackValue("annualSpend", "230000", PackValueKind.Amount, "EUR"), new PackValue("endDate", "2028-04-10", PackValueKind.Date, null)),
        Item("calc:savings-target", PackCorpus.Calc, "ServiceNow — saving target and lever coverage", "Target EUR 20000 is 8.7% of the annual spend of EUR 230000.",
            new PackValue("targetAmount", "20000", PackValueKind.Amount, "EUR")),
        Item("calc:council:play[1]", PackCorpus.Calc, "Play 1 — Market discount", "Ask for the 9% discount peers obtained, worth EUR 20700 a year. Timing: before 2027-10-13. Grounded in: calc:lever[market-discount].",
            new PackValue("estimatedHigh", "20700", PackValueKind.Amount, "EUR"), new PackValue("percent", "9", PackValueKind.Percentage, null)),
        NegotiationPlaybook.ToPackItem(NegotiationPlaybook.All[0]),
    ];

    private static DraftRequest Request(IReadOnlyList<PackItem>? pack = null, string language = "it") =>
        new("Puoi scrivermi la mail per il rinnovo ServiceNow?", language, "ServiceNow", pack ?? Pack(), null);

    private static FixtureAiGateway Fixture() =>
        new(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions());

    private static NegotiationDraftingWorkflow Workflow(IAiGateway gateway, DraftingOptions? options = null) =>
        new(gateway, options ?? new DraftingOptions());

    [Fact]
    public async Task Runs_planner_then_writer_on_the_fixture_and_passes_first_time()
    {
        var recording = new RecordingGateway(Fixture());

        var draft = await Workflow(recording).DraftAsync(Request());

        Assert.Equal([DraftingAgents.OfferPlannerName, DraftingAgents.NegotiationWriterName], recording.Agents);
        Assert.Equal(DraftSource.Model, draft.Source);
        Assert.Equal(1, draft.Attempts);
        Assert.Empty(draft.Failures);
        Assert.NotNull(draft.Metadata);
        Assert.Contains("ServiceNow", draft.Subject, StringComparison.Ordinal);
        Assert.Contains("Gentile team ServiceNow", draft.Body, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\[\d+\]", draft.Body);
        Assert.NotEmpty(draft.UsedCitationKeys);
        Assert.All(draft.UsedCitationKeys, key => Assert.Contains(key, Pack().Select(i => i.CitationKey)));
        Assert.True(DraftGuard.Validate(draft.Subject, draft.Body, draft.UsedCitationKeys, Pack(), 2500).Passed);

        Assert.Contains("\"plan\"", recording.Inputs[DraftingAgents.NegotiationWriterName], StringComparison.Ordinal);
        Assert.Contains("\"language\":\"it\"", recording.Inputs[DraftingAgents.OfferPlannerName], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retries_once_with_the_violation_named_then_falls_back_to_the_template()
    {
        var scripted = new ScriptedWriterGateway(Fixture(),
            """{"subject":"Rinnovo","body":"Chiediamo EUR 99999 di sconto [1].","usedCitationKeys":["fact:x:renewal"]}""");

        var draft = await Workflow(scripted).DraftAsync(Request());

        Assert.Equal(2, scripted.WriterCalls);
        Assert.Contains("rejected by an automated grounding check", scripted.LastWriterPrompt, StringComparison.Ordinal);
        Assert.Equal(DraftSource.Template, draft.Source);
        Assert.Equal(2, draft.Attempts);
        Assert.Equal(2, draft.Failures.Count(f => f.StartsWith(DraftingAgents.NegotiationWriterName, StringComparison.Ordinal)));
        Assert.Contains("Gentile team ServiceNow", draft.Body, StringComparison.Ordinal);
        Assert.True(DraftGuard.Validate(draft.Subject, draft.Body, draft.UsedCitationKeys, Pack(), 2500).Passed);
    }

    [Fact]
    public async Task Gateway_failure_falls_back_to_the_template()
    {
        var failing = new FailingAgentGateway(Fixture(), DraftingAgents.NegotiationWriterName);

        var draft = await Workflow(failing).DraftAsync(Request(language: "en"));

        Assert.Equal(DraftSource.Template, draft.Source);
        Assert.Contains("Dear ServiceNow team", draft.Body, StringComparison.Ordinal);
        Assert.Single(draft.Failures, f => f.StartsWith(DraftingAgents.NegotiationWriterName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Planner_failure_still_writes_from_the_pack_plays()
    {
        var failing = new FailingAgentGateway(Fixture(), DraftingAgents.OfferPlannerName);

        var draft = await Workflow(failing).DraftAsync(Request());

        Assert.Equal(DraftSource.Model, draft.Source);
        Assert.Contains("Ask for the 9% discount peers obtained", draft.Body, StringComparison.Ordinal);
        Assert.Single(draft.Failures, f => f.StartsWith(DraftingAgents.OfferPlannerName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Disabled_or_a_thin_pack_uses_the_template_without_a_call()
    {
        var recording = new RecordingGateway(Fixture());

        var disabled = await Workflow(recording, new DraftingOptions { Enabled = false }).DraftAsync(Request());
        var thin = await Workflow(recording).DraftAsync(Request(Pack().Take(1).ToList()));

        Assert.Equal(DraftSource.Template, disabled.Source);
        Assert.Equal(DraftSource.Template, thin.Source);
        Assert.Equal(0, disabled.Attempts);
        Assert.Empty(recording.Agents);
    }

    // ----- doubles -----

    private sealed class RecordingGateway(IAiGateway inner) : IAiGateway
    {
        public List<string> Agents { get; } = [];

        public Dictionary<string, string> Inputs { get; } = new(StringComparer.Ordinal);

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) => inner.ClassifyAsync(request, cancellationToken);
        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) => inner.ExtractAsync(request, cancellationToken);
        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) => inner.EmbedAsync(request, cancellationToken);
        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) => inner.AnswerAsync(request, cancellationToken);
        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) => inner.OcrAsync(request, cancellationToken);

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            Agents.Add(request.AgentName);
            Inputs[request.AgentName] = request.InputJson;
            return inner.AnalyzeAsync(request, cancellationToken);
        }
    }

    private sealed class FailingAgentGateway(IAiGateway inner, string failingAgent) : IAiGateway
    {
        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) => inner.ClassifyAsync(request, cancellationToken);
        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) => inner.ExtractAsync(request, cancellationToken);
        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) => inner.EmbedAsync(request, cancellationToken);
        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) => inner.AnswerAsync(request, cancellationToken);
        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) => inner.OcrAsync(request, cancellationToken);

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default) =>
            request.AgentName == failingAgent
                ? Task.FromResult(Result<AiAnalysisResult>.Failure("simulated outage"))
                : inner.AnalyzeAsync(request, cancellationToken);
    }

    /// <summary>The planner runs on the fixture; every writer call returns the scripted payload.</summary>
    private sealed class ScriptedWriterGateway(IAiGateway inner, string writerPayload) : IAiGateway
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
            return Task.FromResult(Result<AiAnalysisResult>.Success(new AiAnalysisResult(
                writerPayload,
                new AiCallMetadata("scripted", "1", request.PromptVersion, DateTimeOffset.UtcNow, "hash"))));
        }
    }
}
