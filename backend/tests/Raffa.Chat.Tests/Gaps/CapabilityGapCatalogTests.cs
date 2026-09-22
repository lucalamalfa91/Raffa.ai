using Raffa.Chat.Application.Gaps;

namespace Raffa.Chat.Tests.Gaps;

/// <summary>ADR-030 D1: the five operations Raffa cannot perform are recognised in both
/// languages, in catalog order, and nothing else is.</summary>
public sealed class CapabilityGapCatalogTests
{
    [Theory]
    [InlineData("I have to renegotiate with Amazon. Can you help me create an email based on the negotiation leverage?", CapabilityGapCatalog.EmailDraftKey)]
    [InlineData("Puoi scrivermi la mail per il rinnovo Amazon?", CapabilityGapCatalog.EmailDraftKey)]
    [InlineData("Prepare the renegotiation email for Salesforce", CapabilityGapCatalog.EmailDraftKey)]
    [InlineData("Prepara una bozza di lettera al fornitore per il rinnovo", CapabilityGapCatalog.EmailDraftKey)]
    [InlineData("Write the renewal email for DocuSign", CapabilityGapCatalog.EmailDraftKey)]
    [InlineData("Scrivi la mail per il rinnovo DocuSign", CapabilityGapCatalog.EmailDraftKey)]
    [InlineData("invia una mail al fornitore con la nostra proposta", CapabilityGapCatalog.SendSupplierKey)]
    [InlineData("Can you send the supplier our counter-proposal?", CapabilityGapCatalog.SendSupplierKey)]
    [InlineData("Manda la disdetta via PEC a Salesforce", CapabilityGapCatalog.SendSupplierKey)]
    [InlineData("mettimi un promemoria per la disdetta DocuSign", CapabilityGapCatalog.ReminderKey)]
    [InlineData("Remind me two weeks before the Salesforce notice deadline", CapabilityGapCatalog.ReminderKey)]
    [InlineData("Add the renewal to my calendar", CapabilityGapCatalog.ReminderKey)]
    [InlineData("Export my contracts to Excel", CapabilityGapCatalog.ExportFileKey)]
    [InlineData("esporta il portafoglio in un foglio di calcolo", CapabilityGapCatalog.ExportFileKey)]
    [InlineData("raise a PO for the Microsoft renewal", CapabilityGapCatalog.PurchaseOrderKey)]
    [InlineData("crea un ordine d'acquisto dal contratto AWS", CapabilityGapCatalog.PurchaseOrderKey)]
    public void Matches_the_five_gaps_in_it_and_en(string question, string expectedKey)
    {
        var gap = CapabilityGapCatalog.Match(question);

        Assert.NotNull(gap);
        Assert.Equal(expectedKey, gap!.Key);
    }

    [Theory]
    [InlineData("What is the notice period for Salesforce?")]
    [InlineData("Quando devo mandare la disdetta a DocuSign?")]
    [InlineData("When must we send the notice to Salesforce?")]
    [InlineData("quali leve posso usare per risparmiare 20 k sul rinnovo")]
    [InlineData("Is the Salesforce contract in line with the market?")]
    [InlineData("How should I approach the Salesforce renewal?")]
    [InlineData("Come dovrei affrontare il rinnovo Salesforce?")]
    [InlineData("ciao")]
    [InlineData("How do I upload a PDF?")]
    [InlineData("posso risparmiare un po' sul rinnovo?")]
    [InlineData("what is the SAP contract worth?")]
    public void Does_not_match_ordinary_questions(string question)
    {
        Assert.Null(CapabilityGapCatalog.Match(question));
    }

    [Fact]
    public void Send_wins_over_write_when_both_verbs_appear()
    {
        var gap = CapabilityGapCatalog.Match("write and send an email to the supplier about the renewal");

        Assert.Equal(CapabilityGapCatalog.SendSupplierKey, gap!.Key);
        Assert.Equal(GapAlternative.DraftEmail, gap.Alternative);
    }

    [Fact]
    public void Every_entry_has_both_languages_and_a_unique_key()
    {
        Assert.Equal(5, CapabilityGapCatalog.All.Count);
        Assert.Equal(CapabilityGapCatalog.All.Count, CapabilityGapCatalog.All.Select(g => g.Key).Distinct().Count());
        Assert.All(CapabilityGapCatalog.All, gap =>
        {
            Assert.False(string.IsNullOrWhiteSpace(gap.TitleIt));
            Assert.False(string.IsNullOrWhiteSpace(gap.TitleEn));
            Assert.False(string.IsNullOrWhiteSpace(gap.OperationIt));
            Assert.False(string.IsNullOrWhiteSpace(gap.AlternativeEn));
            Assert.False(string.IsNullOrWhiteSpace(gap.DescriptionIt));
            Assert.Same(gap, CapabilityGapCatalog.Find(gap.Key));
        });
    }

    [Fact]
    public void Preface_and_offer_follow_the_language()
    {
        var gap = CapabilityGapCatalog.Find(CapabilityGapCatalog.EmailDraftKey)!;

        Assert.Equal(
            "Al momento non posso creare o inviare email da Raffa.ai, però posso aiutarti a scrivere la mail per il rinnovo.",
            CapabilityGapCopy.Preface(gap, "it"));
        Assert.StartsWith("I can't create or send emails from Raffa.ai yet, but ", CapabilityGapCopy.Preface(gap, "en"), StringComparison.Ordinal);

        var offer = CapabilityGapCopy.FeedbackOfferFor(gap, "it");
        Assert.Equal(3, offer.Questions.Count);
        Assert.Equal(FeedbackQuestions.WhatKey, offer.Questions[0].Key);
        Assert.Equal(gap.DescriptionIt, offer.Questions[0].Prefill);
        Assert.Equal(FeedbackQuestions.FrequencyKeys.Count, offer.Questions[1].Choices!.Count);
        Assert.All(offer.Questions[2].Choices!, c => Assert.Contains(c.Key, FeedbackQuestions.ImportanceKeys));
        Assert.Contains("GitHub", offer.PublicNotice, StringComparison.Ordinal);
    }

    [Fact]
    public void The_which_contract_follow_up_re_enters_the_email_gap()
    {
        var followUp = CapabilityGapCopy.DraftFollowUp("it", "Salesforce");

        Assert.Equal(CapabilityGapCatalog.EmailDraftKey, CapabilityGapCatalog.Match(followUp)!.Key);
        Assert.Equal(CapabilityGapCatalog.EmailDraftKey, CapabilityGapCatalog.Match(CapabilityGapCopy.DraftFollowUp("en", "Salesforce"))!.Key);
    }
}
