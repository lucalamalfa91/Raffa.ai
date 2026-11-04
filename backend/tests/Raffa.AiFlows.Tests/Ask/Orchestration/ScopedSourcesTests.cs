using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.Shared.Pack;

namespace Raffa.AiFlows.Tests.Ask.Orchestration;

/// <summary>
/// A chat bound to one contract answers from that contract's own sources. Regression for the
/// GitLab chat ("About GitLab") that said the contract was missing and summarised a GitHub
/// contract: a pack or an answer that rests only on other suppliers' material is refused.
/// </summary>
public sealed class ScopedSourcesTests
{
    private static readonly Guid GitLab = Guid.NewGuid();
    private static readonly Guid GitHub = Guid.NewGuid();

    private static PackItem Item(string key, string corpus, Guid? contractId) =>
        new(key, corpus, key, null, null, null, "text", null, null, null, "test", [], contractId?.ToString());

    [Fact]
    public void A_pack_with_only_another_contracts_sources_cannot_ground_a_bound_chat()
    {
        var pack = new[] { Item("peer", PackCorpus.Tenant, null), Item("github", PackCorpus.Tenant, GitHub) };

        Assert.False(ScopedSources.PackCanGround(pack, GitLab));
    }

    [Fact]
    public void A_pack_with_the_bound_contracts_own_source_can_ground_it_even_beside_peers()
    {
        var pack = new[] { Item("fact", PackCorpus.Tenant, GitLab), Item("peer", PackCorpus.Tenant, null) };

        Assert.True(ScopedSources.PackCanGround(pack, GitLab));
    }

    [Fact]
    public void A_pack_with_no_contract_source_at_all_is_left_to_the_answer_how_to_or_market()
    {
        var pack = new[] { Item("feature", PackCorpus.Raffa, null), Item("note", PackCorpus.Market, null) };

        Assert.True(ScopedSources.PackCanGround(pack, GitLab));
    }

    [Fact]
    public void An_answer_citing_only_a_peer_or_a_market_note_is_refused_when_the_contracts_own_source_was_available()
    {
        var pack = new[]
        {
            Item("fact", PackCorpus.Tenant, GitLab),
            Item("peer", PackCorpus.Tenant, null),
            Item("note", PackCorpus.Market, null),
        };

        Assert.False(ScopedSources.CitationsBelong(["peer"], pack, GitLab));
        Assert.False(ScopedSources.CitationsBelong(["note"], pack, GitLab));
        Assert.False(ScopedSources.CitationsBelong(["peer", "note"], pack, GitLab));
    }

    [Fact]
    public void An_answer_citing_the_contracts_own_source_passes_with_or_without_comparisons()
    {
        var pack = new[]
        {
            Item("fact", PackCorpus.Tenant, GitLab),
            Item("calc", PackCorpus.Calc, GitLab),
            Item("peer", PackCorpus.Tenant, null),
            Item("note", PackCorpus.Market, null),
        };

        Assert.True(ScopedSources.CitationsBelong(["fact"], pack, GitLab));
        Assert.True(ScopedSources.CitationsBelong(["calc", "note"], pack, GitLab));
        Assert.True(ScopedSources.CitationsBelong(["fact", "peer", "note"], pack, GitLab));
    }

    [Fact]
    public void A_how_to_answer_citing_only_raffa_features_passes()
    {
        var pack = new[] { Item("fact", PackCorpus.Tenant, GitLab), Item("feature", PackCorpus.Raffa, null) };

        Assert.True(ScopedSources.CitationsBelong(["feature"], pack, GitLab));
        Assert.True(ScopedSources.CitationsBelong([], pack, GitLab));
    }

    [Fact]
    public void The_lead_names_the_bound_contract_in_the_questions_language()
    {
        Assert.Contains("GitLab", ScopedSources.OtherSourcesLead("GitLab", italian: true), StringComparison.Ordinal);
        Assert.StartsWith("Le fonti validate", ScopedSources.OtherSourcesLead("GitLab", italian: true), StringComparison.Ordinal);
        Assert.StartsWith("The validated sources of the GitLab contract", ScopedSources.OtherSourcesLead("GitLab", italian: false), StringComparison.Ordinal);
        Assert.StartsWith("This contract's", ScopedSources.OtherSourcesLead(null, italian: false), StringComparison.Ordinal);
    }
}
