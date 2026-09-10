using Raffa.Chat.Application.Capabilities;

namespace Raffa.Chat.Tests.Capabilities;

/// <summary>
/// Proves task E13/F08/US01/T01's catalog completeness (story us-01-capability-catalog AC-1/AC-4):
/// every V2 route named in `raffa-v2/ia-v2.md`'s route map (plus the two Documents sub-states
/// R-SYS-01/R-SYS-03 also name) has exactly one catalog entry, with the field shape AC-1 requires,
/// and <see cref="FeatureCitation.For"/> produces the R-SYS-03 feature-card shape from it.
/// </summary>
public sealed class CapabilityCatalogTests
{
    private static readonly string[] ExpectedKeys =
    [
        "ask",
        "documents",
        "documents-attention",
        "documents-review",
        "portfolio",
        "contract-360",
        "renewals",
        "savings",
        "quote-check",
        "workspace-members",
    ];

    [Fact]
    public void Catalog_version_is_capabilities_v2_0()
    {
        Assert.Equal("capabilities-v2.0", CapabilityCatalog.Version);
    }

    [Fact]
    public void Catalog_has_exactly_the_ten_v2_capability_keys_with_no_duplicates()
    {
        var keys = CapabilityCatalog.All.Select(c => c.Key).ToList();

        Assert.Equal(ExpectedKeys.Length, keys.Count);
        Assert.Equal(ExpectedKeys.ToHashSet(), keys.ToHashSet());
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Theory]
    [InlineData("ask", "/ask")]
    [InlineData("documents", "/documents")]
    [InlineData("documents-attention", "/documents?filter=attention")]
    [InlineData("documents-review", "/documents?review={documentId}")]
    [InlineData("portfolio", "/contracts")]
    [InlineData("contract-360", "/contracts/{contractId}")]
    [InlineData("renewals", "/renewals")]
    [InlineData("savings", "/savings")]
    [InlineData("quote-check", "/quotes")]
    [InlineData("workspace-members", "/workspace/members")]
    public void Route_pattern_matches_the_v2_route_map(string key, string expectedRoutePattern)
    {
        var capability = CapabilityCatalog.Find(key);

        Assert.NotNull(capability);
        Assert.Equal(expectedRoutePattern, capability!.RoutePattern);
    }

    [Fact]
    public void Find_returns_null_for_an_unknown_key()
    {
        Assert.Null(CapabilityCatalog.Find("not-a-real-capability"));
    }

    [Fact]
    public void Every_entry_has_non_empty_display_content()
    {
        foreach (var capability in CapabilityCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(capability.Title), $"{capability.Key} has no title");
            Assert.False(
                string.IsNullOrWhiteSpace(capability.Description), $"{capability.Key} has no description");
            Assert.NotEmpty(capability.ExampleQuestions);
            Assert.All(capability.ExampleQuestions, q => Assert.False(string.IsNullOrWhiteSpace(q)));
            Assert.NotEmpty(capability.HowTo);
            Assert.All(capability.HowTo, step => Assert.False(string.IsNullOrWhiteSpace(step)));
        }
    }

    [Fact]
    public void Workspace_members_is_the_only_admin_gated_entry()
    {
        var adminGated = CapabilityCatalog.All.Where(c => c.RoleGate == CapabilityRoleGate.Admin).ToList();

        var onlyEntry = Assert.Single(adminGated);
        Assert.Equal("workspace-members", onlyEntry.Key);
        Assert.Equal(CapabilityAvailability.Admin, onlyEntry.Availability);
    }

    [Fact]
    public void Portfolio_renewals_savings_and_quote_check_need_a_validated_contract()
    {
        // Savings is not a rail item (ADR-018 amendment: reached only from actions, Renewals and
        // Contract 360 — no rail badge to grey), but R-SYS-04's underlying rule is "do not route
        // to an empty screen", and /savings has nothing to show with zero validated contracts —
        // the same reason Portfolio/Renewals/Quote check are gated, so it shares their gate too.
        var gated = CapabilityCatalog.All
            .Where(c => c.Availability == CapabilityAvailability.NeedsValidatedContract)
            .Select(c => c.Key)
            .ToHashSet();

        Assert.Equal(new HashSet<string> { "portfolio", "renewals", "savings", "quote-check" }, gated);
    }

    [Theory]
    [InlineData(CapabilityRoleGate.Any, "any")]
    [InlineData(CapabilityRoleGate.Admin, "admin")]
    public void Role_gate_wire_value_matches_R_SYS_01(CapabilityRoleGate gate, string expected)
    {
        Assert.Equal(expected, gate.ToApiValue());
    }

    [Theory]
    [InlineData(CapabilityAvailability.Always, "always")]
    [InlineData(CapabilityAvailability.NeedsValidatedContract, "needsValidatedContract")]
    [InlineData(CapabilityAvailability.Admin, "admin")]
    public void Availability_wire_value_matches_R_SYS_01(CapabilityAvailability availability, string expected)
    {
        Assert.Equal(expected, availability.ToApiValue());
    }

    // --- Suggestions (chipsFor / SuggestionsFor) ---

    [Fact]
    public void SuggestionsFor_documents_matches_the_prototype_chips()
    {
        var chips = CapabilityCatalog.SuggestionsFor("documents");

        Assert.Equal(
            new[] { "Which documents are not askable yet?", "Which fields still lack confidence?" },
            chips);
    }

    [Theory]
    [InlineData("documents-attention")]
    [InlineData("documents-review")]
    public void SuggestionsFor_documents_substates_reuse_the_documents_chips(string screenKey)
    {
        Assert.Equal(CapabilityCatalog.SuggestionsFor("documents"), CapabilityCatalog.SuggestionsFor(screenKey));
    }

    [Fact]
    public void SuggestionsFor_an_unrecognized_screen_falls_back_to_ask_chips()
    {
        Assert.Equal(CapabilityCatalog.SuggestionsFor("ask"), CapabilityCatalog.SuggestionsFor("not-a-screen"));
    }

    [Fact]
    public void SuggestionsFor_contract_360_names_the_supplied_supplier()
    {
        var chips = CapabilityCatalog.SuggestionsFor("contract-360", "Acme Corp");

        Assert.All(chips, chip => Assert.Contains("Acme Corp", chip));
    }

    [Fact]
    public void SuggestionsFor_contract_360_without_a_supplier_name_is_still_two_chips()
    {
        var chips = CapabilityCatalog.SuggestionsFor("contract-360");

        Assert.Equal(2, chips.Count);
    }

    // --- Feature citations (R-SYS-03) ---

    [Fact]
    public void FeatureCitation_for_uses_the_raffa_corpus_and_route_as_subtitle_and_href()
    {
        var capability = CapabilityCatalog.Find("savings")!;

        var citation = FeatureCitation.For(capability);

        Assert.Equal("raffa", citation.Corpus);
        Assert.Equal(capability.Title, citation.Title);
        Assert.Equal(capability.RoutePattern, citation.Subtitle);
        Assert.Equal(capability.Description, citation.Snippet);
        Assert.Equal(capability.RoutePattern, citation.Href);
    }

    [Fact]
    public void FeatureCitation_matches_R_SYS_03_AC_1_for_reviewing_weak_facts()
    {
        var capability = CapabilityCatalog.Find("documents-attention")!;

        var citation = FeatureCitation.For(capability);

        Assert.Equal("Documents › Review", citation.Title);
        Assert.Equal("/documents?filter=attention", citation.Href);
    }

    [Fact]
    public void FeatureCitation_for_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => FeatureCitation.For(null!));
    }
}
