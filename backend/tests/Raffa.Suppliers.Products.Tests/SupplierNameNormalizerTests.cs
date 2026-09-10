using Raffa.Suppliers.Products.Application;

namespace Raffa.Suppliers.Products.Tests;

/// <summary>
/// Proves the Definition of Done for task E13/F03/US01/T01 (parent story
/// us-01-supplier-identity AC-2): "Salesforce, Inc.", "salesforce" and "SALESFORCE INC" all
/// normalize to the same key, every legal suffix `inputs/requirements.md` R-SUP-02 names collapses
/// correctly (dotted and undotted spellings alike), and punctuation/whitespace never leaks into
/// the normalized form. Pure unit tests — no database, no Testcontainers.
/// </summary>
public sealed class SupplierNameNormalizerTests
{
    [Theory]
    [InlineData("Salesforce, Inc.")]
    [InlineData("salesforce")]
    [InlineData("SALESFORCE INC")]
    [InlineData("  Salesforce   Inc.  ")]
    public void Normalize_folds_every_spelling_of_salesforce_onto_the_same_key(string rawName)
    {
        Assert.Equal("salesforce", SupplierNameNormalizer.Normalize(rawName));
    }

    [Theory]
    [InlineData("Foo Inc", "foo")]
    [InlineData("Foo Inc.", "foo")]
    [InlineData("Foo Ltd", "foo")]
    [InlineData("Foo Limited", "foo")]
    [InlineData("Foo GmbH", "foo")]
    [InlineData("Foo AG", "foo")]
    [InlineData("Foo SA", "foo")]
    [InlineData("Foo S.A.", "foo")]
    [InlineData("Foo SpA", "foo")]
    [InlineData("Foo S.p.A.", "foo")]
    [InlineData("Foo S.r.l.", "foo")]
    [InlineData("Foo Srl", "foo")]
    [InlineData("Foo LLC", "foo")]
    [InlineData("Foo Corp", "foo")]
    [InlineData("Foo Corporation", "foo")]
    [InlineData("Foo Co.", "foo")]
    public void Normalize_strips_every_named_legal_suffix_dotted_and_undotted_alike(
        string rawName, string expected)
    {
        Assert.Equal(expected, SupplierNameNormalizer.Normalize(rawName));
    }

    [Fact]
    public void Normalize_is_case_insensitive()
    {
        Assert.Equal(
            SupplierNameNormalizer.Normalize("ALLIANZ"),
            SupplierNameNormalizer.Normalize("Allianz"));
    }

    [Fact]
    public void Normalize_strips_punctuation_without_fragmenting_dotted_abbreviations()
    {
        // "Ferrari S.p.A." must fold onto the exact same key as "Ferrari SpA" -- if periods were
        // replaced with spaces instead of dropped outright, "s.p.a." would fragment into three
        // separate single-letter words ("s", "p", "a") and never match the "spa" suffix token.
        Assert.Equal(
            SupplierNameNormalizer.Normalize("Ferrari SpA"),
            SupplierNameNormalizer.Normalize("Ferrari S.p.A."));
    }

    [Fact]
    public void Normalize_drops_punctuation_that_is_not_a_legal_suffix_marker()
    {
        Assert.Equal("att", SupplierNameNormalizer.Normalize("AT&T, Inc."));
    }

    [Fact]
    public void Normalize_collapses_repeated_internal_whitespace()
    {
        Assert.Equal("foo bar", SupplierNameNormalizer.Normalize("  Foo    Bar  "));
    }

    [Fact]
    public void Normalize_strips_more_than_one_trailing_suffix_word()
    {
        // A name could legitimately carry two suffix-shaped trailing words (e.g. after a merger);
        // both are stripped, not just the last one.
        Assert.Equal("foo", SupplierNameNormalizer.Normalize("Foo Ltd Corp"));
    }

    [Fact]
    public void Normalize_never_strips_down_to_an_empty_string()
    {
        // A supplier literally named after a bare suffix token still normalizes to something --
        // the "at least one word survives" guard.
        Assert.Equal("ltd", SupplierNameNormalizer.Normalize("Ltd"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_rejects_a_blank_name(string? rawName)
    {
        // ThrowsAny, not Throws: a null input throws ArgumentNullException specifically (a
        // subtype of ArgumentException), and xUnit's Assert.Throws<T> requires an exact type
        // match rather than accepting a derived type.
        Assert.ThrowsAny<ArgumentException>(() => SupplierNameNormalizer.Normalize(rawName!));
    }
}
