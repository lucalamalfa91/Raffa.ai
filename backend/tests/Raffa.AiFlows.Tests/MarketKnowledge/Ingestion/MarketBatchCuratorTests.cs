using System.Text.Json;
using Raffa.AiFlows.MarketKnowledge.Ingestion;
using Raffa.AiFlows.Tests.TestSupport;
using Raffa.AiGateway;
using Raffa.AiGateway.Agents;
using Raffa.AiGateway.Contracts;
using Raffa.Market.Contracts;
using Raffa.Market.Ingestion;
using Raffa.Market.Mock;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.MarketKnowledge.Ingestion;

/// <summary>
/// Plan A-11 / F7-T09: the ingest batch's data-quality agent and provenance curator, as runner steps.
/// No live model: every gateway here is a script.
/// </summary>
public sealed class MarketBatchCuratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    private static MarketBatchCurator Curator(ScriptedAnalyzeGateway gateway, bool enabled = true, bool provenance = true) =>
        new(new StepRunner(gateway), new MarketCurationOptions { Enabled = enabled, CurateProvenance = provenance }, new FixedClock(Now));

    private static MarketDeal Deal(string id, string? unit = "per user per month", decimal p25 = 10m, decimal p50 = 12m, decimal p75 = 14m) =>
        SampleDeal.Create(recordId: id, unitMetric: unit, unitPriceP25: p25, unitPriceP50: p50, unitPriceP75: p75);

    private static string Decisions(params (string Id, string Verdict, string[] Reasons)[] items) =>
        JsonSerializer.Serialize(new
        {
            decisions = items.Select(i => new { recordId = i.Id, verdict = i.Verdict, reasons = i.Reasons }),
        });

    private static string Units(params (string Id, string? Unit)[] items) =>
        JsonSerializer.Serialize(new { decisions = items.Select(i => new { recordId = i.Id, unit = i.Unit }) });

    // ---------------------------------------------------------------- the definitions

    [Fact]
    public void The_definitions_validate_and_pin_name_version_and_policy()
    {
        var quality = new MarketDataQualityStep().Definition;
        var provenance = new MarketProvenanceCuratorStep().Definition;

        Assert.Empty(quality.Validate());
        Assert.Empty(provenance.Validate());
        Assert.Equal("market-data-quality", quality.Name);
        Assert.Equal("market-provenance-curator", provenance.Name);
        Assert.All(new[] { quality, provenance }, d =>
        {
            Assert.Equal(MarketIngestAgents.Version, d.Version);
            Assert.Equal(AgentRole.Analyze, d.Role);
            Assert.Equal(AgentFailurePolicy.Fallback, d.FailurePolicy);
        });
    }

    [Fact]
    public void The_prompts_never_call_the_data_demo_or_synthetic()
    {
        foreach (var prompt in new[] { MarketIngestAgents.DataQualityPrompt, MarketIngestAgents.ProvenanceCuratorPrompt })
        {
            Assert.DoesNotContain("demo", prompt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("mock", prompt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("synthetic", prompt, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---------------------------------------------------------------- curation

    [Fact]
    public async Task Disabled_it_is_a_pass_through_with_no_ai_call()
    {
        var gateway = new ScriptedAnalyzeGateway();
        var deals = new[] { Deal("A"), Deal("B", p25: 99m) };

        var outcome = await Curator(gateway, enabled: false).CurateAsync(deals);

        Assert.Equal(deals, outcome.Accepted);
        Assert.Empty(outcome.Rejected);
        Assert.Empty(gateway.Requests);
    }

    [Fact]
    public async Task A_record_breaking_a_hard_rule_is_rejected_without_the_model_and_the_rest_are_reviewed()
    {
        var gateway = new ScriptedAnalyzeGateway(Decisions(("GOOD", "Accept", [])));
        var broken = Deal("BAD", p25: 20m, p50: 12m, p75: 14m);

        var outcome = await Curator(gateway).CurateAsync([Deal("GOOD"), broken]);

        Assert.Equal(["GOOD"], outcome.Accepted.Select(d => d.RecordId));
        var rejection = Assert.Single(outcome.Rejected);
        Assert.Equal("BAD", rejection.RecordId);
        Assert.Contains("percentiles-out-of-order", rejection.Reasons);
        var request = Assert.Single(gateway.Requests);
        Assert.Contains("GOOD", request.InputJson, StringComparison.Ordinal);
        Assert.DoesNotContain("BAD", request.InputJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_agent_can_reject_a_record_and_its_reasons_are_kept()
    {
        var gateway = new ScriptedAnalyzeGateway(
            Decisions(("A", "Accept", []), ("B", "Reject", ["currency-geography-mismatch"])));

        var outcome = await Curator(gateway).CurateAsync([Deal("A"), Deal("B")]);

        Assert.Equal(["A"], outcome.Accepted.Select(d => d.RecordId));
        Assert.Equal(["currency-geography-mismatch"], Assert.Single(outcome.Rejected).Reasons);
    }

    [Fact]
    public async Task One_call_per_step_per_batch_not_per_record()
    {
        var gateway = new ScriptedAnalyzeGateway(
            Decisions(("A", "Accept", []), ("B", "Accept", []), ("C", "Accept", [])),
            Units(("A", "per user per month"), ("B", null), ("C", "flat annual fee")));

        var outcome = await Curator(gateway).CurateAsync([Deal("A", unit: null), Deal("B", unit: null), Deal("C", unit: null)]);

        Assert.Equal(2, gateway.Requests.Count);
        Assert.Equal(["market-data-quality", "market-provenance-curator"], gateway.Requests.Select(r => r.AgentName));
        Assert.Equal(["per user per month", null, "flat annual fee"], outcome.Accepted.Select(d => d.UnitMetric));
    }

    [Fact]
    public async Task A_unit_is_only_filled_where_the_feed_has_none_and_only_from_the_closed_list()
    {
        var gateway = new ScriptedAnalyzeGateway(
            Decisions(("A", "Accept", []), ("B", "Accept", [])),
            Units(("B", "per moon")),   // not on the list: verifier rejects, retried once...
            Units(("B", "per moon")));  // ...and again, so the step falls back to "no unit"

        var outcome = await Curator(gateway).CurateAsync([Deal("A", unit: "per device per month"), Deal("B", unit: null)]);

        Assert.Equal("per device per month", outcome.Accepted.Single(d => d.RecordId == "A").UnitMetric);
        Assert.Null(outcome.Accepted.Single(d => d.RecordId == "B").UnitMetric);
        Assert.DoesNotContain("\"A\"", gateway.Requests[1].InputJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_answer_that_skips_a_record_is_retried_with_the_violation_named()
    {
        var gateway = new ScriptedAnalyzeGateway(
            Decisions(("A", "Accept", [])),
            Decisions(("A", "Accept", []), ("B", "Reject", ["implausible-spread"])));

        var outcome = await Curator(gateway, provenance: false).CurateAsync([Deal("A"), Deal("B")]);

        Assert.Equal(2, gateway.Requests.Count);
        Assert.Contains("'B' has no decision", gateway.Requests[1].SystemPrompt, StringComparison.Ordinal);
        Assert.Equal(["B"], outcome.Rejected.Select(r => r.RecordId));
    }

    [Fact]
    public async Task When_the_model_is_unreachable_the_mechanical_rules_still_hold_and_the_rest_is_accepted()
    {
        var gateway = new ScriptedAnalyzeGateway { Fail = true };

        var outcome = await Curator(gateway).CurateAsync([Deal("A", unit: null), Deal("BAD", p25: 99m)]);

        Assert.Equal(["A"], outcome.Accepted.Select(d => d.RecordId));
        Assert.Null(outcome.Accepted[0].UnitMetric);
        Assert.Equal(["BAD"], outcome.Rejected.Select(r => r.RecordId));
    }

    [Fact]
    public async Task The_whole_reference_feed_satisfies_the_mechanical_rules()
    {
        var feed = await new MockMarketIntelligenceProvider().GetDealsAsync();

        var violations = feed.Value.Deals
            .Select(d => (d.RecordId, Reasons: MarketDealRules.Violations(d, Now)))
            .Where(x => x.Reasons.Count > 0)
            .ToList();

        Assert.Empty(violations);
    }

    // ---------------------------------------------------------------- the double

    private sealed class ScriptedAnalyzeGateway(params string[] payloads) : IAiGateway
    {
        private int _next;

        public List<AiAnalysisRequest> Requests { get; } = [];

        public bool Fail { get; set; }

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Fail || _next >= payloads.Length)
            {
                return Task.FromResult(Result<AiAnalysisResult>.Failure("scripted failure"));
            }

            return Task.FromResult(Result<AiAnalysisResult>.Success(new AiAnalysisResult(
                payloads[_next++], new AiCallMetadata("fake", "1", request.PromptVersion, Now, "hash"))));
        }

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
