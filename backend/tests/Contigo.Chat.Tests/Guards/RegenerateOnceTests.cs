using Contigo.AiGateway.Contracts;
using Contigo.Chat.Application.Guards;
using Contigo.Chat.Application.Pack;

namespace Contigo.Chat.Tests.Guards;

/// <summary>
/// Proves task E13/F06/US01/T01's regenerate-once policy (ask-engine coding objective point 5;
/// `inputs/requirements.md` R-ASK-06: "one retry naming the violation, then downgrade to abstain
/// with the pack's own facts"). <see cref="NumericGuardTests"/> and <see cref="GroundingGuardTests"/>
/// prove the guards that produce the violation text this type consumes; this file proves
/// <see cref="RegenerateOnce"/>'s own two building blocks in isolation.
/// </summary>
public sealed class RegenerateOnceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly AiCallMetadata Metadata = new("fixture-answer-model", "v1", "answer-v2.1", Now, "deadbeef");

    [Fact]
    public void BuildRetryInstruction_names_the_violation_verbatim()
    {
        var instruction = RegenerateOnce.BuildRetryInstruction("amount 'CHF 140' does not equal any pack value.");

        Assert.Contains("amount 'CHF 140' does not equal any pack value.", instruction);
        Assert.Contains("canDetermine to false", instruction);
    }

    [Fact]
    public void BuildRetryInstruction_rejects_a_null_violation()
    {
        Assert.Throws<ArgumentNullException>(() => RegenerateOnce.BuildRetryInstruction(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildRetryInstruction_rejects_a_blank_violation(string violation)
    {
        Assert.Throws<ArgumentException>(() => RegenerateOnce.BuildRetryInstruction(violation));
    }

    [Fact]
    public void DowngradeToAbstain_produces_an_honest_cannot_determine_result_preserving_metadata()
    {
        var result = RegenerateOnce.DowngradeToAbstain(Metadata, pack: [], "a violation was found.");

        Assert.False(result.CanDetermine);
        Assert.Null(result.Answer);
        Assert.Null(result.AnswerMarkdown);
        Assert.Empty(result.Citations);
        Assert.Empty(result.CitationKeys!);
        Assert.Empty(result.ActionKeys!);
        Assert.Equal(Metadata, result.Metadata);
    }

    [Fact]
    public void DowngradeToAbstain_states_nothing_supports_an_answer_when_the_pack_is_empty()
    {
        var result = RegenerateOnce.DowngradeToAbstain(Metadata, pack: [], "a violation was found.");

        Assert.Contains("Nothing in the validated contracts supports a reliable answer", result.AbstainReason);
    }

    [Fact]
    public void DowngradeToAbstain_names_the_packs_own_facts_when_the_pack_is_non_empty()
    {
        var pack = new[]
        {
            new PackItem(
                "market:deal-1",
                PackCorpus.Market,
                "Category · Market feed",
                "Representative market data",
                Page: null,
                Section: null,
                Snippet: "Market P50 unit price is CHF 132.",
                Href: null,
                PreviewUrl: null,
                RecordId: "record-1",
                Provenance: "representative market data · mock feed",
                Values: [new PackValue("unitPriceP50", "132", PackValueKind.Amount, "CHF")]),
        };

        var result = RegenerateOnce.DowngradeToAbstain(Metadata, pack, "amount 'CHF 140' does not equal any pack value.");

        Assert.Contains("amount 'CHF 140' does not equal any pack value.", result.AbstainReason);
        Assert.Contains("Category · Market feed", result.AbstainReason);
        Assert.Contains("unitPriceP50=132", result.AbstainReason);
    }

    [Fact]
    public void DowngradeToAbstain_names_at_most_five_pack_items()
    {
        var pack = Enumerable.Range(1, 8)
            .Select(i => new PackItem(
                $"market:deal-{i}",
                PackCorpus.Market,
                $"Item {i}",
                null,
                Page: null,
                Section: null,
                Snippet: "snippet",
                Href: null,
                PreviewUrl: null,
                RecordId: null,
                Provenance: "representative market data",
                Values: []))
            .ToList();

        var result = RegenerateOnce.DowngradeToAbstain(Metadata, pack, "a violation was found.");

        Assert.Contains("Item 5", result.AbstainReason);
        Assert.DoesNotContain("Item 6", result.AbstainReason);
    }

    [Fact]
    public void DowngradeToAbstain_rejects_null_metadata()
    {
        Assert.Throws<ArgumentNullException>(() => RegenerateOnce.DowngradeToAbstain(null!, [], "violation"));
    }

    [Fact]
    public void DowngradeToAbstain_rejects_null_pack()
    {
        Assert.Throws<ArgumentNullException>(() => RegenerateOnce.DowngradeToAbstain(Metadata, null!, "violation"));
    }

    [Fact]
    public void DowngradeToAbstain_rejects_a_null_violation()
    {
        Assert.Throws<ArgumentNullException>(() => RegenerateOnce.DowngradeToAbstain(Metadata, [], null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DowngradeToAbstain_rejects_a_blank_violation(string violation)
    {
        Assert.Throws<ArgumentException>(() => RegenerateOnce.DowngradeToAbstain(Metadata, [], violation));
    }
}
