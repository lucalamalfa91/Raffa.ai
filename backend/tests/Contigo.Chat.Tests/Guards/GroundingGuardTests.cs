using Contigo.AiGateway.Contracts;
using Contigo.Chat.Application.Guards;
using Contigo.Chat.Application.Pack;

namespace Contigo.Chat.Tests.Guards;

/// <summary>
/// Proves task E13/F06/US01/T01's grounding guard (ask-engine coding objective point 5;
/// `inputs/requirements.md` R-ASK-06 points 1 + 3): every citationKey / inline <c>[n]</c> marker
/// must resolve into the pack, and every actionKey must resolve to a real capability. Includes this
/// task's own Definition of Done scenario: "grounding guard rejects a foreign key" — a citationKey
/// the model invents that does not belong to the pack it was given.
/// </summary>
public sealed class GroundingGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly AiCallMetadata Metadata = new("fixture-answer-model", "v1", "answer-v2.1", Now, "deadbeef");

    private static readonly PackItem[] OneItemPack =
    [
        new(
            "tenant:contract-1",
            PackCorpus.Tenant,
            "Salesforce · MSA 2024",
            "p.12 §8.4",
            Page: 12,
            Section: "8.4",
            Snippet: "Liability is capped at CHF 1,000,000.",
            Href: "/contracts/11111111-1111-1111-1111-111111111111",
            PreviewUrl: null,
            RecordId: null,
            Provenance: "validated contract",
            Values: []),
    ];

    private static AiAnswerResult Determined(
        string markdown, IReadOnlyList<string> citationKeys, IReadOnlyList<string>? actionKeys = null) =>
        new(
            CanDetermine: true,
            Answer: markdown,
            Citations: [],
            Metadata,
            AnswerMarkdown: markdown,
            CitationKeys: citationKeys,
            ActionKeys: actionKeys ?? [],
            AbstainReason: null,
            FollowUps: []);

    [Fact]
    public void A_citation_key_foreign_to_the_pack_is_rejected()
    {
        var result = Determined("Liability is capped at CHF 1,000,000 [1].", ["foreign-key-not-in-pack"]);

        var verdict = GroundingGuard.Validate(result, OneItemPack);

        Assert.False(verdict.Passed);
        Assert.Contains("foreign-key-not-in-pack", verdict.Violation);
    }

    [Fact]
    public void A_citation_key_that_belongs_to_the_pack_is_accepted()
    {
        var result = Determined("Liability is capped at CHF 1,000,000 [1].", ["tenant:contract-1"]);

        var verdict = GroundingGuard.Validate(result, OneItemPack);

        Assert.True(verdict.Passed);
        Assert.Null(verdict.Violation);
    }

    [Fact]
    public void An_inline_marker_past_the_end_of_citation_keys_is_rejected()
    {
        // citationKeys has one entry (n=1 is valid), but the markdown's own inline marker points
        // one past it.
        var result = Determined("Liability is capped at CHF 1,000,000 [2].", ["tenant:contract-1"]);

        var verdict = GroundingGuard.Validate(result, OneItemPack);

        Assert.False(verdict.Passed);
        Assert.Contains("[2]", verdict.Violation);
    }

    [Fact]
    public void An_unresolvable_action_key_is_rejected()
    {
        var result = Determined(
            "Liability is capped at CHF 1,000,000 [1].", ["tenant:contract-1"], ["not-a-real-capability"]);

        var verdict = GroundingGuard.Validate(result, OneItemPack);

        Assert.False(verdict.Passed);
        Assert.Contains("not-a-real-capability", verdict.Violation);
    }

    [Fact]
    public void A_real_catalog_action_key_is_accepted()
    {
        var result = Determined(
            "Liability is capped at CHF 1,000,000 [1].", ["tenant:contract-1"], ["portfolio"]);

        var verdict = GroundingGuard.Validate(result, OneItemPack);

        Assert.True(verdict.Passed);
    }

    [Fact]
    public void An_already_honest_abstention_is_never_re_checked()
    {
        var abstained = new AiAnswerResult(
            CanDetermine: false,
            Answer: null,
            Citations: [],
            Metadata,
            AnswerMarkdown: null,
            CitationKeys: [],
            ActionKeys: [],
            AbstainReason: "Nothing in the validated contracts supports a reliable answer.",
            FollowUps: []);

        var verdict = GroundingGuard.Validate(abstained, OneItemPack);

        Assert.True(verdict.Passed);
    }

    [Fact]
    public void Validate_rejects_a_null_result()
    {
        Assert.Throws<ArgumentNullException>(() => GroundingGuard.Validate(null!, OneItemPack));
    }

    [Fact]
    public void Validate_rejects_a_null_pack()
    {
        var result = Determined("text [1].", ["tenant:contract-1"]);

        Assert.Throws<ArgumentNullException>(() => GroundingGuard.Validate(result, null!));
    }
}
