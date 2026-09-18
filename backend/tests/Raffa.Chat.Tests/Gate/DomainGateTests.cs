using Raffa.Chat.Application.Gate;
using Raffa.Chat.Domain;

namespace Raffa.Chat.Tests.Gate;

/// <summary>
/// Proves task E13/F06/US01/T01's admission gate (ask-engine coding objective point 1;
/// `inputs/requirements.md` R-ASK-02/R-ASK-03): the five deterministic lexicons/rules fire before
/// any planner, pack or model call, in the exact order <see cref="DomainGate.Classify"/> documents
/// — this is the task's own Definition of Done set of gate labels, run in isolation ("gate labels
/// for 'ciao', 'ricetta della carbonara', 'posso fare causa?', 'cosa sai fare?', 'quando scade
/// Databricks?' (needs_document), 'Is my Allianz contract above market?' (in_domain)").
///
/// <para>
/// Task E27/F05/US01/T01 (NW-80) added the <c>Normalized_contains_*</c>/<c>Partial_mention_*</c>
/// tests below, proving <see cref="DomainGate.Classify"/>'s second matching tier — everything above
/// them proves Tier 1 (the pre-existing, unchanged exact capitalized-run match) still behaves
/// exactly as before.
/// </para>
/// </summary>
public sealed class DomainGateTests
{
    private readonly DomainGate _gate = new();

    private static readonly KnownSupplierName[] NoKnownSuppliers = [];
    private static readonly KnownSupplierName[] KnownAllianz = [Known("Allianz")];

    /// <summary>Builds a <see cref="KnownSupplierName"/> the way the host would for every Tier-1
    /// test below: none of these names carry punctuation or a legal suffix, so a plain
    /// <see cref="string.ToLowerInvariant"/> is already exactly what the real
    /// <c>SupplierNameNormalizer.Normalize</c> would produce for them too — the Tier-2 tests added
    /// for task E27/F05/US01/T01 (NW-80) construct <see cref="KnownSupplierName"/> directly instead,
    /// precisely to exercise a normalized form that differs from a plain lower-case (a stripped
    /// legal suffix).</summary>
    private static KnownSupplierName Known(string displayName) => new(displayName, displayName.ToLowerInvariant());

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
        var result = _gate.Classify("quando scade Databricks?", [Known("databricks")]);

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
        var result = _gate.Classify("How should I approach the Salesforce renewal?", [Known("Salesforce")]);

        Assert.Equal(GateLabel.InDomain, result.Label);
        Assert.Equal("Salesforce", result.NamedSupplier);
    }

    [Fact]
    public void A_known_supplier_anywhere_in_the_question_wins_over_an_earlier_capitalized_word()
    {
        // The known supplier is the last capitalized run here; an earlier one must not shadow it.
        var result = _gate.Classify("In Q4 we renew Databricks, right?", [Known("Databricks")]);

        Assert.Equal(GateLabel.InDomain, result.Label);
        Assert.Equal("Databricks", result.NamedSupplier);
    }

    [Fact]
    public void An_unknown_named_supplier_still_asks_for_the_document()
    {
        // Nothing about the fix loosens R-ASK-03: a supplier this tenant has no contract for is
        // still answered with "upload it first", and the reported candidate is the real name, not
        // a pronoun.
        var result = _gate.Classify("How should I approach the Snowflake renewal?", [Known("Salesforce")]);

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

    // ----- Task E27/F05/US01/T01 (NW-80): Tier 2, normalized/contains -----

    /// <summary>AC-1's own worked example: "astercloud GmbH" (lower-case first word) must resolve
    /// to the stored "AsterCloud GmbH". <see cref="DomainGate"/>'s capitalized-run pattern only ever
    /// sees "GmbH" here ("astercloud" is lower-case, so it never joins the run) — Tier 1 alone can
    /// only fail this, never resolve it — so this is proof of Tier 2's whole-question
    /// normalized/contains match, not of a wider Tier-1 pattern.</summary>
    [Fact]
    public void A_capitalized_legal_suffix_with_a_lowercase_supplier_name_resolves_via_normalized_contains()
    {
        var knownAsterCloud = new[] { new KnownSupplierName("AsterCloud GmbH", "astercloud") };

        var result = _gate.Classify("Tell me about astercloud GmbH please", knownAsterCloud);

        Assert.Equal(GateLabel.InDomain, result.Label);
        Assert.Equal("AsterCloud GmbH", result.NamedSupplier);
    }

    /// <summary>AC-1's other worked example: a fully lower-case mention with no capital letter at
    /// all ("su salesforce") produces zero Tier-1 candidates (the capitalized-run pattern matches
    /// nothing), so only Tier 2 can resolve it.</summary>
    [Fact]
    public void A_fully_lowercase_mention_with_no_capital_letter_resolves_via_normalized_contains()
    {
        var result = _gate.Classify(
            "Quanto paghiamo su salesforce quest'anno?", [new KnownSupplierName("Salesforce", "salesforce")]);

        Assert.Equal(GateLabel.InDomain, result.Label);
        Assert.Equal("Salesforce", result.NamedSupplier);
    }

    /// <summary>A candidate missing the supplier's own legal suffix ("AsterCloud" for the stored
    /// "AsterCloud GmbH") is a real Tier-1 candidate, but it does not equal the full stored name —
    /// Tier 2's normalized form (legal suffix stripped by the host's own SupplierNameNormalizer
    /// before this test ever runs) is what resolves it to the canonical display name, not the
    /// as-typed partial one.</summary>
    [Fact]
    public void A_partial_mention_missing_the_legal_suffix_resolves_to_the_full_stored_name()
    {
        var knownAsterCloud = new[] { new KnownSupplierName("AsterCloud GmbH", "astercloud") };

        var result = _gate.Classify("How should I approach the AsterCloud renewal?", knownAsterCloud);

        Assert.Equal(GateLabel.InDomain, result.Label);
        Assert.Equal("AsterCloud GmbH", result.NamedSupplier);
    }

    /// <summary>Tier 2 matches whole words only — a known supplier normalizing to "sap" must never
    /// fire merely because "sap" is a substring of "sapling". Proves the word-boundary guarantee,
    /// not just the happy path.</summary>
    [Fact]
    public void Normalized_contains_match_never_fires_on_a_same_word_substring()
    {
        var result = _gate.Classify(
            "Can you check the sapling in our garden?", [new KnownSupplierName("SAP", "sap")]);

        Assert.Equal(GateLabel.InDomain, result.Label);
        Assert.Null(result.NamedSupplier);
    }

    /// <summary>Tier 1 still wins outright when it already resolves — Tier 2 is a fallback, never a
    /// second opinion that could contradict an exact match.</summary>
    [Fact]
    public void An_exact_capitalized_match_is_never_second_guessed_by_the_normalized_tier()
    {
        var knownBoth = new[]
        {
            new KnownSupplierName("Salesforce", "salesforce"),
            new KnownSupplierName("AsterCloud GmbH", "astercloud"),
        };

        var result = _gate.Classify("How should I approach the Salesforce renewal?", knownBoth);

        Assert.Equal(GateLabel.InDomain, result.Label);
        Assert.Equal("Salesforce", result.NamedSupplier);
    }
}
