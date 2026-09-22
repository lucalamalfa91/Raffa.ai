using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Chat.Application.Pack;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Application.WebResearch;
using Raffa.Chat.Tests.TestSupport;
using Raffa.SharedKernel;

namespace Raffa.Chat.Tests.WebResearch;

/// <summary>
/// ADR-030: the composer's input is three strings and its output is guard-approved or an honest
/// abstain — a summary that cites a source the tool did not return, or restates a figure no cited
/// snippet carries, is never shown; an off-topic refusal is a refusal; a missing deployment is a
/// failure the caller words, not a fallback onto the answer role.
/// </summary>
public sealed class WebResearchComposerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly AiCallMetadata Metadata = new("gpt-5.4-research-dev", "2026-03-17", "research-v1", Now, "abc123");

    private static readonly AiWebSource[] Sources =
    [
        new("https://example.com/procurement/saas-renewals", "SaaS renewal benchmarks", "Typical renewals close with a 5-10% uplift cap and 60 to 90 days of notice."),
        new("https://example.org/negotiation/levers", "Levers", "Multi-year commitments are the lever buyers cite most."),
    ];

    private static WebResearchComposer Compose(RecordingResearchGateway gateway, int maxSources = 5) =>
        new(gateway, new WebResearchOptions { Enabled = true, MaxSources = maxSources }, new FixedClock(Now));

    [Fact]
    public async Task A_grounded_summary_becomes_an_unverified_web_answer_with_one_citation_per_source()
    {
        var gateway = new RecordingResearchGateway(new AiResearchResult(
            "Public, unverified: a 5-10% uplift cap is common [1]; multi-year commitments are the main lever [2].",
            Sources, OffTopic: false, Metadata));

        var outcome = await Compose(gateway).ComposeAsync("saas renewal uplift market practice", "MarketPractice", "en");

        Assert.Equal(WebResearchOutcomeKind.Answered, outcome.Kind);
        Assert.Equal(ReplyKind.Answer, outcome.AsReplyKind);
        Assert.False(outcome.GuardIntervened);
        Assert.Equal(2, outcome.Citations.Count);
        Assert.All(outcome.Citations, c => Assert.Equal(PackCorpus.Web, c.Corpus));
        Assert.Equal("example.com · SaaS renewal benchmarks", outcome.Citations[0].Title);
        Assert.Equal("https://example.com/procurement/saas-renewals", outcome.Citations[0].Href);
        Assert.Null(outcome.Citations[0].ContractId);
        Assert.Null(outcome.Citations[0].DocumentId);
        Assert.Equal([PackCorpus.Web], outcome.Provenance.Sources);
        Assert.True(outcome.Provenance.Unverified);
        Assert.Equal("research-v1", outcome.Provenance.PromptVersion);
        Assert.Equal("gpt-5.4-research-dev", outcome.Provenance.ModelId);

        var request = Assert.Single(gateway.Requests);
        Assert.Equal("saas renewal uplift market practice", request.Query);
        Assert.Equal("MarketPractice", request.Purpose);
        Assert.Equal("en", request.Language);
        Assert.Equal(5, request.MaxSources);
        Assert.Equal(WebResearchPrompt.SystemPrompt, request.SystemPrompt);
        Assert.Equal(WebResearchPrompt.Version, request.PromptVersion);
    }

    [Fact]
    public async Task A_figure_no_cited_snippet_carries_is_an_abstain_that_names_the_hosts_not_the_text()
    {
        var gateway = new RecordingResearchGateway(new AiResearchResult(
            "Uplift caps of 25% are common [1].", Sources, OffTopic: false, Metadata));

        var outcome = await Compose(gateway).ComposeAsync("saas renewal uplift", "BenchmarkRange", "en");

        Assert.Equal(WebResearchOutcomeKind.Abstained, outcome.Kind);
        Assert.Equal(ReplyKind.Abstain, outcome.AsReplyKind);
        Assert.True(outcome.GuardIntervened);
        Assert.Contains("25%", outcome.GuardViolation, StringComparison.Ordinal);
        Assert.DoesNotContain("25%", outcome.Markdown, StringComparison.Ordinal);
        Assert.Contains("example.com, example.org", outcome.Markdown, StringComparison.Ordinal);
        Assert.Empty(outcome.Citations);
        Assert.Empty(outcome.Provenance.Sources);
        Assert.Equal(2, outcome.SourceCount);
    }

    [Fact]
    public async Task A_marker_past_the_source_list_is_an_abstain()
    {
        var gateway = new RecordingResearchGateway(new AiResearchResult(
            "Common practice [3].", Sources, OffTopic: false, Metadata));

        var outcome = await Compose(gateway).ComposeAsync("saas renewal practice", "MarketPractice", "en");

        Assert.Equal(WebResearchOutcomeKind.Abstained, outcome.Kind);
        Assert.Contains("[3]", outcome.GuardViolation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_sources_at_all_is_an_abstain_never_an_answer()
    {
        var gateway = new RecordingResearchGateway(new AiResearchResult(
            "Everyone knows caps are 5%.", [], OffTopic: false, Metadata));

        var outcome = await Compose(gateway).ComposeAsync("saas renewal practice", "MarketPractice", "it");

        Assert.Equal(WebResearchOutcomeKind.Abstained, outcome.Kind);
        Assert.Contains("nessuna", outcome.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Off_topic_is_a_refusal_with_no_citations()
    {
        var gateway = new RecordingResearchGateway(new AiResearchResult(string.Empty, [], OffTopic: true, Metadata));

        var outcome = await Compose(gateway).ComposeAsync("best pizza in naples", "MarketPractice", "en");

        Assert.Equal(WebResearchOutcomeKind.Refused, outcome.Kind);
        Assert.Equal(ReplyKind.Refusal, outcome.AsReplyKind);
        Assert.Empty(outcome.Citations);
        Assert.Contains("procurement topics", outcome.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_gateway_failure_is_reported_as_unavailable_never_answered_from_the_answer_role()
    {
        var gateway = new RecordingResearchGateway(Result<AiResearchResult>.Failure("AiGateway:Models:Research is not configured"));

        var outcome = await Compose(gateway).ComposeAsync("saas renewal practice", "MarketPractice", "en");

        Assert.Equal(WebResearchOutcomeKind.Failed, outcome.Kind);
        Assert.Contains("Models:Research", outcome.Error, StringComparison.Ordinal);
        Assert.Contains("not available", outcome.Markdown, StringComparison.Ordinal);
        Assert.Empty(outcome.Citations);
    }

    [Fact]
    public async Task The_request_that_leaves_raffa_has_no_pack_slot_and_the_answer_role_is_never_called()
    {
        var gateway = new RecordingResearchGateway(new AiResearchResult(
            "Public, unverified: a 5-10% uplift cap is common [1].", Sources, OffTopic: false, Metadata));

        await Compose(gateway, maxSources: 3).ComposeAsync("saas renewal uplift", "MarketPractice", "en");

        Assert.Empty(gateway.AnswerCalls);
        Assert.DoesNotContain(
            typeof(AiResearchRequest).GetProperties(),
            p => p.Name.Contains("Pack", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Evidence", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3, gateway.Requests.Single().MaxSources);
    }

    private sealed class RecordingResearchGateway(Result<AiResearchResult> result) : IAiGateway
    {
        public List<AiResearchRequest> Requests { get; } = [];

        public List<AiAnswerRequest> AnswerCalls { get; } = [];

        public RecordingResearchGateway(AiResearchResult result) : this(Result<AiResearchResult>.Success(result))
        {
        }

        public Task<Result<AiResearchResult>> ResearchAsync(AiResearchRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(result);
        }

        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default)
        {
            AnswerCalls.Add(request);
            throw new InvalidOperationException("The research composer must never call the answer role.");
        }

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
