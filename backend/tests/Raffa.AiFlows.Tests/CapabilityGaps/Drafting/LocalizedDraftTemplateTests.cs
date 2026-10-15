using Raffa.AiFlows.CapabilityGaps.Drafting;
using Raffa.AiFlows.CapabilityGaps.Guards;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Playbook;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.CapabilityGaps.Drafting;

/// <summary>LANG-07 / F4-D10: the fallback draft for the three communication types in the five
/// supported languages, behind <see cref="DraftingOptions.LocalizedTemplates"/> (off by default).</summary>
public sealed class LocalizedDraftTemplateTests
{
    private static readonly string[] Languages = ["it", "en", "fr", "es", "de"];

    private static PackItem Item(string key, string corpus, string title, string snippet, params PackValue[] values) =>
        new(key, corpus, title, null, null, null, snippet, "/contracts/x", null, null, "test", values, "contract-x");

    private static IReadOnlyList<PackItem> FullPack() =>
    [
        Item("calc:when-you-must-move", PackCorpus.Calc, "When you must move", "Notice by 2027-10-13, renewal on 2028-04-10.",
            new PackValue("cancellationDeadline", "2027-10-13", PackValueKind.Date), new PackValue("renewalDate", "2028-04-10", PackValueKind.Date)),
        Item("fact:x:renewal", PackCorpus.Tenant, "ServiceNow · OrderForm", "ServiceNow ends on 2028-04-10.",
            new PackValue("annualSpend", "230000", PackValueKind.Amount, "EUR"), new PackValue("endDate", "2028-04-10", PackValueKind.Date)),
        Item("calc:council:play[1]", PackCorpus.Calc, "Play 1 — Market discount", "Ask for the 9% discount peers obtained, worth EUR 20700 a year. Grounded in: calc:lever[market-discount].",
            new PackValue("estimatedHigh", "20700", PackValueKind.Amount, "EUR"), new PackValue("percent", "9", PackValueKind.Percentage)),
        NegotiationPlaybook.ToPackItem(NegotiationPlaybook.All[0]),
    ];

    private static IReadOnlyList<PackItem> FactOnly() =>
    [
        Item("fact:x:renewal", PackCorpus.Tenant, "ServiceNow · OrderForm", "ServiceNow ends on 2028-04-10.",
            new PackValue("annualSpend", "230000", PackValueKind.Amount, "n/a"), new PackValue("endDate", "2028-04-10", PackValueKind.Date)),
    ];

    public static TheoryData<string, DraftType> LanguageAndType()
    {
        var data = new TheoryData<string, DraftType>();
        foreach (var language in Languages)
        {
            foreach (var type in Enum.GetValues<DraftType>())
            {
                data.Add(language, type);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LanguageAndType))]
    public void Every_language_and_type_passes_the_draft_guard_with_a_full_pack_and_with_a_thin_one(string language, DraftType type)
    {
        foreach (var pack in new[] { FullPack(), FactOnly() })
        {
            var draft = LocalizedDraftTemplate.Build(language, "ServiceNow", pack, null, type);

            Assert.Equal(DraftSource.Template, draft.Source);
            Assert.Equal(type, draft.Type);
            Assert.Contains("ServiceNow", draft.Subject, StringComparison.Ordinal);
            var verdict = DraftGuard.Validate(draft.Subject, draft.Body, draft.UsedCitationKeys, pack, 2500);
            Assert.True(verdict.Passed, $"{language}/{type}: {verdict.Violation}");
        }
    }

    [Theory]
    [MemberData(nameof(LanguageAndType))]
    public void An_empty_pack_gives_a_digit_free_draft(string language, DraftType type)
    {
        var draft = LocalizedDraftTemplate.Build(language, "ServiceNow", [], null, type);

        Assert.Empty(draft.UsedCitationKeys);
        Assert.DoesNotMatch(@"\d", draft.Body);
    }

    [Theory]
    [InlineData("it")]
    [InlineData("en")]
    public void The_italian_and_english_renegotiation_text_is_the_existing_templates(string language)
    {
        foreach (var pack in new[] { FullPack(), FactOnly(), (IReadOnlyList<PackItem>)[] })
        {
            foreach (var plan in new DraftPlan?[] { null, DraftPlan.FromPack(pack) })
            {
                var existing = NegotiationEmailTemplate.Build(language, "ServiceNow", pack, plan);
                var typed = LocalizedDraftTemplate.Build(language, "ServiceNow", pack, plan, DraftType.Renegotiation);

                Assert.Equal(existing.Subject, typed.Subject);
                Assert.Equal(existing.Body, typed.Body);
                Assert.Equal(existing.UsedCitationKeys, typed.UsedCitationKeys);
            }
        }
    }

    [Fact]
    public void The_greeting_the_subject_and_the_dates_follow_the_language()
    {
        var de = LocalizedDraftTemplate.Build("de", "ServiceNow", FullPack(), null, DraftType.CancellationNonRenewal);
        var fr = LocalizedDraftTemplate.Build("fr-FR", "ServiceNow", FullPack(), null, DraftType.CounterProposal);
        var es = LocalizedDraftTemplate.Build("es", "ServiceNow", FullPack(), null, DraftType.Renegotiation);

        Assert.Equal("Nichtverlängerung des Vertrags mit ServiceNow", de.Subject);
        Assert.StartsWith("Sehr geehrtes ServiceNow-Team,", de.Body, StringComparison.Ordinal);
        Assert.Contains("Vertragsende: 2028-04-10", de.Body, StringComparison.Ordinal);
        Assert.Contains("Kündigungsfrist bis: 2027-10-13", de.Body, StringComparison.Ordinal);

        Assert.Equal("Réponse à votre proposition – ServiceNow", fr.Subject);
        Assert.Contains("contre-proposition", fr.Body, StringComparison.Ordinal);
        Assert.Contains("EUR 230000", fr.Body, StringComparison.Ordinal);

        Assert.Equal("Renovación ServiceNow: solicitud de revisión de las condiciones", es.Subject);
        Assert.Contains("[Nombre y apellidos]", es.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cancellation_draft_carries_no_negotiation_asks_and_a_counter_proposal_carries_the_plan()
    {
        var cancel = LocalizedDraftTemplate.Build("en", "ServiceNow", FullPack(), null, DraftType.CancellationNonRenewal);
        var counter = LocalizedDraftTemplate.Build("en", "ServiceNow", FullPack(), null, DraftType.CounterProposal);

        Assert.DoesNotContain("- ", cancel.Body, StringComparison.Ordinal);
        Assert.Contains("do not intend to renew", cancel.Body, StringComparison.Ordinal);
        Assert.Contains("counter-proposal", counter.Body, StringComparison.Ordinal);
        Assert.Contains("- ", counter.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unsupported_or_blank_language_is_english()
    {
        foreach (var language in new[] { "pt", "", "  " })
        {
            var draft = LocalizedDraftTemplate.Build(language, "ServiceNow", FullPack(), null, DraftType.Renegotiation);

            Assert.StartsWith("Dear ServiceNow team,", draft.Body, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------- the workflow switch

    private static DraftRequest Request(string question, string language) =>
        new(question, language, "ServiceNow", FullPack(), null);

    private static NegotiationDraftingWorkflow Workflow(DraftingOptions options) =>
        new(new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()), options);

    [Fact]
    public async Task Off_by_default_the_template_fallback_is_the_renegotiation_mail_whatever_the_type_and_language()
    {
        var draft = await Workflow(new DraftingOptions { Enabled = false })
            .DraftAsync(Request("Kündige den Vertrag bitte, nicht verlängern", "de"));

        Assert.Equal(DraftSource.Template, draft.Source);
        Assert.Equal(DraftType.CancellationNonRenewal, draft.Type);
        Assert.StartsWith("Dear ServiceNow team,", draft.Body, StringComparison.Ordinal);
        Assert.Contains("renewal", draft.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task On_the_template_fallback_follows_the_type_and_the_language()
    {
        var draft = await Workflow(new DraftingOptions { Enabled = false, LocalizedTemplates = true })
            .DraftAsync(Request("Kündige den Vertrag bitte, nicht verlängern", "de"));

        Assert.Equal(DraftSource.Template, draft.Source);
        Assert.Equal(DraftType.CancellationNonRenewal, draft.Type);
        Assert.Equal("Nichtverlängerung des Vertrags mit ServiceNow", draft.Subject);
        Assert.True(DraftGuard.Validate(draft.Subject, draft.Body, draft.UsedCitationKeys, FullPack(), 2500).Passed);
    }
}
