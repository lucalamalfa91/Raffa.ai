using Raffa.AiFlows.Ask.Packs;
using Raffa.AiFlows.Negotiation.DataCheck;
using Raffa.AiFlows.Shared.Pack;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.Ask.Packs;

/// <summary>Pins the deterministic parts of <see cref="MarketDataCheck"/>: which contracts a
/// multi-contract turn checks, and that no contracts means no reads at all.</summary>
public sealed class MarketDataCheckTests
{
    private static PortfolioListItem Item(Guid id) =>
        new(id, Guid.NewGuid(), ContractDocumentType.Msa, 1000m, "EUR", null, null,
            null, null, false, "completed", null, null, null);

    private static PackItem Pack(string key, string? contractId) =>
        new(key, PackCorpus.Tenant, key, null, null, null, "s", null, null, null, "test", [], contractId);

    private static PortfolioPage Page(params PortfolioListItem[] items) => new(items, 1, 25, items.Length);

    [Fact]
    public void Contracts_follow_pack_order_and_skip_unknown_or_missing_ids()
    {
        var a = Item(Guid.NewGuid());
        var b = Item(Guid.NewGuid());
        var pack = new[]
        {
            Pack("1", b.ContractId.ToString()),
            Pack("2", null),
            Pack("3", "not-a-guid"),
            Pack("4", Guid.NewGuid().ToString()),
            Pack("5", a.ContractId.ToString()),
        };

        var result = MarketDataCheck.ContractsForDataCheck(pack, Page(a, b));

        Assert.Equal([b.ContractId, a.ContractId], result.Select(i => i.ContractId));
    }

    [Fact]
    public void A_contract_cited_twice_is_checked_once()
    {
        var a = Item(Guid.NewGuid());
        var pack = new[] { Pack("1", a.ContractId.ToString()), Pack("2", a.ContractId.ToString()) };

        Assert.Single(MarketDataCheck.ContractsForDataCheck(pack, Page(a)));
    }

    [Fact]
    public void At_most_four_contracts_are_checked()
    {
        var items = Enumerable.Range(0, 6).Select(_ => Item(Guid.NewGuid())).ToArray();
        var pack = items.Select((i, n) => Pack(n.ToString(), i.ContractId.ToString())).ToArray();

        var result = MarketDataCheck.ContractsForDataCheck(pack, Page(items));

        Assert.Equal(items.Take(4).Select(i => i.ContractId), result.Select(i => i.ContractId));
    }

    [Fact]
    public async Task No_contracts_yield_an_empty_check_without_touching_the_context()
    {
        // A null reader proves nothing is read when there is nothing to check.
        var check = new MarketDataCheck(null!, new FixedClock());
        var checks = new List<MarketSafetyNet.ContractCheck>();

        var result = await check.RunAsync([], checks, CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Empty(result.MissingFields);
        Assert.Empty(checks);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
    }
}
