using Raffa.Chat.Application.Capabilities;
using Raffa.SharedKernel;

namespace Raffa.Chat.Tests.Capabilities;

/// <summary>
/// Proves <see cref="ActionKeyNormalizer"/>: the action keys an `answer` call returns are repaired
/// into bare catalog keys before any guard sees them, so a model echoing a Raffa feature item's
/// citation key (<c>raffa:renewals</c> — the live "Where can I save the most this quarter?" abstain)
/// or its route (<c>/renewals</c>) keeps its button instead of failing the whole answer, and
/// anything that is no capability at all simply has no button.
/// </summary>
public sealed class ActionKeyNormalizerTests
{
    [Fact]
    public void A_raffa_prefixed_feature_key_becomes_the_bare_catalog_key()
    {
        var keys = ActionKeyNormalizer.Normalize(["raffa:renewals", "raffa:savings"]);

        Assert.Equal([CapabilityCatalog.RenewalsKey, CapabilityCatalog.SavingsKey], keys);
    }

    [Fact]
    public void A_bare_catalog_key_is_kept_as_is()
    {
        Assert.Equal([CapabilityCatalog.PortfolioKey], ActionKeyNormalizer.Normalize(["portfolio"]));
    }

    [Fact]
    public void An_exact_catalog_route_maps_to_its_key()
    {
        Assert.Equal([CapabilityCatalog.RenewalsKey], ActionKeyNormalizer.Normalize(["/renewals"]));
    }

    [Fact]
    public void A_casing_slip_on_a_real_key_is_repaired()
    {
        Assert.Equal([CapabilityCatalog.RenewalsKey], ActionKeyNormalizer.Normalize([" Renewals "]));
    }

    [Theory]
    [InlineData("raffa:playbook:notice-timing")]
    [InlineData("calc:portfolio-target")]
    [InlineData("https://example.com/renewals")]
    [InlineData("/renewals?select=abc")]
    [InlineData("negotiate-now")]
    [InlineData("")]
    public void Anything_that_is_not_a_capability_is_dropped_rather_than_failing(string key)
    {
        Assert.Empty(ActionKeyNormalizer.Normalize([key]));
    }

    [Fact]
    public void Duplicates_fold_and_order_is_kept()
    {
        var keys = ActionKeyNormalizer.Normalize(["raffa:savings", "renewals", "savings", "/renewals"]);

        Assert.Equal([CapabilityCatalog.SavingsKey, CapabilityCatalog.RenewalsKey], keys);
    }

    [Fact]
    public void Null_or_empty_input_gives_an_empty_list()
    {
        Assert.Empty(ActionKeyNormalizer.Normalize(null));
        Assert.Empty(ActionKeyNormalizer.Normalize([]));
    }

    [Fact]
    public void Routable_drops_contract_360_on_a_turn_with_no_contract_in_scope()
    {
        var portfolioWide = new RoutingContext(3, CapabilityCallerRole.Standard);

        var keys = ActionKeyNormalizer.Routable(["contract-360", "raffa:renewals", "documents-review"], portfolioWide);

        Assert.Equal([CapabilityCatalog.RenewalsKey], keys);
    }

    [Fact]
    public void Routable_keeps_contract_360_when_the_turn_names_a_contract()
    {
        var scoped = new RoutingContext(3, CapabilityCallerRole.Standard, EntityId.New());

        Assert.Equal([CapabilityCatalog.ContractDetailKey], ActionKeyNormalizer.Routable(["contract-360"], scoped));
    }

    [Fact]
    public void Every_routable_key_resolves_without_throwing()
    {
        var portfolioWide = new RoutingContext(3, CapabilityCallerRole.Standard);
        var every = CapabilityCatalog.All.Select(c => c.Key).ToList();

        var keys = ActionKeyNormalizer.Routable(every, portfolioWide);
        var actions = new CapabilityRouting().ResolveActions(keys.Select(CapabilityIntent.HowTo).ToList(), portfolioWide);

        Assert.NotEmpty(actions);
        Assert.All(actions, action => Assert.DoesNotContain("{", action.Href, StringComparison.Ordinal));
    }
}
