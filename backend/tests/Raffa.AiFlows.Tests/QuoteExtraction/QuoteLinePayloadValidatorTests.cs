using Microsoft.EntityFrameworkCore;
using Raffa.AiFlows.QuoteExtraction.Agents;
using Raffa.AiFlows.QuoteExtraction.Schemas;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.QuoteExtraction;

/// <summary>
/// Task F6-T06 (payload validation, schema v2): numbers that do not fit their column and texts
/// longer than their column drop only their own row (counted as invalid) instead of making the
/// whole save throw. No database needed: <see cref="QuoteLineExtractionService.ApplyExtractedLines"/>
/// only adds to the change tracker.
/// </summary>
public sealed class QuoteLinePayloadValidatorTests
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
        var outcome = new QuoteLineExtractionService(db).ApplyExtractedLines(
            TenantId.New(), EntityId.New(), payload, pageCount: 1, DateTimeOffset.UtcNow);
        return (outcome, db.ChangeTracker.Entries<QuoteLine>().Select(e => e.Entity).ToList());
    }

    [Fact]
    public void Text_limits_match_the_quote_line_columns()
    {
        using var db = CreateUnopenedContext();
        var entity = db.Model.FindEntityType(typeof(QuoteLine))!;
        int? Max(string name) => entity.FindProperty(name)!.GetMaxLength();

        Assert.Equal(QuoteLinePayloadValidator.MaxSkuLength, Max(nameof(QuoteLine.Sku)));
        Assert.Equal(QuoteLinePayloadValidator.MaxEditionLength, Max(nameof(QuoteLine.Edition)));
        Assert.Equal(QuoteLinePayloadValidator.MaxDescriptionLength, Max(nameof(QuoteLine.Description)));
        Assert.Equal(QuoteLinePayloadValidator.MaxUnitLength, Max(nameof(QuoteLine.Unit)));
        Assert.Equal(QuoteLinePayloadValidator.MaxTermLength, Max(nameof(QuoteLine.Term)));
        Assert.Equal(QuoteLinePayloadValidator.MaxSourceSpanLength, Max(nameof(QuoteLine.SourceSpan)));
    }

    [Theory]
    [InlineData("quantity", "100000000000000")]
    [InlineData("unitPrice", "10000000000000000")]
    [InlineData("listPrice", "10000000000000000")]
    public void A_number_too_large_for_its_column_is_refused(string field, string value)
    {
        var payload = $$"""
            {"items":[
                {"description":"Good","quantity":1,"unitPrice":5},
                {"description":"Huge","{{field}}":{{value}}}
            ]}
            """;

        var (outcome, lines) = Apply(payload);

        Assert.Equal(1, outcome.ExtractedCount);
        Assert.Equal(1, outcome.InvalidCount);
        Assert.Equal(1, outcome.SkippedCount);
        Assert.Equal("Good", Assert.Single(lines).Description);
    }

    [Fact]
    public void An_extended_price_that_would_overflow_its_column_refuses_the_row()
    {
        const string payload = """{"items":[{"description":"Big","quantity":99999999999999,"unitPrice":99999999}]}""";

        var (outcome, lines) = Apply(payload);

        Assert.Equal(0, outcome.ExtractedCount);
        Assert.Equal(1, outcome.InvalidCount);
        Assert.Empty(lines);
    }

    [Fact]
    public void A_decimal_overflowing_product_is_refused_not_thrown()
    {
        // 99999999999999 x 9999999999999999 exceeds decimal.MaxValue's headroom only when scaled; the
        // multiplication must be guarded either way.
        const string payload = """{"items":[{"description":"Big","quantity":99999999999999,"unitPrice":9999999999999999}]}""";

        var (outcome, _) = Apply(payload);

        Assert.Equal(1, outcome.InvalidCount);
    }

    [Theory]
    [InlineData("sku", 101)]
    [InlineData("edition", 101)]
    [InlineData("unit", 51)]
    [InlineData("term", 101)]
    public void A_key_or_term_longer_than_its_column_is_refused_not_cut(string field, int length)
    {
        var payload = $$"""{"items":[{"description":"Long {{field}}","{{field}}":"{{new string('x', length)}}"}]}""";

        var (outcome, lines) = Apply(payload);

        Assert.Equal(0, outcome.ExtractedCount);
        Assert.Equal(1, outcome.InvalidCount);
        Assert.Empty(lines);
    }

    [Fact]
    public void A_description_over_1000_characters_is_refused_and_exactly_1000_is_kept()
    {
        var payload = $$"""
            {"items":[
                {"description":"{{new string('d', 1001)}}"},
                {"description":"{{new string('e', 1000)}}"}
            ]}
            """;

        var (outcome, lines) = Apply(payload);

        Assert.Equal(1, outcome.ExtractedCount);
        Assert.Equal(1, outcome.InvalidCount);
        Assert.Equal(1000, Assert.Single(lines).Description.Length);
    }

    [Fact]
    public void An_overlong_source_span_is_shortened_because_it_is_a_citation_not_a_key()
    {
        var payload = $$"""{"items":[{"description":"Cited","sourceSpan":"{{new string('s', 2500)}}"}]}""";

        var (outcome, lines) = Apply(payload);

        Assert.Equal(1, outcome.ExtractedCount);
        Assert.Equal(0, outcome.InvalidCount);
        Assert.Equal(QuoteLinePayloadValidator.MaxSourceSpanLength, Assert.Single(lines).SourceSpan!.Length);
    }

    [Fact]
    public void A_nul_character_is_removed_from_every_text_field_the_row_survives()
    {
        const string payload = """
            {"items":[{"description":"Seat\u0000s","sku":"AB\u0000C","edition":"Ent\u0000","unit":"u\u0000","term":"12\u0000 months","sourceSpan":"sp\u0000an"}]}
            """;

        var (outcome, lines) = Apply(payload);

        var line = Assert.Single(lines);
        Assert.Equal(0, outcome.InvalidCount);
        Assert.Equal("Seats", line.Description);
        Assert.Equal("ABC", line.Sku);
        Assert.Equal("Ent", line.Edition);
        Assert.Equal("u", line.Unit);
        Assert.Equal("12 months", line.Term);
        Assert.Equal("span", line.SourceSpan);
    }

    [Fact]
    public void Schema_version_is_two()
    {
        Assert.Equal(2, QuoteLinePayloadValidator.SchemaVersion);
    }
}
