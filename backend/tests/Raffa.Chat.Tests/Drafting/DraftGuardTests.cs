using Raffa.Chat.Application.Drafting;
using Raffa.Chat.Application.Pack;

namespace Raffa.Chat.Tests.Drafting;

public sealed class DraftGuardTests
{
    private static readonly IReadOnlyList<PackItem> Pack =
    [
        new("fact:x:renewal", PackCorpus.Tenant, "ServiceNow · OrderForm", null, null, null,
            "ServiceNow ends on 2028-04-10 (notice by 2027-10-13).", "/contracts/x", null, null, "test",
            [new PackValue("annualSpend", "230000", PackValueKind.Amount, "EUR"), new PackValue("cancellationDeadline", "2027-10-13", PackValueKind.Date, null)],
            "contract-x"),
        new("calc:lever[market-discount]", PackCorpus.Calc, "ServiceNow — Market discount", null, null, null,
            "Peers achieved a 9% discount: up to EUR 20700 a year.", "/contracts/x", null, null, "calc",
            [new PackValue("estimatedHigh", "20700", PackValueKind.Amount, "EUR"), new PackValue("percent", "9", PackValueKind.Percentage, null)],
            "contract-x"),
    ];

    [Fact]
    public void Accepts_a_grounded_plain_text_email()
    {
        var verdict = DraftGuard.Validate(
            "ServiceNow renewal", "Dear team,\n\nOur spend is EUR 230000 and the notice deadline is 2027-10-13. We ask for the 9% discount.\n\nKind regards",
            ["fact:x:renewal", "calc:lever[market-discount]"], Pack, 2500);

        Assert.True(verdict.Passed, verdict.Violation);
    }

    [Fact]
    public void Rejects_an_inline_marker()
    {
        var verdict = DraftGuard.Validate("Subject", "We ask for the 9% discount [1].", ["fact:x:renewal"], Pack, 2500);

        Assert.False(verdict.Passed);
        Assert.Contains("inline citation marker", verdict.Violation, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_number_not_in_the_pack()
    {
        var verdict = DraftGuard.Validate("Subject", "We ask for EUR 99999 off.", ["fact:x:renewal"], Pack, 2500);

        Assert.False(verdict.Passed);
        Assert.Contains("EUR 99999", verdict.Violation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("See https://example.com for details.")]
    [InlineData("Reference 3f2504e0-4f89-11d3-9a0c-0305e82c3301.")]
    [InlineData("Grounded in: calc:lever[market-discount].")]
    public void Rejects_a_link_a_guid_and_an_internal_token(string body)
    {
        var verdict = DraftGuard.Validate("Subject", body, ["fact:x:renewal"], Pack, 2500);

        Assert.False(verdict.Passed);
    }

    [Fact]
    public void Rejects_unknown_or_empty_citation_keys()
    {
        Assert.False(DraftGuard.Validate("Subject", "Dear team, thank you.", [], Pack, 2500).Passed);
        Assert.False(DraftGuard.Validate("Subject", "Dear team, thank you.", ["calc:nope"], Pack, 2500).Passed);
        Assert.Equal(["fact:x:renewal"], DraftGuard.GroundedKeys(["calc:nope", "fact:x:renewal", "fact:x:renewal"], Pack));
    }

    [Fact]
    public void Rejects_a_blank_subject_a_blank_body_and_an_overlong_body()
    {
        Assert.False(DraftGuard.Validate("", "body", ["fact:x:renewal"], Pack, 2500).Passed);
        Assert.False(DraftGuard.Validate("Subject", " ", ["fact:x:renewal"], Pack, 2500).Passed);
        Assert.False(DraftGuard.Validate("Subject", new string('a', 30), ["fact:x:renewal"], Pack, 20).Passed);
    }
}
