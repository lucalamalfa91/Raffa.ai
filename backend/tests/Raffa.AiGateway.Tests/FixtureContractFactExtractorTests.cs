using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.AiGateway.Tests.TestSupport;

namespace Raffa.AiGateway.Tests;

/// <summary>
/// The fixture gateway's deterministic `extract` role (<see cref="FixtureContractFactExtractor"/>,
/// behind <see cref="FixtureAiGateway.ExtractAsync"/>): every fact is read from the text with its
/// page and literal span, a clean contract yields only trusted facts, and an ambiguous one yields
/// low-confidence facts for exactly the fields the text leaves open — so a fixture-backed host
/// (local, CI, the deployed dev until a Foundry account exists) routes documents to review for
/// real reasons, never by default. The two texts below are the same two sample contracts the web's
/// "Sample MSA" buttons upload (<c>web/src/routes/documents/sampleDocument.ts</c>): the clean
/// Northwind MSA must complete, the Fabrikam MSA must need review.
/// </summary>
public class FixtureContractFactExtractorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private const string CleanPage1 =
        "MASTER SERVICES AGREEMENT. This Master Services Agreement is entered into between Raffa Demo AG " +
        "(\"Customer\") and Northwind Traders SA (\"Supplier\"), effective 2026-01-01. 1. Scope. This Agreement " +
        "governs every Order Form the parties execute under it for the Supplier's cloud procurement platform and " +
        "related support services. 2. Fees. The annual subscription fee is EUR 48,000, invoiced yearly in advance. " +
        "The total contract value for the initial term is EUR 144,000. All invoices are payable within thirty (30) " +
        "days of receipt.";

    private const string CleanPage2 =
        "3. Term and renewal. The initial term is thirty-six (36) months from the effective date. Thereafter this " +
        "Agreement renews automatically for successive twelve (12) month terms unless either party gives ninety (90) " +
        "days written notice before the end of the then-current term. Price increases at renewal are capped at four " +
        "percent. 4. Termination. Either party may terminate this Agreement for material breach not cured within " +
        "thirty (30) days of written notice. 5. Governing law. This Agreement is governed by the laws of Switzerland, " +
        "and the courts of Zurich have exclusive jurisdiction.";

    private const string AmbiguousPage1 =
        "MASTER SERVICES AGREEMENT. This Master Services Agreement is made between Raffa Demo AG and Fabrikam " +
        "Software GmbH, effective 1 February 2026. 1. Scope. Fabrikam provides its analytics platform and support " +
        "services to Raffa Demo AG under the Order Forms executed from time to time. 2. Fees. The annual " +
        "subscription fee is EUR 36,000, invoiced quarterly in arrears; Schedule 1, however, lists an annual fee of " +
        "EUR 39,600 after the agreed uplift. Invoices are payable within forty-five (45) days.";

    private const string AmbiguousPage2 =
        "3. Term and renewal. The initial term is twenty-four (24) months. This Agreement renews automatically for " +
        "successive twelve (12) month periods; notwithstanding the foregoing, the Customer may elect in writing that " +
        "this Agreement shall not automatically renew. 4. Notice. Either party may give sixty (60) days written notice " +
        "before the end of the current term. 5. Governing law. This Agreement is governed by the laws of Germany.";

    private static string PageMarked(params string[] pages) =>
        string.Join("\n\n", pages.Select((text, index) => $"[[PAGE {index + 1}]]\n{text}")) + "\n\n";

    private static Dictionary<string, JsonElement> Facts(string stage, string text)
    {
        var payload = FixtureContractFactExtractor.Extract(stage, text);
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("facts").EnumerateArray()
            .ToDictionary(f => f.GetProperty("field").GetString()!, f => f.Clone(), StringComparer.Ordinal);
    }

    private static string Value(JsonElement fact) => fact.GetProperty("value").GetString()!;
    private static double Confidence(JsonElement fact) => fact.GetProperty("confidence").GetDouble();
    private static int? Page(JsonElement fact) =>
        fact.GetProperty("sourcePage").ValueKind == JsonValueKind.Null ? null : fact.GetProperty("sourcePage").GetInt32();
    private static string Span(JsonElement fact) => fact.GetProperty("sourceSpan").GetString()!;

    [Fact]
    public void Clean_msa_metadata_names_the_labelled_supplier_currency_law_and_status_with_strong_confidence()
    {
        var facts = Facts("Metadata", PageMarked(CleanPage1, CleanPage2));

        Assert.Equal("Northwind Traders SA", Value(facts["supplier"]));
        Assert.Equal(FixtureContractFactExtractor.StrongConfidence, Confidence(facts["supplier"]));
        Assert.Equal(1, Page(facts["supplier"]));
        Assert.Contains("Northwind Traders SA (\"Supplier\")", Span(facts["supplier"]));

        Assert.Equal("EUR", Value(facts["currency"]));
        Assert.Equal(FixtureContractFactExtractor.StrongConfidence, Confidence(facts["currency"]));

        Assert.Equal("Switzerland", Value(facts["governingLaw"]));
        Assert.Equal(2, Page(facts["governingLaw"]));

        Assert.Equal("active", Value(facts["status"]));
        Assert.Equal(FixtureContractFactExtractor.GoodConfidence, Confidence(facts["status"]));
    }

    [Fact]
    public void Clean_msa_commercial_terms_read_the_annual_fee_total_value_and_payment_terms()
    {
        var facts = Facts("CommercialTerms", PageMarked(CleanPage1, CleanPage2));

        Assert.Equal("48000", Value(facts["annualSpend"]));
        Assert.Equal(FixtureContractFactExtractor.StrongConfidence, Confidence(facts["annualSpend"]));
        Assert.Equal("EUR 48,000,", Span(facts["annualSpend"]));

        Assert.Equal("144000", Value(facts["totalContractValue"]));
        Assert.Equal(FixtureContractFactExtractor.StrongConfidence, Confidence(facts["totalContractValue"]));

        Assert.Equal("Net 30", Value(facts["paymentTerms"]));
        Assert.Equal(FixtureContractFactExtractor.StrongConfidence, Confidence(facts["paymentTerms"]));
    }

    [Fact]
    public void Clean_msa_dates_are_read_or_honestly_derived_and_renewal_terms_are_explicit()
    {
        var facts = Facts("DatesAndRenewalTerms", PageMarked(CleanPage1, CleanPage2));

        Assert.Equal("2026-01-01", Value(facts["effectiveDate"]));
        Assert.Equal(FixtureContractFactExtractor.StrongConfidence, Confidence(facts["effectiveDate"]));

        // No explicit start clause: the start date is the effective date, flagged as derived.
        Assert.Equal("2026-01-01", Value(facts["startDate"]));
        Assert.Equal(FixtureContractFactExtractor.DerivedConfidence, Confidence(facts["startDate"]));

        // 2026-01-01 + 36 months - 1 day, derived from the initial-term clause on page 2.
        Assert.Equal("2028-12-31", Value(facts["endDate"]));
        Assert.Equal(FixtureContractFactExtractor.DerivedConfidence, Confidence(facts["endDate"]));
        Assert.Equal(2, Page(facts["endDate"]));

        Assert.Equal("true", Value(facts["autoRenewal"]));
        Assert.Equal(FixtureContractFactExtractor.StrongConfidence, Confidence(facts["autoRenewal"]));

        Assert.Equal("12", Value(facts["renewalTermMonths"]));

        // End date minus the ninety-day notice period.
        Assert.Equal("2028-10-02", Value(facts["cancellationDeadline"]));
        Assert.Equal(FixtureContractFactExtractor.DerivedConfidence, Confidence(facts["cancellationDeadline"]));
    }

    [Fact]
    public void Every_clean_msa_fact_clears_the_pipelines_review_bars()
    {
        var text = PageMarked(CleanPage1, CleanPage2);
        var all = new[] { "Metadata", "CommercialTerms", "DatesAndRenewalTerms" }
            .SelectMany(stage => Facts(stage, text))
            .ToList();

        Assert.NotEmpty(all);
        Assert.All(all, fact => Assert.True(Confidence(fact.Value) >= 0.8, $"{fact.Key} at {Confidence(fact.Value)}"));
    }

    [Fact]
    public void Ambiguous_msa_proposes_the_unlabelled_supplier_conflicting_fee_and_contradictory_renewal_at_low_confidence()
    {
        var text = PageMarked(AmbiguousPage1, AmbiguousPage2);
        var metadata = Facts("Metadata", text);
        var commercial = Facts("CommercialTerms", text);
        var dates = Facts("DatesAndRenewalTerms", text);

        // Two parties, no role label: the second party is proposed, not trusted.
        Assert.Equal("Fabrikam Software GmbH", Value(metadata["supplier"]));
        Assert.Equal(FixtureContractFactExtractor.WeakConfidence, Confidence(metadata["supplier"]));

        // Two different annual amounts in the text: the first is proposed at low confidence.
        Assert.Equal("36000", Value(commercial["annualSpend"]));
        Assert.Equal(FixtureContractFactExtractor.WeakConfidence, Confidence(commercial["annualSpend"]));

        // "renews automatically" and "shall not automatically renew" in the same clause.
        Assert.Equal(FixtureContractFactExtractor.WeakConfidence, Confidence(dates["autoRenewal"]));

        // Everything the text states plainly is still trusted.
        Assert.Equal("EUR", Value(metadata["currency"]));
        Assert.Equal(FixtureContractFactExtractor.StrongConfidence, Confidence(metadata["currency"]));
        Assert.Equal("Germany", Value(metadata["governingLaw"]));
        Assert.Equal("Net 45", Value(commercial["paymentTerms"]));
        Assert.Equal("2026-02-01", Value(dates["effectiveDate"]));
        Assert.Equal("2028-01-31", Value(dates["endDate"]));
        Assert.Equal("12", Value(dates["renewalTermMonths"]));
        Assert.Equal("2027-12-02", Value(dates["cancellationDeadline"]));

        var weak = metadata.Concat(commercial).Concat(dates)
            .Where(f => Confidence(f.Value) < 0.6)
            .Select(f => f.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(["annualSpend", "autoRenewal", "supplier"], weak);
    }

    [Fact]
    public void A_text_without_a_cue_yields_no_fact_for_that_field_rather_than_a_guess()
    {
        var facts = Facts("CommercialTerms", "[[PAGE 1]]\nThis agreement contains no commercial terms at all.\n\n");

        Assert.Empty(facts);
    }

    [Fact]
    public void Pages_are_resolved_from_the_markers_and_never_invented_without_them()
    {
        var marked = Facts("Metadata", PageMarked("Nothing here.", "This Agreement is governed by the laws of Ireland."));
        Assert.Equal(2, Page(marked["governingLaw"]));

        var unmarked = Facts("Metadata", "This Agreement is governed by the laws of Ireland.");
        Assert.Null(Page(unmarked["governingLaw"]));
    }

    [Fact]
    public void A_party_name_keeps_its_comma_separated_legal_form_but_drops_a_trailing_place()
    {
        var withSuffix = Facts("Metadata", "between Contoso Ltd (\"Customer\") and Salesforce, Inc. (\"Supplier\").");
        Assert.Equal("Salesforce, Inc.", Value(withSuffix["supplier"]));

        var withPlace = Facts("Metadata", "between Contoso Ltd (\"Customer\") and Northwind Traders SA, Lisbon (\"Supplier\").");
        Assert.Equal("Northwind Traders SA", Value(withPlace["supplier"]));
    }

    [Fact]
    public void Two_currencies_in_one_document_make_the_currency_a_low_confidence_fact()
    {
        var facts = Facts("Metadata", "Fees are EUR 10,000 per year. Liability is capped at USD 50,000.");

        Assert.Equal("EUR", Value(facts["currency"]));
        Assert.Equal(FixtureContractFactExtractor.WeakConfidence, Confidence(facts["currency"]));
    }

    [Theory]
    [InlineData("LineItems")]
    [InlineData("LegalClauses")]
    [InlineData("Obligations")]
    [InlineData("Risk")]
    public void List_stages_return_an_empty_item_list(string stage)
    {
        var payload = FixtureContractFactExtractor.Extract(stage, PageMarked(CleanPage1, CleanPage2));

        using var document = JsonDocument.Parse(payload);
        Assert.Equal(0, document.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void An_unknown_stage_returns_an_empty_facts_list()
    {
        var payload = FixtureContractFactExtractor.Extract("SomethingElse", PageMarked(CleanPage1));

        using var document = JsonDocument.Parse(payload);
        Assert.Equal(0, document.RootElement.GetProperty("facts").GetArrayLength());
    }

    [Fact]
    public async Task The_gateway_serves_the_extractor_payload_with_the_configured_model_metadata()
    {
        var gateway = new FixtureAiGateway(
            new AiGatewayModelOptions { Extract = new AiModelSelection("fixture-extract", "1") },
            new FixedClock(Now));

        var result = await gateway.ExtractAsync(new AiExtractionRequest(
            StageName: "Metadata",
            DocumentText: PageMarked(CleanPage1, CleanPage2),
            JsonSchema: """{"type":"object"}"""));

        Assert.True(result.IsSuccess);
        Assert.Equal("fixture-extract", result.Value.Metadata.ModelId);
        using var document = JsonDocument.Parse(result.Value.PayloadJson);
        Assert.Contains(
            document.RootElement.GetProperty("facts").EnumerateArray(),
            f => f.GetProperty("field").GetString() == "supplier" && f.GetProperty("value").GetString() == "Northwind Traders SA");
    }
}
