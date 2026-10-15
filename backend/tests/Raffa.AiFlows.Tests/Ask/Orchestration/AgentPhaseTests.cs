using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.Ask.Packs;
using Raffa.AiFlows.Negotiation.Agents;
using Raffa.AiFlows.Negotiation.Options;
using Raffa.AiFlows.Negotiation.Orchestration;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.Ask.Orchestration;

/// <summary>Pins <see cref="AgentPhase"/>'s deterministic composition with every agent step off
/// (no researcher, no council) over the offline fixture gateway: the budget and the calendar item.</summary>
public sealed class AgentPhaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);

    private static PackItem Item(string key) =>
        new(key, PackCorpus.Tenant, key, null, null, null, "snippet", null, null, null, "test", []);

    private static AgentPhase Phase(PackBudget? budget = null)
    {
        var gateway = new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions());
        var options = new CouncilOptions();
        var flow = new AskAgentFlow(new MarketResearcher(gateway, options), new NegotiationCouncil(gateway, options));
        var check = new MarketDataCheck(null!, new FixedClock());
        return new AgentPhase(flow, check, budget ?? new PackBudget(), new FixedClock());
    }

    private static AgentPhaseRequest Off(bool calendar) => new([], false, false, calendar);

    [Fact]
    public async Task With_every_step_off_the_pack_passes_through_and_no_step_runs()
    {
        var pack = new[] { Item("a"), Item("b") };

        var result = await Phase().RunAsync("what is the notice period?", null, pack, Off(calendar: true), CancellationToken.None);

        Assert.Equal(["a", "b"], result.BoundedPack.Select(i => i.CitationKey));
        Assert.Empty(result.DataChecks);
        Assert.Empty(result.Flow.StepsRun);
        Assert.Empty(result.Flow.Failures);
    }

    [Fact]
    public async Task A_period_question_appends_the_calendar_item_after_the_budget()
    {
        var result = await Phase().RunAsync("what renews this quarter?", null, [Item("a")], Off(calendar: true), CancellationToken.None);

        Assert.Equal(2, result.BoundedPack.Count);
        Assert.Equal("a", result.BoundedPack[0].CitationKey);
        Assert.Equal(CalendarPackItem.Build(DateOnly.FromDateTime(Now.UtcDateTime)).CitationKey, result.BoundedPack[1].CitationKey);
    }

    [Fact]
    public async Task No_calendar_item_unless_requested_or_the_pack_is_empty()
    {
        var phase = Phase();

        var notRequested = await phase.RunAsync("what renews this quarter?", null, [Item("a")], Off(calendar: false), CancellationToken.None);
        var emptyPack = await phase.RunAsync("what renews this quarter?", null, [], Off(calendar: true), CancellationToken.None);
        var notPeriod = await phase.RunAsync("what is the notice period?", null, [Item("a")], Off(calendar: true), CancellationToken.None);

        Assert.Single(notRequested.BoundedPack);
        Assert.Empty(emptyPack.BoundedPack);
        Assert.Single(notPeriod.BoundedPack);
    }

    [Fact]
    public async Task The_budget_trims_trailing_items_but_keeps_the_first()
    {
        var big = new string('x', 3500);
        PackItem Big(string key) => Item(key) with { Snippet = big };

        var result = await Phase(new PackBudget(maxTokens: 1500))
            .RunAsync("notice period?", null, [Big("a"), Big("b"), Big("c")], Off(calendar: false), CancellationToken.None);

        Assert.Equal("a", result.BoundedPack[0].CitationKey);
        Assert.True(result.BoundedPack.Count < 3);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = Now;
    }
}
