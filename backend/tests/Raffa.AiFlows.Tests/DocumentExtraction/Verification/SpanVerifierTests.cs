using System.Diagnostics;
using System.Text;
using Raffa.AiFlows.DocumentExtraction.Verification;
using Raffa.Documents.Contracts.Application.Extraction;

namespace Raffa.AiFlows.Tests.DocumentExtraction.Verification;

/// <summary>F5-T07: the pure span verifier.</summary>
public sealed class SpanVerifierTests
{
    private static readonly IReadOnlyList<DocumentPageText> Pages =
    [
        new DocumentPageText(1, "This Agreement is governed by the laws of Italy.\nThe Supplier is Acme S.r.l. and payment is due within 30 days."),
        new DocumentPageText(2, "The Customer's renewal term is twelve months.\nNotice of termination: 90 days prior to the end date."),
    ];

    [Fact]
    public void An_exact_span_on_the_named_page_is_found()
    {
        Assert.Equal(SpanVerdict.Found, SpanVerifier.Verify("payment is due within 30 days", 1, Pages));
    }

    [Fact]
    public void Matching_ignores_case_and_whitespace_runs_and_line_breaks()
    {
        Assert.Equal(SpanVerdict.Found, SpanVerifier.Verify("GOVERNED   by the laws\n of  Italy.", 1, Pages));
    }

    [Fact]
    public void A_span_on_another_page_is_reported_as_such_and_the_page_is_not_corrected()
    {
        Assert.Equal(SpanVerdict.FoundOnOtherPage, SpanVerifier.Verify("renewal term is twelve months", 1, Pages));
    }

    [Fact]
    public void A_span_with_no_usable_page_is_found_anywhere()
    {
        Assert.Equal(SpanVerdict.Found, SpanVerifier.Verify("renewal term is twelve months", null, Pages));
        Assert.Equal(SpanVerdict.Found, SpanVerifier.Verify("renewal term is twelve months", 99, Pages));
    }

    [Fact]
    public void A_made_up_span_is_not_found()
    {
        Assert.Equal(SpanVerdict.NotFound, SpanVerifier.Verify("the Supplier shall indemnify the Customer without limit", 1, Pages));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public void An_empty_span_has_nothing_to_verify(string? span)
    {
        Assert.Equal(SpanVerdict.NoSpan, SpanVerifier.Verify(span, 1, Pages));
    }

    [Fact]
    public void Curly_quotes_dashes_and_ligatures_fold_to_the_same_text()
    {
        var pages = new[] { new DocumentPageText(1, "The Customer’s “official” fee – payable in eﬃcient instalments") };

        Assert.Equal(SpanVerdict.Found, SpanVerifier.Verify("The Customer's \"official\" fee - payable in efficient instalments", 1, pages));
    }

    [Fact]
    public void Diacritics_do_not_matter()
    {
        var pages = new[] { new DocumentPageText(1, "Il fornitore è tenuto al pagamento entro 30 giorni, perché così previsto.") };

        Assert.Equal(SpanVerdict.Found, SpanVerifier.Verify("Il fornitore e tenuto al pagamento entro 30 giorni, perche cosi previsto.", 1, pages));
        Assert.Equal(SpanVerdict.Found, SpanVerifier.Verify("fornitore è tenuto", 1, new[] { new DocumentPageText(1, "il fornitore e tenuto") }));
    }

    [Fact]
    public void Line_end_hyphenation_and_soft_hyphens_are_joined()
    {
        var pages = new[] { new DocumentPageText(1, "This agree-\nment shall termi­nate on the end date.") };

        Assert.Equal(SpanVerdict.Found, SpanVerifier.Verify("This agreement shall terminate on the end date.", 1, pages));
    }

    [Fact]
    public void A_span_straddling_a_page_break_is_found()
    {
        var pages = new[] { new DocumentPageText(1, "Payment is due within"), new DocumentPageText(2, "thirty days of invoice.") };

        Assert.Equal(SpanVerdict.Found, SpanVerifier.Verify("Payment is due within thirty days of invoice", 1, pages));
    }

    [Fact]
    public void A_fuzzy_span_above_the_threshold_is_found()
    {
        // One word wrong in about 60 characters: ratio well above 0.85.
        Assert.Equal(SpanVerdict.Found, SpanVerifier.Verify("Notice of termination: 90 days prior to the contract end date.", 2, Pages));
    }

    [Fact]
    public void A_fuzzy_span_below_the_threshold_is_not_found_and_the_threshold_is_configurable()
    {
        const string loose = "Notice of cancellation: 30 days before the expiry of the term";

        Assert.Equal(SpanVerdict.NotFound, SpanVerifier.Verify(loose, 2, Pages));
        Assert.Equal(SpanVerdict.Found, SpanVerifier.Verify(loose, 2, Pages, threshold: 0.5));
    }

    [Fact]
    public void A_short_span_must_match_exactly()
    {
        // 3 characters leave no error budget at 0.85.
        Assert.Equal(SpanVerdict.NotFound, SpanVerifier.Verify("xyz", 1, Pages));
    }

    [Fact]
    public void An_invalid_threshold_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpanVerifier(Pages, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpanVerifier(Pages, 1.01));
    }

    [Fact]
    public void A_very_long_span_is_compared_by_its_300_character_prefix()
    {
        var sentence = "The Supplier shall deliver the services described in Schedule A in accordance with the agreed service levels and report monthly. ";
        var pages = new[] { new DocumentPageText(1, string.Concat(Enumerable.Repeat(sentence, 6))) };
        var longSpan = string.Concat(Enumerable.Repeat(sentence, 6)).Replace("monthly", "weekly");

        Assert.Equal(SpanVerdict.Found, SpanVerifier.Verify(longSpan, 1, pages));
    }

    [Fact]
    public void Long_documents_are_checked_quickly()
    {
        var builder = new StringBuilder();
        var random = new Random(7);
        var words = new[] { "the", "supplier", "customer", "shall", "payment", "invoice", "term", "agreement", "notice", "service", "fee", "days" };
        while (builder.Length < 600_000)
        {
            builder.Append(words[random.Next(words.Length)]).Append(' ');
        }

        var pages = Enumerable.Range(1, 20)
            .Select(n => new DocumentPageText(n, builder.ToString(n * 1000, 30_000)))
            .ToList();
        var target = pages[11].Text.Substring(5000, 200);
        var typo = target.Remove(40, 1).Insert(90, "Q");
        var invented = string.Concat(Enumerable.Repeat("completely invented sentence about indemnity ", 6));

        var watch = Stopwatch.StartNew();
        var verifier = new SpanVerifier(pages);
        var found = verifier.Verify(target, 12);
        var fuzzy = verifier.Verify(typo, 12);
        var otherPage = verifier.Verify(target, 3);
        var missing = verifier.Verify(invented, 5);
        watch.Stop();

        Assert.Equal(SpanVerdict.Found, found);
        Assert.Equal(SpanVerdict.Found, fuzzy);
        Assert.NotEqual(SpanVerdict.NotFound, otherPage);
        Assert.Equal(SpanVerdict.NotFound, missing);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
    }
}
