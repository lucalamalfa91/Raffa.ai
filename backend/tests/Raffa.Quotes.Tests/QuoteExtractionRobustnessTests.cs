using Raffa.Quotes.Application.Extraction;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Quotes.Tests;

/// <summary>
/// Tasks F6-T04 (payload validation: negative quantity/price, discount outside 0-100, confidence
/// outside 0-1 are discarded and counted) and F6-T02 (typed failure text). Unlike the sibling
/// extraction tests this needs no database: <see cref="QuoteLineExtractionService.ApplyExtractedLines"/>
/// only adds rows to the change tracker, which is what these tests read, so a never-opened context
/// is enough and the tests run without Docker.
/// </summary>
public sealed class QuoteExtractionRobustnessTests
{
    private static QuotesDbContext CreateUnopenedContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<QuotesDbContext>();
        QuotesDbContextOptions.Configure(optionsBuilder, "Host=localhost;Database=never-opened");
        return new QuotesDbContext(optionsBuilder.Options);
    }

    private static (QuoteLineExtractionOutcome Outcome, IReadOnlyList<QuoteLine> Lines) Apply(string payload)
    {
        using var db = CreateUnopenedContext();
        var service = new QuoteLineExtractionService(db);
        var outcome = service.ApplyExtractedLines(
            TenantId.New(), EntityId.New(), payload, pageCount: 1, DateTimeOffset.UtcNow);
        var lines = db.ChangeTracker.Entries<QuoteLine>().Select(e => e.Entity).ToList();
        return (outcome, lines);
    }

    [Theory]
    [InlineData("\"quantity\":-1,\"unitPrice\":10")]
    [InlineData("\"quantity\":1,\"unitPrice\":-0.01")]
    [InlineData("\"quantity\":1,\"listPrice\":-10")]
    [InlineData("\"quantity\":1,\"listPrice\":10,\"discountPercent\":100.5")]
    [InlineData("\"quantity\":1,\"listPrice\":10,\"discountPercent\":-5")]
    [InlineData("\"quantity\":1,\"unitPrice\":10,\"confidence\":1.01")]
    [InlineData("\"quantity\":1,\"unitPrice\":10,\"confidence\":-0.2")]
    public void A_row_with_an_impossible_value_is_discarded_and_counted(string impossibleFields)
    {
        var payload = $$"""
            {"items":[
                {"description":"Good line","quantity":2,"unitPrice":10,"confidence":0.9},
                {"description":"Bad line",{{impossibleFields}}}
            ]}
            """;

        var (outcome, lines) = Apply(payload);

        Assert.Equal(1, outcome.ExtractedCount);
        Assert.Equal(1, outcome.InvalidCount);
        // Counted as skipped too, so the pipeline lands the quote in NeedsReview.
        Assert.Equal(1, outcome.SkippedCount);
        Assert.Equal("Good line", Assert.Single(lines).Description);
    }

    [Fact]
    public void Boundary_values_are_valid_zero_quantity_zero_price_full_discount_and_confidence_0_and_1()
    {
        const string payload = """
            {"items":[
                {"description":"Free line","quantity":0,"unitPrice":0,"confidence":0},
                {"description":"Fully discounted","quantity":1,"listPrice":10,"discountPercent":100,"confidence":1},
                {"description":"No discount","quantity":1,"listPrice":10,"discountPercent":0,"confidence":0.7}
            ]}
            """;

        var (outcome, lines) = Apply(payload);

        Assert.Equal(3, outcome.ExtractedCount);
        Assert.Equal(0, outcome.InvalidCount);
        Assert.Equal(0, outcome.SkippedCount);
        Assert.Equal(3, lines.Count);
    }

    [Fact]
    public void Absent_values_are_not_invalid_a_row_with_only_a_description_survives()
    {
        const string payload = """{"items":[{"description":"Description only"}]}""";

        var (outcome, lines) = Apply(payload);

        Assert.Equal(1, outcome.ExtractedCount);
        Assert.Equal(0, outcome.InvalidCount);
        Assert.Single(lines);
    }

    [Fact]
    public void A_blank_description_is_skipped_but_is_not_counted_as_invalid()
    {
        const string payload = """{"items":[{"description":" ","quantity":1,"unitPrice":1,"confidence":0.9}]}""";

        var (outcome, _) = Apply(payload);

        Assert.Equal(0, outcome.ExtractedCount);
        Assert.Equal(1, outcome.SkippedCount);
        Assert.Equal(0, outcome.InvalidCount);
    }

    [Fact]
    public void An_invalid_row_never_contributes_to_the_low_confidence_flag_or_to_pricing()
    {
        // The bad row has confidence 0.1 (would flag review) and a negative price; it is dropped
        // before either is considered, while the good row alone decides the outcome.
        const string payload = """
            {"items":[
                {"description":"Good line","quantity":1,"unitPrice":10,"confidence":0.95},
                {"description":"Bad line","quantity":1,"unitPrice":-10,"confidence":0.1}
            ]}
            """;

        var (outcome, lines) = Apply(payload);

        Assert.False(outcome.AnyLowConfidence);
        Assert.Equal(10m, Assert.Single(lines).ExtendedPrice);
    }

    // ----- typed failure text (F6-T02) -----

    [Theory]
    [InlineData(QuoteExtractionFailureKind.ParseFailed)]
    [InlineData(QuoteExtractionFailureKind.NoReadableText)]
    [InlineData(QuoteExtractionFailureKind.ExtractionFailed)]
    [InlineData(QuoteExtractionFailureKind.MalformedPayload)]
    [InlineData(QuoteExtractionFailureKind.PersistenceFailed)]
    [InlineData(QuoteExtractionFailureKind.Cancelled)]
    [InlineData(QuoteExtractionFailureKind.Unexpected)]
    public void A_formatted_failure_round_trips_its_kind(QuoteExtractionFailureKind kind)
    {
        var text = QuoteExtractionFailure.Format(kind, "something went wrong");

        Assert.Equal($"[{kind}] something went wrong", text);
        Assert.True(QuoteExtractionFailure.TryParseKind(text, out var parsed));
        Assert.Equal(kind, parsed);
    }

    [Fact]
    public void A_failure_is_truncated_to_the_column_budget_and_keeps_its_kind()
    {
        var text = QuoteExtractionFailure.Format(
            QuoteExtractionFailureKind.ExtractionFailed, new string('x', 5000));

        Assert.Equal(QuoteExtractionFailure.MaxErrorDetailLength, text.Length);
        Assert.True(QuoteExtractionFailure.TryParseKind(text, out var kind));
        Assert.Equal(QuoteExtractionFailureKind.ExtractionFailed, kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plain text written before the typing existed")]
    [InlineData("[NotAKind] x")]
    [InlineData("[] x")]
    [InlineData("[7] x")]
    [InlineData("[1] x")]
    public void An_untyped_or_unknown_error_detail_does_not_parse(string? errorDetail)
    {
        Assert.False(QuoteExtractionFailure.TryParseKind(errorDetail, out _));
    }
}
