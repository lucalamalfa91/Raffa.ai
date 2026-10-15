using System.Text.Json;
using Raffa.AiFlows.QuoteExtraction;
using Raffa.AiFlows.QuoteExtraction.Agents;
using Raffa.AiFlows.QuoteExtraction.Orchestration;
using Raffa.AiFlows.QuoteExtraction.Schemas;
using Raffa.AiGateway.Foundry;
using Raffa.Quotes.Domain;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.QuoteExtraction;

/// <summary>
/// Task F6-T07 (header stage): the schema, and the rules that decide which of the model's header
/// values may be recorded. Pure: no database, no gateway.
/// </summary>
public sealed class QuoteHeaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static Quote NewQuote(
        string? supplier = null, string? currency = null, string? geography = null, DateOnly? purchaseDate = null) => new()
    {
        TenantId = TenantId.New(),
        FileName = "q.pdf",
        MimeType = "application/pdf",
        StoragePath = "x",
        Checksum = "c",
        Supplier = supplier,
        Currency = currency,
        Geography = geography,
        // The upload defaults the date to the upload day when the caller sends none.
        PurchaseDate = purchaseDate ?? DateOnly.FromDateTime(Now.UtcDateTime),
        CreatedAt = Now,
    };

    private static string Payload(
        string? supplier = "Northwind Traders", string? date = "2026-09-20", string? currency = "EUR",
        string? geography = "IT", double confidence = 0.9)
    {
        static string Field(string? value, double confidence) =>
            $$"""{"value":{{(value is null ? "null" : JsonSerializer.Serialize(value))}},"sourcePage":1,"sourceSpan":"x","confidence":{{confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}""";

        return $$"""
            {"supplier":{{Field(supplier, confidence)}},"quoteDate":{{Field(date, confidence)}},
             "currency":{{Field(currency, confidence)}},"geography":{{Field(geography, confidence)}}}
            """;
    }

    // ----- schema -----

    [Fact]
    public void The_schema_is_strict_mode_valid_and_asks_for_exactly_the_four_header_fields()
    {
        var schema = QuoteHeaderJsonSchema.Header();

        Assert.True(StrictJsonSchemaValidator.Validate(schema).IsSuccess, "the header schema must be accepted by strict structured outputs");
        using var document = JsonDocument.Parse(schema);
        var properties = document.RootElement.GetProperty("properties");
        Assert.Equal(
            ["supplier", "quoteDate", "currency", "geography"],
            properties.EnumerateObject().Select(p => p.Name).ToArray());
        foreach (var field in properties.EnumerateObject())
        {
            Assert.Equal(
                ["value", "sourcePage", "sourceSpan", "confidence"],
                field.Value.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).ToArray());
        }
    }

    [Fact]
    public void The_header_stage_has_its_own_label_and_the_line_label_is_untouched()
    {
        Assert.Equal("QuoteHeader", QuoteExtractionStages.HeaderStageName);
        Assert.Equal("QuoteLineItems", QuoteExtractionStages.StageName);
    }

    [Fact]
    public void The_header_stage_is_off_by_default()
    {
        var options = new QuoteExtractionOptions();

        Assert.False(options.HeaderStageEnabled);
        Assert.Equal(0.7, options.HeaderMinConfidence);
    }

    // ----- applying -----

    [Fact]
    public void Blank_fields_are_filled_from_confident_valid_values()
    {
        var quote = NewQuote();

        var outcome = QuoteHeaderApplier.Apply(quote, Payload(), Now, 0.7);

        Assert.Equal(["supplier", "currency", "geography", "purchaseDate"], outcome.Filled);
        Assert.Empty(outcome.Abstained);
        Assert.Equal("Northwind Traders", quote.Supplier);
        Assert.Equal("EUR", quote.Currency);
        Assert.Equal("IT", quote.Geography);
        Assert.Equal(new DateOnly(2026, 9, 20), quote.PurchaseDate);
    }

    [Fact]
    public void A_value_a_person_gave_is_never_overwritten()
    {
        var quote = NewQuote(
            supplier: "Fabrikam", currency: "USD", geography: "US", purchaseDate: new DateOnly(2026, 8, 1));

        var outcome = QuoteHeaderApplier.Apply(quote, Payload(), Now, 0.7);

        Assert.Empty(outcome.Filled);
        Assert.Empty(outcome.Abstained);
        Assert.Equal("Fabrikam", quote.Supplier);
        Assert.Equal("USD", quote.Currency);
        Assert.Equal("US", quote.Geography);
        Assert.Equal(new DateOnly(2026, 8, 1), quote.PurchaseDate);
    }

    [Fact]
    public void Only_the_missing_fields_are_filled()
    {
        var quote = NewQuote(supplier: "Fabrikam", currency: "USD");

        var outcome = QuoteHeaderApplier.Apply(quote, Payload(), Now, 0.7);

        Assert.Equal(["geography", "purchaseDate"], outcome.Filled);
        Assert.Equal("Fabrikam", quote.Supplier);
        Assert.Equal("USD", quote.Currency);
    }

    [Fact]
    public void A_field_below_the_confidence_floor_stays_blank()
    {
        var quote = NewQuote();

        var outcome = QuoteHeaderApplier.Apply(quote, Payload(confidence: 0.69), Now, 0.7);

        Assert.Empty(outcome.Filled);
        Assert.Equal(["supplier", "currency", "geography", "purchaseDate"], outcome.Abstained);
        Assert.Null(quote.Supplier);
        Assert.Null(quote.Currency);
        Assert.Null(quote.Geography);
    }

    [Fact]
    public void The_confidence_floor_is_inclusive()
    {
        var quote = NewQuote();

        var outcome = QuoteHeaderApplier.Apply(quote, Payload(confidence: 0.7), Now, 0.7);

        Assert.Equal(4, outcome.Filled.Count);
    }

    [Theory]
    [InlineData("currency", "euro")]
    [InlineData("currency", "EU")]
    [InlineData("currency", "€")]
    [InlineData("geography", "Italy")]
    [InlineData("geography", "I")]
    [InlineData("date", "20/09/2026")]
    [InlineData("date", "2026-13-40")]
    [InlineData("date", "1999-12-31")]
    [InlineData("date", "2026-12-31")]
    [InlineData("supplier", " ")]
    [InlineData("supplier", "X")]
    public void An_invalid_value_is_declined_not_repaired(string field, string value)
    {
        var quote = NewQuote();
        var payload = field switch
        {
            "currency" => Payload(currency: value),
            "geography" => Payload(geography: value),
            "date" => Payload(date: value),
            _ => Payload(supplier: value),
        };

        var outcome = QuoteHeaderApplier.Apply(quote, payload, Now, 0.7);

        var name = field == "date" ? "purchaseDate" : field;
        Assert.Contains(name, outcome.Abstained);
        Assert.DoesNotContain(name, outcome.Filled);
    }

    [Fact]
    public void Values_are_normalised_a_lowercase_currency_and_a_padded_supplier()
    {
        var quote = NewQuote();

        QuoteHeaderApplier.Apply(quote, Payload(supplier: "  Northwind   Traders ", currency: "eur", geography: "it"), Now, 0.7);

        Assert.Equal("Northwind Traders", quote.Supplier);
        Assert.Equal("EUR", quote.Currency);
        Assert.Equal("IT", quote.Geography);
    }

    [Fact]
    public void A_null_value_means_the_document_does_not_say_and_is_declined()
    {
        var quote = NewQuote();

        var outcome = QuoteHeaderApplier.Apply(quote, Payload(supplier: null, currency: null), Now, 0.7);

        Assert.Equal(["supplier", "currency"], outcome.Abstained);
        Assert.Null(quote.Supplier);
        Assert.Null(quote.Currency);
    }

    [Fact]
    public void An_empty_answer_declines_every_blank_field_and_changes_nothing()
    {
        var quote = NewQuote();
        var defaultedDate = quote.PurchaseDate;

        var outcome = QuoteHeaderApplier.Apply(quote, "{}", Now, 0.7);

        Assert.Empty(outcome.Filled);
        Assert.Equal(4, outcome.Abstained.Count);
        Assert.Equal(defaultedDate, quote.PurchaseDate);
    }

    [Fact]
    public void Text_that_is_not_json_throws_so_the_pipeline_can_skip_the_stage()
    {
        Assert.ThrowsAny<JsonException>(() => QuoteHeaderApplier.Apply(NewQuote(), "not json", Now, 0.7));
    }

    // ----- when the stage has something to do -----

    [Fact]
    public void A_complete_header_with_a_stated_date_needs_no_stage()
    {
        var complete = NewQuote("Northwind", "EUR", "IT", new DateOnly(2026, 8, 1));

        Assert.False(QuoteHeaderApplier.IsIncomplete(complete));
    }

    [Fact]
    public void A_date_that_is_still_the_upload_day_default_counts_as_missing()
    {
        var quote = NewQuote("Northwind", "EUR", "IT");

        Assert.True(QuoteHeaderApplier.IsIncomplete(quote));
        Assert.True(QuoteHeaderApplier.IsDefaultedDate(quote));
    }

    [Theory]
    [InlineData(null, "EUR", "IT")]
    [InlineData("Northwind", null, "IT")]
    [InlineData("Northwind", "EUR", null)]
    [InlineData("Northwind", "EUR", " ")]
    public void Any_blank_field_means_the_stage_has_work(string? supplier, string? currency, string? geography)
    {
        var quote = NewQuote(supplier, currency, geography, new DateOnly(2026, 8, 1));

        Assert.True(QuoteHeaderApplier.IsIncomplete(quote));
    }
}
