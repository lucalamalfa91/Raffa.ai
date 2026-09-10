using Raffa.Quotes.Application.Assessment;
using Raffa.Quotes.Domain;
using Raffa.SharedKernel;

namespace Raffa.Quotes.Tests;

/// <summary>
/// Proves task E05/F02/US01/T01's own <see cref="MarketAssessmentQueryBuilder"/>: builds a
/// <c>Raffa.Benchmark.Contracts.BenchmarkQuery</c> from a <see cref="Quote"/>/<see cref="QuoteLine"/>
/// pair, or an honest, named failure when a required dimension is missing (Appendix C rule 10) —
/// parent story us-01-market-assessment AC-1 ("Match normalized line items to the Benchmark
/// Service (multi-dimensional)"). Pure — no database.
/// </summary>
public sealed class MarketAssessmentQueryBuilderTests
{
    private static Quote NewQuote(
        string? supplier = "Salesforce",
        string? currency = "USD",
        string? geography = "US",
        DateOnly? purchaseDate = null) => new()
    {
        TenantId = TenantId.New(),
        FileName = "quote.pdf",
        MimeType = "application/pdf",
        StoragePath = "tenant/quote.pdf",
        Checksum = "checksum",
        Supplier = supplier,
        Currency = currency,
        Geography = geography,
        PurchaseDate = purchaseDate ?? new DateOnly(2026, 9, 1),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static QuoteLine NewLine(
        string? description = "Sales Cloud Enterprise",
        decimal? quantity = 100m,
        string? term = "12 months",
        decimal? unitPrice = 1950m,
        string? sku = null,
        string? normalizedSku = null) => new()
    {
        TenantId = TenantId.New(),
        QuoteId = EntityId.New(),
        Description = description!,
        Quantity = quantity,
        Term = term,
        UnitPrice = unitPrice,
        Sku = sku,
        NormalizedSku = normalizedSku,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void Builds_a_multi_dimensional_query_from_a_fully_populated_quote_and_line()
    {
        var quote = NewQuote();
        var line = NewLine(sku: "  ent-500  ", normalizedSku: "ENT-500");

        var result = MarketAssessmentQueryBuilder.Build(quote, line);

        Assert.True(result.IsSuccess);
        var query = result.Value;
        Assert.Equal("Salesforce", query.Supplier);
        Assert.Equal("Sales Cloud Enterprise", query.Product);
        Assert.Equal("ENT-500", query.Sku); // prefers NormalizedSku over the raw Sku
        Assert.Equal("US", query.Geography);
        Assert.Equal(100m, query.Quantity);
        Assert.Equal("12 months", query.Term);
        Assert.Equal("USD", query.Currency);
        Assert.Equal(new DateOnly(2026, 9, 1), query.PurchaseDate);
    }

    [Fact]
    public void Falls_back_to_the_raw_Sku_when_no_normalized_form_exists_yet()
    {
        var result = MarketAssessmentQueryBuilder.Build(NewQuote(), NewLine(sku: "RAW-SKU", normalizedSku: null));

        Assert.True(result.IsSuccess);
        Assert.Equal("RAW-SKU", result.Value.Sku);
    }

    [Fact]
    public void A_line_with_no_Sku_at_all_produces_a_null_Sku_dimension_not_a_fabricated_one()
    {
        var result = MarketAssessmentQueryBuilder.Build(NewQuote(), NewLine(sku: null, normalizedSku: null));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Sku);
    }

    // ----- Honest abstain: missing quote-level dimensions -----

    [Fact]
    public void Fails_honestly_when_the_quote_has_no_supplier()
    {
        var result = MarketAssessmentQueryBuilder.Build(NewQuote(supplier: null), NewLine());

        Assert.True(result.IsFailure);
        Assert.Contains("Supplier", result.Error);
    }

    [Fact]
    public void Fails_honestly_when_the_quote_has_no_currency()
    {
        var result = MarketAssessmentQueryBuilder.Build(NewQuote(currency: "   "), NewLine());

        Assert.True(result.IsFailure);
        Assert.Contains("Currency", result.Error);
    }

    [Fact]
    public void Fails_honestly_when_the_quote_has_no_geography()
    {
        var result = MarketAssessmentQueryBuilder.Build(NewQuote(geography: null), NewLine());

        Assert.True(result.IsFailure);
        Assert.Contains("Geography", result.Error);
    }

    [Fact]
    public void Fails_honestly_when_the_quote_has_no_purchase_date()
    {
        var quote = NewQuote();
        quote.PurchaseDate = null;

        var result = MarketAssessmentQueryBuilder.Build(quote, NewLine());

        Assert.True(result.IsFailure);
        Assert.Contains("PurchaseDate", result.Error);
    }

    // ----- Honest abstain: missing line-level dimensions -----

    [Fact]
    public void Fails_honestly_when_the_line_has_no_description()
    {
        var result = MarketAssessmentQueryBuilder.Build(NewQuote(), NewLine(description: null));

        Assert.True(result.IsFailure);
        Assert.Contains("description", result.Error);
    }

    // xUnit InlineData cannot carry a decimal literal directly (not a valid C# attribute argument
    // type) — double? round-trips through the theory, cast to decimal? for the actual test input.
    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(-1d)]
    public void Fails_honestly_when_the_line_has_no_positive_quantity(double? quantity)
    {
        var result = MarketAssessmentQueryBuilder.Build(NewQuote(), NewLine(quantity: (decimal?)quantity));

        Assert.True(result.IsFailure);
        Assert.Contains("Quantity", result.Error);
    }

    [Fact]
    public void Fails_honestly_when_the_line_has_no_term()
    {
        var result = MarketAssessmentQueryBuilder.Build(NewQuote(), NewLine(term: null));

        Assert.True(result.IsFailure);
        Assert.Contains("Term", result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(-1d)]
    public void Fails_honestly_when_the_line_has_no_positive_unit_price(double? unitPrice)
    {
        var result = MarketAssessmentQueryBuilder.Build(NewQuote(), NewLine(unitPrice: (decimal?)unitPrice));

        Assert.True(result.IsFailure);
        Assert.Contains("UnitPrice", result.Error);
    }

    [Fact]
    public void Rejects_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => MarketAssessmentQueryBuilder.Build(null!, NewLine()));
        Assert.Throws<ArgumentNullException>(() => MarketAssessmentQueryBuilder.Build(NewQuote(), null!));
    }
}
