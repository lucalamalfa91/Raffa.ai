using Raffa.Chat.Application.Memory;

namespace Raffa.Chat.Tests.Memory;

public sealed class UserMemoryProfileTests
{
    [Fact]
    public void Round_trip_preserves_only_the_structured_profile()
    {
        var profile = new UserMemoryProfile(
            new Dictionary<string, int> { ["ContractAnalysis"] = 4 },
            new Dictionary<string, int> { ["supplier_email"] = 3 },
            new Dictionary<string, int> { ["benchmark->supplier_email"] = 2 },
            "supplier_email", "concise", ["Acme"], true);

        var restored = UserMemoryProfile.Parse(profile.Serialize());

        Assert.Equal("concise", restored.PreferredOutput);
        Assert.Equal(3, restored.WorkflowCounts!["supplier_email"]);
        Assert.Equal(["Acme"], restored.RecentSuppliers);
        Assert.True(restored.Enabled);
    }

    [Fact]
    public void Disabled_tombstone_contains_no_learned_behaviour()
    {
        var tombstone = UserMemoryProfile.Empty with { Enabled = false };
        var restored = UserMemoryProfile.Parse(tombstone.Serialize());

        Assert.False(restored.Enabled);
        Assert.Empty(restored.IntentCounts!);
        Assert.Empty(restored.WorkflowCounts!);
        Assert.Empty(restored.RecentSuppliers!);
        Assert.Null(restored.PreferredOutput);
    }

    [Fact]
    public void Invalid_persisted_json_fails_closed_to_an_empty_profile()
    {
        var restored = UserMemoryProfile.Parse("{not-json");

        Assert.Empty(restored.IntentCounts!);
        Assert.Empty(restored.WorkflowCounts!);
        Assert.Empty(restored.RecentSuppliers!);
    }

    [Theory]
    [InlineData("supplier_email", "it", "Vuoi")]
    [InlineData("supplier_email", "en", "Would you")]
    [InlineData("artifact", "it", "piano operativo")]
    [InlineData("artifact", "en", "action plan")]
    [InlineData("benchmark", "it", "benchmark autorizzato")]
    [InlineData("benchmark", "en", "authorized benchmark")]
    public void Predictive_suggestions_are_localized(string workflow, string language, string expected)
    {
        var suggestion = UserMemorySnapshot.SuggestionFor(workflow, language);

        Assert.Contains(expected, suggestion, StringComparison.OrdinalIgnoreCase);
    }
}
