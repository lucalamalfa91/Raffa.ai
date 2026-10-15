using Raffa.Quotes.Application.Assessment;
using Raffa.Quotes.Application.Normalization;
using Raffa.Quotes.Domain;
using Raffa.SharedKernel;

namespace Raffa.Quotes.Tests;

/// <summary>
/// Task F6-T08 (normalization used by the assessment): a commitment duration stated in months or
/// years reaches the benchmark as <c>"{n} months"</c>; anything else goes through as written. The
/// motivating case is <c>"36 months (3 years)"</c>, which a matcher that concatenates the digits of the
/// text reads as 363.
/// </summary>
public sealed class QuoteTermParserTests
{
    [Theory]
    [InlineData("36 months (3 years)", 36)]
    [InlineData("3 years (36 months)", 36)]
    [InlineData("36 months", 36)]
    [InlineData("12 months", 12)]
    [InlineData("12-month", 12)]
    [InlineData("12 Months", 12)]
    [InlineData("1 year", 12)]
    [InlineData("3 years", 36)]
    [InlineData("3-year term", 36)]
    [InlineData("1.5 years", 18)]
    [InlineData("1,5 anni", 18)]
    [InlineData("24 mo", 24)]
    [InlineData("three years", 36)]
    [InlineData("Initial term: 36 months, renewing for 36 months", 36)]
    // Italian, German, French, Spanish.
    [InlineData("36 mesi", 36)]
    [InlineData("3 anni", 36)]
    [InlineData("12 Monate", 12)]
    [InlineData("3 Jahre", 36)]
    [InlineData("24 mois", 24)]
    [InlineData("2 ans", 24)]
    [InlineData("2 années", 24)]
    [InlineData("12 meses", 12)]
    [InlineData("2 años", 24)]
    public void A_stated_duration_is_read_in_months(string term, int expected)
    {
        Assert.Equal(expected, QuoteTermParser.TryParseMonths(term));
        Assert.Equal($"{expected} months", QuoteTermParser.Canonicalize(term));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Annual")]
    [InlineData("monthly")]
    [InlineData("one-time")]
    [InlineData("perpetual")]
    [InlineData("30 days")]
    [InlineData("net 30")]
    [InlineData("1.5 months")]
    [InlineData("0 months")]
    [InlineData("500 months")]
    [InlineData("messaggi 12")]
    public void A_text_without_a_duration_is_not_read(string? term)
    {
        Assert.Null(QuoteTermParser.TryParseMonths(term));
    }

    [Theory]
    [InlineData("12 months, option for 3 years")]
    [InlineData("12 months or 24 months")]
    public void Two_different_durations_are_ambiguous_and_not_guessed(string term)
    {
        Assert.Null(QuoteTermParser.TryParseMonths(term));
        Assert.Equal(term, QuoteTermParser.Canonicalize(term));
    }

    [Theory]
    [InlineData("Annual")]
    [InlineData("one-time")]
    [InlineData("net 30")]
    public void Canonicalize_leaves_other_text_exactly_as_written(string term)
    {
        Assert.Equal(term, QuoteTermParser.Canonicalize(term));
    }

    private static Quote NewQuote() => new()
    {
        TenantId = TenantId.New(),
        FileName = "q.pdf",
        MimeType = "application/pdf",
        StoragePath = "x",
        Checksum = "c",
        Supplier = "Northwind",
        Currency = "EUR",
        Geography = "IT",
        PurchaseDate = new DateOnly(2026, 9, 1),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static QuoteLine NewLine(string term, EntityId quoteId) => new()
    {
        TenantId = TenantId.New(),
        QuoteId = quoteId,
        Description = "Cloud Suite",
        Quantity = 10,
        UnitPrice = 100m,
        Term = term,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Theory]
    [InlineData("36 months (3 years)", "36 months")]
    [InlineData("3 years", "36 months")]
    [InlineData("12 months", "12 months")]
    [InlineData("Annual", "Annual")]
    [InlineData("one-time", "one-time")]
    public void The_benchmark_query_carries_the_canonical_term(string lineTerm, string expectedQueryTerm)
    {
        var quote = NewQuote();

        var result = MarketAssessmentQueryBuilder.Build(quote, NewLine(lineTerm, quote.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedQueryTerm, result.Value.Term);
    }
}
