using Contigo.Chat.Application.Gate;
using Contigo.Chat.Domain;

namespace Contigo.Chat.Tests.Gate;

/// <summary>
/// Proves task E13/F06/US01/T01's admission gate (ask-engine coding objective point 1;
/// `inputs/requirements.md` R-ASK-02/R-ASK-03): the five deterministic lexicons/rules fire before
/// any planner, pack or model call, in the exact order <see cref="DomainGate.Classify"/> documents
/// — this is the task's own Definition of Done set of gate labels, run in isolation ("gate labels
/// for 'ciao', 'ricetta della carbonara', 'posso fare causa?', 'cosa sai fare?', 'quando scade
/// Databricks?' (needs_document), 'Is my Allianz contract above market?' (in_domain)").
/// </summary>
public sealed class DomainGateTests
{
    private readonly DomainGate _gate = new();

    private static readonly string[] NoKnownSuppliers = [];
    private static readonly string[] KnownAllianz = ["Allianz"];

    [Fact]
    public void Ciao_is_greeting()
    {
        var result = _gate.Classify("ciao", NoKnownSuppliers);

        Assert.Equal(GateLabel.Greeting, result.Label);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("Hi")]
    [InlineData("buongiorno")]
    public void Other_greeting_lexicon_entries_are_also_greeting(string question)
    {
        var result = _gate.Classify(question, NoKnownSuppliers);

        Assert.Equal(GateLabel.Greeting, result.Label);
    }

    [Fact]
    public void Ricetta_della_carbonara_is_off_domain()
    {
        var result = _gate.Classify("ricetta della carbonara", NoKnownSuppliers);

        Assert.Equal(GateLabel.OffDomain, result.Label);
    }

    [Fact]
    public void Posso_fare_causa_is_legal()
    {
        var result = _gate.Classify("posso fare causa?", NoKnownSuppliers);

        Assert.Equal(GateLabel.Legal, result.Label);
    }

    [Fact]
    public void Cosa_sai_fare_is_capability()
    {
        var result = _gate.Classify("cosa sai fare?", NoKnownSuppliers);

        Assert.Equal(GateLabel.Capability, result.Label);
    }

    [Fact]
    public void Quando_scade_databricks_is_needs_document_when_supplier_unknown()
    {
        var result = _gate.Classify("quando scade Databricks?", NoKnownSuppliers);

        Assert.Equal(GateLabel.NeedsDocument, result.Label);
        Assert.Equal("Databricks", result.NamedSupplier);
    }

    [Fact]
    public void Is_my_allianz_contract_above_market_is_in_domain_when_supplier_is_known()
    {
        var result = _gate.Classify("Is my Allianz contract above market?", KnownAllianz);

        Assert.Equal(GateLabel.InDomain, result.Label);
        Assert.Equal("Allianz", result.NamedSupplier);
    }

    [Fact]
    public void Supplier_resolution_is_case_insensitive()
    {
        var result = _gate.Classify("quando scade Databricks?", ["databricks"]);

        Assert.Equal(GateLabel.InDomain, result.Label);
    }

    [Fact]
    public void A_question_naming_no_supplier_and_matching_no_lexicon_defaults_to_in_domain()
    {
        var result = _gate.Classify("when does this contract expire?", NoKnownSuppliers);

        Assert.Equal(GateLabel.InDomain, result.Label);
        Assert.Null(result.NamedSupplier);
    }

    [Fact]
    public void Legal_is_checked_before_capability_when_a_question_could_match_both_lexicons()
    {
        // "posso fare causa" only matches the legal lexicon, but this proves the declared
        // precedence stays legal-before-capability even if a future edit widens either pattern:
        // a legal question must never be answered by the capability catalog instead of refused.
        var result = _gate.Classify("posso fare causa, aiuto?", NoKnownSuppliers);

        Assert.Equal(GateLabel.Legal, result.Label);
    }

    [Fact]
    public void Off_domain_never_reaches_the_supplier_check_even_when_a_supplier_name_is_present()
    {
        // R-ASK-02: off-domain never retrieves — a recipe question that happens to also mention a
        // capitalized word must still resolve to OffDomain, not NeedsDocument.
        var result = _gate.Classify("Can you share a ricetta for carbonara, Databricks style?", NoKnownSuppliers);

        Assert.Equal(GateLabel.OffDomain, result.Label);
    }

    [Fact]
    public void Classify_rejects_a_null_question()
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace throws the more specific ArgumentNullException
        // for null (vs. plain ArgumentException for empty/whitespace) — asserted separately since
        // xUnit's Assert.Throws<T> requires an exact type match, not a subclass.
        Assert.Throws<ArgumentNullException>(() => _gate.Classify(null!, NoKnownSuppliers));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Classify_rejects_a_blank_question(string question)
    {
        Assert.Throws<ArgumentException>(() => _gate.Classify(question, NoKnownSuppliers));
    }

    [Fact]
    public void Classify_rejects_null_known_supplier_names()
    {
        Assert.Throws<ArgumentNullException>(() => _gate.Classify("ciao", null!));
    }

    [Fact]
    public void The_english_pronoun_is_never_read_as_a_supplier_name()
    {
        // R-STR-01's own worked question. "I" is capitalized mid-sentence by the rules of English,
        // so the first-capitalized-run heuristic used to report it as a named, unknown supplier and
        // the turn was answered "No I contract has been uploaded and validated" -- never
        // considering Salesforce (golden set, GAP-ASK-PRONOUN-AS-SUPPLIER).
        var result = _gate.Classify("How should I approach the Salesforce renewal?", ["Salesforce"]);

        Assert.Equal(GateLabel.InDomain, result.Label);
        Assert.Equal("Salesforce", result.NamedSupplier);
    }

    [Fact]
    public void A_known_supplier_anywhere_in_the_question_wins_over_an_earlier_capitalized_word()
    {
        // The known supplier is the last capitalized run here; an earlier one must not shadow it.
        var result = _gate.Classify("In Q4 we renew Databricks, right?", ["Databricks"]);

        Assert.Equal(GateLabel.InDomain, result.Label);
        Assert.Equal("Databricks", result.NamedSupplier);
    }

    [Fact]
    public void An_unknown_named_supplier_still_asks_for_the_document()
    {
        // Nothing about the fix loosens R-ASK-03: a supplier this tenant has no contract for is
        // still answered with "upload it first", and the reported candidate is the real name, not
        // a pronoun.
        var result = _gate.Classify("How should I approach the Snowflake renewal?", ["Salesforce"]);

        Assert.Equal(GateLabel.NeedsDocument, result.Label);
        Assert.Equal("Snowflake", result.NamedSupplier);
    }

    [Fact]
    public void An_empty_known_supplier_collection_still_resolves_greeting_and_off_domain_labels()
    {
        // A brand-new tenant with zero contracts must still get the ordinary greeting/off-domain
        // treatment, not an exception — only a genuinely named, unresolved supplier becomes
        // NeedsDocument.
        Assert.Equal(GateLabel.Greeting, _gate.Classify("ciao", NoKnownSuppliers).Label);
        Assert.Equal(GateLabel.OffDomain, _gate.Classify("che meteo fa oggi?", NoKnownSuppliers).Label);
    }
}
