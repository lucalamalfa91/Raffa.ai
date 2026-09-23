using Raffa.Chat.Application.Gate;
using Raffa.Chat.Application.Gaps;
using Raffa.Chat.Domain;

namespace Raffa.Chat.Tests.Gate;

/// <summary>ADR-030 D1: the capability-gap catalog is checked after the legal lexicon and before
/// the capability/how-to lexicon, and the supplier name is still extracted for the draft.</summary>
public sealed class DomainGateCapabilityGapTests
{
    private readonly DomainGate _gate = new();

    private static readonly KnownSupplierName[] KnownSalesforce = [new("Salesforce", "salesforce")];

    [Fact]
    public void Help_me_create_an_email_is_a_capability_gap_not_the_capability_tour()
    {
        var result = _gate.Classify("Can you help me create an email based on the negotiation leverage?", KnownSalesforce);

        Assert.Equal(GateLabel.CapabilityGap, result.Label);
        Assert.Equal(CapabilityGapCatalog.EmailDraftKey, result.Gap!.Key);
    }

    [Fact]
    public void Help_me_send_an_email_is_not_the_capability_tour()
    {
        var result = _gate.Classify("can you help me send an email to the supplier?", KnownSalesforce);

        Assert.Equal(GateLabel.CapabilityGap, result.Label);
        Assert.Equal(CapabilityGapCatalog.SendSupplierKey, result.Gap!.Key);
    }

    [Fact]
    public void Capability_gap_still_extracts_the_named_supplier()
    {
        var result = _gate.Classify("I have to renegotiate with Salesforce. Can you help me create an email based on the negotiation leverage?", KnownSalesforce);

        Assert.Equal(GateLabel.CapabilityGap, result.Label);
        Assert.Equal("Salesforce", result.NamedSupplier);
    }

    [Fact]
    public void An_unknown_supplier_on_a_gap_turn_keeps_the_gap_label_with_the_typed_name()
    {
        var result = _gate.Classify("Write the renewal email for Amazon", KnownSalesforce);

        Assert.Equal(GateLabel.CapabilityGap, result.Label);
        Assert.Equal("Amazon", result.NamedSupplier);
    }

    [Fact]
    public void Legal_wins_over_a_gap_phrase()
    {
        var result = _gate.Classify("write a letter so we can sue the supplier", KnownSalesforce);

        Assert.Equal(GateLabel.Legal, result.Label);
        Assert.Null(result.Gap);
    }

    [Fact]
    public void Follow_up_phrasing_for_a_validated_supplier_re_enters_the_email_gap()
    {
        var result = _gate.Classify(CapabilityGapCopy.DraftFollowUp("it", "Salesforce"), KnownSalesforce);

        Assert.Equal(GateLabel.CapabilityGap, result.Label);
        Assert.Equal("Salesforce", result.NamedSupplier);
    }

    [Fact]
    public void A_notice_timing_question_is_still_in_domain()
    {
        var result = _gate.Classify("When must we send the notice to Salesforce?", KnownSalesforce);

        Assert.Equal(GateLabel.InDomain, result.Label);
    }

    /// <summary>ADR-031: the first turn of the owner's screenshot answered "No CFO contract has been
    /// uploaded and validated" — a role written in capitals is not a supplier.</summary>
    [Theory]
    [InlineData("puoi scrivere un report per riportare l'anamento del 2026 al CFO?")]
    [InlineData("Prepare a summary of our renewals for the Board")]
    [InlineData("What should I tell the CEO about our KPIs?")]
    public void A_role_or_a_metric_in_capitals_is_never_a_supplier(string question)
    {
        var result = _gate.Classify(question, KnownSalesforce);

        Assert.NotEqual(GateLabel.NeedsDocument, result.Label);
        Assert.Null(result.NamedSupplier);
    }
}
