using Raffa.AiFlows.Tests.TestSupport;
using Raffa.AiFlows.WebResearch.Configuration;
using Raffa.AiFlows.WebResearch.Orchestration;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Chat.Application.WebResearch;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.WebResearch.Orchestration;

/// <summary>F3-T03: the reserved budget unit goes back for a transport failure always, and for an outcome
/// with no source only when <see cref="WebResearchOptions.ChargeOnlyWhenSourcesFound"/> is on.</summary>
public sealed class WebResearchBudgetChargeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly AiCallMetadata Metadata = new("m", "v", "research-v1", Now, "h");
    private static readonly WebResearchReservation Reservation = new(new TenantId(Guid.NewGuid()), new DateOnly(2026, 9, 22));

    private sealed class CountingBudget : IWebResearchBudget
    {
        public int Released { get; private set; }

        public Task<bool> HasRemainingAsync(TenantId tenantId, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<WebResearchReservation?> TryReserveAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WebResearchReservation?>(Reservation);

        public Task ReleaseAsync(WebResearchReservation reservation, CancellationToken cancellationToken = default)
        {
            Released++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubGateway(Result<AiResearchResult> result) : IAiGateway
    {
        public Task<Result<AiResearchResult>> ResearchAsync(AiResearchRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);

        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static async Task<(WebResearchOutcome Outcome, bool Released, int ReleaseCalls)> RunAsync(
        Result<AiResearchResult> result, bool chargeOnlyWhenSourcesFound)
    {
        var options = new WebResearchOptions { Enabled = true, ChargeOnlyWhenSourcesFound = chargeOnlyWhenSourcesFound };
        var budget = new CountingBudget();
        var composer = new WebResearchComposer(new StubGateway(result), options, new FixedClock(Now));
        // Only the composer and the budget are used by the reserved call; the other collaborators are not.
        var flow = new WebResearchFlow(
            WebResearchOptionsMonitor.Static(options), composer, budget, null!, null!, new FixedClock(Now), null!,
            new Raffa.AiGateway.Logging.KeyedQueryHasher(() => null));

        var (outcome, released) = await flow.ComposeReservedAsync(Reservation, "saas renewal uplift", "MarketPractice", "en", CancellationToken.None);
        return (outcome, released, budget.Released);
    }

    private static Result<AiResearchResult> OffTopic() => Result<AiResearchResult>.Success(new AiResearchResult("", [], OffTopic: true, Metadata));

    [Fact]
    public async Task By_default_an_off_topic_refusal_keeps_the_unit_spent()
    {
        var (outcome, released, calls) = await RunAsync(OffTopic(), chargeOnlyWhenSourcesFound: false);

        Assert.Equal(WebResearchOutcomeKind.Refused, outcome.Kind);
        Assert.False(released);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task With_the_option_on_an_outcome_without_sources_gives_the_unit_back()
    {
        var (outcome, released, calls) = await RunAsync(OffTopic(), chargeOnlyWhenSourcesFound: true);

        Assert.Equal(WebResearchOutcomeKind.Refused, outcome.Kind);
        Assert.True(released);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task With_the_option_on_an_outcome_with_sources_stays_charged()
    {
        var sources = new[] { new AiWebSource("https://example.com/a", "A", "Typical renewals close with a 5-10% uplift cap.") };
        var result = Result<AiResearchResult>.Success(new AiResearchResult("Public: uplift caps of 5-10% are common [1].", sources, OffTopic: false, Metadata));

        var (outcome, released, calls) = await RunAsync(result, chargeOnlyWhenSourcesFound: true);

        Assert.Equal(WebResearchOutcomeKind.Answered, outcome.Kind);
        Assert.False(released);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task A_transport_failure_is_refunded_whatever_the_option_says()
    {
        foreach (var option in new[] { false, true })
        {
            var (outcome, released, calls) = await RunAsync(Result<AiResearchResult>.Failure("research endpoint unreachable"), option);

            Assert.Equal(WebResearchOutcomeKind.Failed, outcome.Kind);
            Assert.True(released);
            Assert.Equal(1, calls);
        }
    }
}
