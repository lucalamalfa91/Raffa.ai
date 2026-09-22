using Raffa.Chat.Application.Pack;
using Raffa.Chat.Application.Playbook;

namespace Raffa.Chat.Tests.Playbook;

public sealed class NegotiationPlaybookTests
{
    [Fact]
    public void Every_entry_is_digit_free_so_it_can_never_lend_a_number_to_an_answer()
    {
        foreach (var entry in NegotiationPlaybook.All)
        {
            var text = entry.Title + entry.Tactic + entry.WhenToUse + entry.WhatToAsk;
            Assert.DoesNotContain(text, char.IsDigit);
        }
    }

    [Fact]
    public void Keys_are_unique()
    {
        Assert.Equal(NegotiationPlaybook.All.Count, NegotiationPlaybook.All.Select(e => e.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Selection_prefers_the_levers_the_contract_grounds_then_the_category_then_generic()
    {
        var picked = NegotiationPlaybook.Select(PlaybookCategory.Saas, ["MarketDiscount", "UpliftCap", "unknown-lever"], 4);

        Assert.Equal(4, picked.Count);
        Assert.Equal(PlaybookLever.MarketDiscount, picked[0].Lever);
        Assert.Equal(PlaybookLever.UpliftCap, picked[1].Lever);
        Assert.Equal(PlaybookCategory.Saas, picked[1].Category);
        Assert.Equal(picked.Count, picked.Distinct().Count());
    }

    [Theory]
    [InlineData("Enterprise Software", PlaybookCategory.Saas)]
    [InlineData("Cloud Infrastructure", PlaybookCategory.Cloud)]
    [InlineData("Professional Services", PlaybookCategory.Consulting)]
    [InlineData("Insurance", PlaybookCategory.Insurance)]
    [InlineData(null, PlaybookCategory.Generic)]
    public void Market_categories_map_onto_playbook_categories(string? market, PlaybookCategory expected)
    {
        Assert.Equal(expected, NegotiationPlaybook.CategoryFrom(market));
    }

    [Fact]
    public void Pack_items_are_raffa_corpus_with_a_stable_key_and_no_values()
    {
        var item = NegotiationPlaybook.ToPackItem(NegotiationPlaybook.All[0]);

        Assert.Equal(PackCorpus.Raffa, item.Corpus);
        Assert.StartsWith("raffa:playbook:", item.CitationKey, StringComparison.Ordinal);
        Assert.Empty(item.Values);
        Assert.Equal(NegotiationPlaybook.Provenance, item.Provenance);
    }
}
