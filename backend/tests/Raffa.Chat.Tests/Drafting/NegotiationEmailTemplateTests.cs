using Raffa.Chat.Application.Drafting;
using Raffa.Chat.Application.Pack;
using Raffa.Chat.Application.Playbook;

namespace Raffa.Chat.Tests.Drafting;

public sealed class NegotiationEmailTemplateTests
{
    private static PackItem Item(string key, string corpus, string title, string snippet, params PackValue[] values) =>
        new(key, corpus, title, null, null, null, snippet, "/contracts/x", null, null, "test", values, "contract-x");

    private static IReadOnlyList<PackItem> FullPack() =>
    [
        Item("calc:when-you-must-move", PackCorpus.Calc, "When you must move", "Notice by 2027-10-13, renewal on 2028-04-10.",
            new PackValue("cancellationDeadline", "2027-10-13", PackValueKind.Date, null), new PackValue("renewalDate", "2028-04-10", PackValueKind.Date, null)),
        Item("fact:x:renewal", PackCorpus.Tenant, "ServiceNow · OrderForm", "ServiceNow ends on 2028-04-10.",
            new PackValue("annualSpend", "230000", PackValueKind.Amount, "EUR"), new PackValue("endDate", "2028-04-10", PackValueKind.Date, null)),
        Item("calc:savings-target", PackCorpus.Calc, "ServiceNow — saving target and lever coverage", "Target EUR 20000 is 8.7% of the annual spend of EUR 230000.",
            new PackValue("targetAmount", "20000", PackValueKind.Amount, "EUR"), new PackValue("targetPercent", "8.7", PackValueKind.Percentage, null)),
        Item("calc:council:play[1]", PackCorpus.Calc, "Play 1 — Market discount", "Ask for the 9% discount peers obtained, worth EUR 20700 a year. Timing: before 2027-10-13. Fallback: hold the notice. Grounded in: calc:lever[market-discount].",
            new PackValue("estimatedHigh", "20700", PackValueKind.Amount, "EUR"), new PackValue("percent", "9", PackValueKind.Percentage, null)),
        NegotiationPlaybook.ToPackItem(NegotiationPlaybook.All[0]),
    ];

    private static IReadOnlyList<PackItem> ContractFactOnly() =>
    [
        Item("fact:x:renewal", PackCorpus.Tenant, "ServiceNow · OrderForm", "ServiceNow ends on 2028-04-10.",
            new PackValue("annualSpend", "230000", PackValueKind.Amount, "n/a"), new PackValue("endDate", "2028-04-10", PackValueKind.Date, null)),
    ];

    public static TheoryData<string, IReadOnlyList<PackItem>> Packs => new()
    {
        { "full", FullPack() },
        { "fact-only", ContractFactOnly() },
        { "empty", [] },
    };

    [Theory]
    [MemberData(nameof(Packs))]
    public void Template_always_passes_the_draft_guard(string name, IReadOnlyList<PackItem> pack)
    {
        foreach (var language in new[] { "it", "en" })
        {
            var draft = NegotiationEmailTemplate.Build(language, "ServiceNow", pack, null);

            Assert.Equal(DraftSource.Template, draft.Source);
            Assert.False(string.IsNullOrWhiteSpace(draft.Subject), name);
            Assert.False(string.IsNullOrWhiteSpace(draft.Body), name);

            if (pack.Count > 0)
            {
                var verdict = DraftGuard.Validate(draft.Subject, draft.Body, draft.UsedCitationKeys, pack, 2500);
                Assert.True(verdict.Passed, $"{name}/{language}: {verdict.Violation}");
            }
            else
            {
                Assert.Empty(draft.UsedCitationKeys);
                Assert.DoesNotMatch(@"\d", draft.Body);
            }
        }
    }

    [Fact]
    public void Skips_a_spend_value_whose_currency_is_not_iso()
    {
        var draft = NegotiationEmailTemplate.Build("en", "ServiceNow", ContractFactOnly(), null);

        Assert.DoesNotContain("230000", draft.Body, StringComparison.Ordinal);
        Assert.Contains("2028-04-10", draft.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Italian_and_english_bodies_name_supplier_deadline_and_top_lever()
    {
        var it = NegotiationEmailTemplate.Build("it", "ServiceNow", FullPack(), null);
        var en = NegotiationEmailTemplate.Build("en", "ServiceNow", FullPack(), null);

        Assert.StartsWith("Rinnovo ServiceNow", it.Subject, StringComparison.Ordinal);
        Assert.Contains("Gentile team ServiceNow", it.Body, StringComparison.Ordinal);
        Assert.Contains("2027-10-13", it.Body, StringComparison.Ordinal);
        Assert.Contains("EUR 230000", it.Body, StringComparison.Ordinal);
        Assert.Contains("Ask for the 9% discount peers obtained", it.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Grounded in:", it.Body, StringComparison.Ordinal);
        Assert.Contains("calc:council:play[1]", it.UsedCitationKeys);
        Assert.Contains("fact:x:renewal", it.UsedCitationKeys);

        Assert.StartsWith("ServiceNow renewal", en.Subject, StringComparison.Ordinal);
        Assert.Contains("Dear ServiceNow team", en.Body, StringComparison.Ordinal);
        Assert.Contains("[Name and surname]", en.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftPlan_from_pack_reads_plays_position_trade_and_deadline()
    {
        var plan = DraftPlan.FromPack(FullPack());

        var ask = Assert.Single(plan.Asks);
        Assert.Equal("Market discount", ask.Lever);
        Assert.Equal("Ask for the 9% discount peers obtained, worth EUR 20700 a year.", ask.Sentence);
        Assert.StartsWith("Target EUR 20000", plan.Position, StringComparison.Ordinal);
        Assert.Equal("2027-10-13", plan.DeadlineAnchor);
        Assert.DoesNotContain("\"", plan.Trade, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(plan.Trade));
    }
}
