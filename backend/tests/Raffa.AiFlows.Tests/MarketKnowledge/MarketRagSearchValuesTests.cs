using Raffa.AiFlows.MarketKnowledge.Retrieval;
using Raffa.AiFlows.Negotiation.Options;
using Raffa.AiFlows.Shared.Pack;
using Raffa.Market.Contracts;
using Raffa.Market.Retrieval;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Market;

namespace Raffa.AiFlows.Tests.MarketKnowledge;

/// <summary>F2-T10: a market note carries its sample size and as-of date as pack values when
/// <see cref="CouncilOptions.MarketNoteValues"/> is on, and carries none otherwise.</summary>
public sealed class MarketRagSearchValuesTests
{
    private static MarketNote Note(MarketProvenanceInfo? info) =>
        new("MKT-1", "Acme · Suite", "Companies paid a median of EUR 100.", "SaaS", "EU",
            DateTimeOffset.UnixEpoch, "market feed", 0.9, info);

    [Fact]
    public async Task Off_by_default_the_note_has_no_values()
    {
        var info = new MarketProvenanceInfo(MarketSourceClasses.Provider, false, 42, new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero), "EUR/seat/year", "feed");

        foreach (var search in new[] { new MarketRagSearch(new StubRetrieval(Note(info))), new MarketRagSearch(new StubRetrieval(Note(info)), new CouncilOptions()) })
        {
            var result = await search.SearchAsync("acme", 3);

            Assert.Empty(Assert.Single(result.Value).Values);
        }
    }

    [Fact]
    public async Task On_the_note_carries_its_sample_size_and_as_of_date()
    {
        var info = new MarketProvenanceInfo(MarketSourceClasses.Provider, false, 42, new DateTimeOffset(2026, 6, 20, 13, 0, 0, TimeSpan.Zero), "EUR/seat/year", "feed");
        var search = new MarketRagSearch(new StubRetrieval(Note(info)), new CouncilOptions { MarketNoteValues = true });

        var item = Assert.Single((await search.SearchAsync("acme", 3)).Value);

        Assert.Equal(
            [
                new PackValue("sampleSize", "42", PackValueKind.Number),
                new PackValue("asOf", "2026-06-20", PackValueKind.Date),
            ],
            item.Values);
    }

    [Fact]
    public async Task On_a_note_without_a_sample_carries_only_the_date_and_one_without_provenance_none()
    {
        var info = new MarketProvenanceInfo(MarketSourceClasses.Provider, false, 0, new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero), null, "feed");
        var options = new CouncilOptions { MarketNoteValues = true };

        var withDate = Assert.Single((await new MarketRagSearch(new StubRetrieval(Note(info)), options).SearchAsync("acme", 3)).Value);
        var without = Assert.Single((await new MarketRagSearch(new StubRetrieval(Note(null)), options).SearchAsync("acme", 3)).Value);

        Assert.Equal([new PackValue("asOf", "2026-06-20", PackValueKind.Date)], withDate.Values);
        Assert.Empty(without.Values);
    }

    private sealed class StubRetrieval(MarketNote note) : IMarketKnowledgeRetrieval
    {
        public Task<Result<IReadOnlyList<MarketNote>>> SearchAsync(
            string query, int topK, MarketKnowledgeSearchFilters? filters = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<MarketNote>>.Success([note]));
    }
}
