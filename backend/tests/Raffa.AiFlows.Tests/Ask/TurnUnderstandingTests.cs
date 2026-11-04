using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Chat.Domain;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.Ask;

/// <summary>The model's reading of a turn: it can only pick from the tenant's suppliers and the intent list, and any
/// unusable answer falls back (null) so the deterministic gate and planner decide the turn.</summary>
public sealed class TurnUnderstandingTests
{
    private static readonly string[] Suppliers = ["GitLab", "UiPath"];
    private static readonly (string Role, string Markdown)[] History = [("you", "Riassumi il contratto GitLab"), ("raffa", "Ecco il riassunto del contratto GitLab …")];

    [Fact]
    public async Task A_follow_up_keeps_the_supplier_in_focus_and_the_intent_and_is_rewritten_standalone()
    {
        var gateway = new ExtractOnlyGateway("""
            {"label":"inDomain","intent":"StructuredFact","supplier":"gitlab","unknownSupplier":null,
             "standaloneQuestion":"Mostrami il contratto GitLab con dei KPI","clarification":null}
            """);

        var result = await new TurnUnderstanding(gateway).ResolveAsync("mostramelo con dei kpi", History, Suppliers, false, default);

        Assert.NotNull(result);
        Assert.Equal("GitLab", result.SupplierName);
        Assert.Equal(AskIntent.StructuredFact, result.Intent);
        Assert.Equal("Mostrami il contratto GitLab con dei KPI", result.StandaloneQuestion);
        Assert.Null(result.Clarification);
        Assert.Contains("[you] Riassumi il contratto GitLab", gateway.Requests.Single().DocumentText);
    }

    [Fact]
    public async Task A_request_to_build_content_carries_its_scope_and_acknowledgement_in_the_same_call()
    {
        var gateway = new ExtractOnlyGateway("""
            {"label":"inDomain","intent":"StructuredFact","supplier":"GitLab","unknownSupplier":null,
             "standaloneQuestion":"Mostrami il contratto GitLab con dei KPI","clarification":null,
             "wantsArtifact":true,"artifactKind":"kpi","usesPortfolio":false,"usesMarket":true,
             "acknowledgement":"Ho preparato i KPI di GitLab nel pannello a destra."}
            """);

        var result = await new TurnUnderstanding(gateway).ResolveAsync("mostramelo con dei kpi", History, Suppliers, false, default);

        Assert.NotNull(result?.Artifact);
        Assert.Equal("kpi", result.Artifact.Kind);
        Assert.True(result.Artifact.UsesMarket);
        Assert.False(result.Artifact.UsesPortfolio);
        Assert.Equal("Ho preparato i KPI di GitLab nel pannello a destra.", result.Artifact.Acknowledgement);
        Assert.Single(gateway.Requests);
    }

    [Fact]
    public async Task A_clarification_never_builds_content()
    {
        var gateway = new ExtractOnlyGateway("""
            {"label":"inDomain","intent":"StructuredFact","supplier":null,"unknownSupplier":null,
             "standaloneQuestion":"Fammi una dashboard","clarification":"Di quale contratto?",
             "wantsArtifact":true,"artifactKind":"dashboard","usesPortfolio":false,"usesMarket":false,"acknowledgement":""}
            """);

        var result = await new TurnUnderstanding(gateway).ResolveAsync("fammi una dashboard", [], Suppliers, false, default);

        Assert.NotNull(result);
        Assert.Null(result.Artifact);
        Assert.Equal("Di quale contratto?", result.Clarification);
    }

    [Fact]
    public async Task A_supplier_that_is_not_on_the_list_is_never_returned_as_one_of_the_tenants()
    {
        var gateway = new ExtractOnlyGateway("""
            {"label":"inDomain","intent":"Clause","supplier":"Oracle","unknownSupplier":"Oracle",
             "standaloneQuestion":"Cosa dice il contratto Oracle sulla responsabilità?","clarification":null}
            """);

        var result = await new TurnUnderstanding(gateway).ResolveAsync("e Oracle?", History, Suppliers, false, default);

        Assert.NotNull(result);
        Assert.Null(result.SupplierName);
        Assert.Equal("Oracle", result.UnknownSupplier);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"label":"nonsense","intent":"StructuredFact","supplier":null,"unknownSupplier":null,"standaloneQuestion":"x","clarification":null}""")]
    [InlineData("""{"label":"inDomain","intent":"StructuredFact","supplier":null,"unknownSupplier":null,"standaloneQuestion":"","clarification":null}""")]
    public async Task An_unusable_answer_changes_nothing(string payload)
    {
        var result = await new TurnUnderstanding(new ExtractOnlyGateway(payload)).ResolveAsync("ciao", History, Suppliers, false, default);

        Assert.Null(result);
    }

    [Fact]
    public async Task A_gateway_failure_changes_nothing()
    {
        var result = await new TurnUnderstanding(new ExtractOnlyGateway(null)).ResolveAsync("ciao", History, Suppliers, false, default);

        Assert.Null(result);
    }

    private sealed class ExtractOnlyGateway(string? payload) : IAiGateway
    {
        public List<AiExtractionRequest> Requests { get; } = [];

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(payload is null
                ? Result<AiExtractionResult>.Failure("down")
                : Result<AiExtractionResult>.Success(new AiExtractionResult(payload, new AiCallMetadata("fake", "1", "v", DateTimeOffset.UnixEpoch, "hash"))));
        }

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
