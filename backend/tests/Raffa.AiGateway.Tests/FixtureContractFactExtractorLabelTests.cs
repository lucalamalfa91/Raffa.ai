using System.Text.Json;
using Raffa.AiGateway.Fixtures;

namespace Raffa.AiGateway.Tests;

/// <summary>
/// Office tables and order-form labels reach the fixture extract role as
/// <c>Label: Value</c> / <c>Label | Value</c> rows. Those must populate the same Review fields
/// a prose PDF does — without inventing a supplier the text does not name.
/// </summary>
public sealed class FixtureContractFactExtractorLabelTests
{
    private const string TableOrderForm =
        "[[PAGE 1]]\n" +
        "IBM Enterprise Service Order\n" +
        "Supplier: IBM Corporation\n" +
        "Governing law: New York\n" +
        "Annual spend: USD 188000\n" +
        "Total contract value: USD 391000\n" +
        "Payment terms: Net 60\n" +
        "Effective date: 1 January 2026\n" +
        "Start date: 1 January 2026\n" +
        "End date: 31 December 2028\n" +
        "Cancellation deadline: 2 October 2028\n" +
        "This Agreement renews automatically for successive twelve month terms.\n\n";

    private static Dictionary<string, JsonElement> Facts(string stage, string text)
    {
        var payload = FixtureContractFactExtractor.Extract(stage, text);
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("facts").EnumerateArray()
            .ToDictionary(f => f.GetProperty("field").GetString()!, f => f.Clone(), StringComparer.Ordinal);
    }

    private static string Value(JsonElement fact) => fact.GetProperty("value").GetString()!;
    private static double Confidence(JsonElement fact) => fact.GetProperty("confidence").GetDouble();

    [Fact]
    public void A_labelled_order_form_recovers_supplier_dates_law_spend_and_renewal()
    {
        var metadata = Facts("Metadata", TableOrderForm);
        var commercial = Facts("CommercialTerms", TableOrderForm);
        var dates = Facts("DatesAndRenewalTerms", TableOrderForm);

        Assert.Equal("IBM Corporation", Value(metadata["supplier"]));
        Assert.Equal(FixtureContractFactExtractor.StrongConfidence, Confidence(metadata["supplier"]));
        Assert.Equal("USD", Value(metadata["currency"]));
        Assert.Equal("New York", Value(metadata["governingLaw"]));

        Assert.Equal("188000", Value(commercial["annualSpend"]));
        Assert.Equal("391000", Value(commercial["totalContractValue"]));
        Assert.Equal("Net 60", Value(commercial["paymentTerms"]));

        Assert.Equal("2026-01-01", Value(dates["effectiveDate"]));
        Assert.Equal("2026-01-01", Value(dates["startDate"]));
        Assert.Equal("2028-12-31", Value(dates["endDate"]));
        Assert.Equal("2028-10-02", Value(dates["cancellationDeadline"]));
        Assert.Equal("true", Value(dates["autoRenewal"]));
        Assert.True(Confidence(dates["startDate"]) >= 0.90);
        Assert.True(Confidence(dates["endDate"]) >= 0.90);
        Assert.True(Confidence(dates["cancellationDeadline"]) >= 0.90);
    }

    [Fact]
    public void A_pipe_separated_table_row_is_the_same_as_a_colon_label()
    {
        var facts = Facts("Metadata", "[[PAGE 1]]\nSupplier | IBM Corporation\nGoverning law | New York\n\n");

        Assert.Equal("IBM Corporation", Value(facts["supplier"]));
        Assert.Equal(FixtureContractFactExtractor.StrongConfidence, Confidence(facts["supplier"]));
        Assert.Equal("New York", Value(facts["governingLaw"]));
    }

    [Fact]
    public void A_column_header_named_supplier_is_not_invented_as_the_party()
    {
        var facts = Facts(
            "Metadata",
            "[[PAGE 1]]\nSupplier | Start date | End date\nIBM Corporation | 1 January 2026 | 31 December 2028\n\n");

        Assert.False(facts.ContainsKey("supplier") && Value(facts["supplier"]) == "Start date");
    }

    [Fact]
    public void A_document_without_a_supplier_cue_does_not_invent_one()
    {
        var facts = Facts("Metadata", "[[PAGE 1]]\nThis agreement contains no party names at all.\n\n");

        Assert.False(facts.ContainsKey("supplier"));
    }
}
