using Raffa.AiFlows.WebResearch.Guards;
using Raffa.AiGateway.Contracts;

namespace Raffa.AiFlows.Tests.WebResearch.Guards;

/// <summary>
/// F3-T01 / F3-D02 — the web figure check: a figure shares its sentence with a marker and is found in
/// the verbatim quote of a cited source; three states; a sentence with a rejected figure is removed;
/// the grammar (symbols, words, k/M, US dates, month-year, spelled percentages, ranges) in the five
/// languages. The sources here have an empty snippet, exactly as production's: only the quote carries text.
/// </summary>
public sealed class WebFigureGuardTests
{
    private static AiWebSource Source(string quote, string title = "Page", int n = 1) =>
        new($"https://example.com/{n}", title, Snippet: string.Empty, Quote: quote);

    private static AiWebSource NoQuote(int n = 1) => Source(string.Empty, n: n);

    private static WebFigureReport Verify(string summary, string? language, params AiWebSource[] sources) =>
        WebFigureGuard.Verify(summary, sources, language);

    // ---- The three states ------------------------------------------------------------------------

    [Fact]
    public void A_figure_in_the_quote_of_the_source_its_sentence_cites_is_verified()
    {
        var report = Verify(
            "Public, unverified information. Caps of 5-10% are common [1].", "en",
            Source("Most renewals close with a 5-10% uplift cap."));

        Assert.Equal(0, report.SentencesRemoved);
        Assert.Contains(report.Findings, f => f.Raw == "10%" && f.State == WebFigureState.Verified);
        Assert.All(report.Findings, f => Assert.Equal(WebFigureState.Verified, f.State));
        Assert.Equal("Public, unverified information. Caps of 5-10% are common [1].", report.Markdown);
        Assert.True(report.HasCitedClaim);
    }

    [Fact]
    public void A_figure_the_cited_quote_does_not_carry_is_rejected_and_its_sentence_removed()
    {
        var report = Verify(
            "Public, unverified information. Caps of 25% are common [1]. Multi-year deals help [1].", "en",
            Source("Most renewals close with a 5-10% uplift cap."));

        Assert.Equal(1, report.SentencesRemoved);
        var finding = Assert.Single(report.Findings);
        Assert.Equal(WebFigureState.Rejected, finding.State);
        Assert.Equal("Public, unverified information. Multi-year deals help [1].", report.Markdown);
        Assert.Contains("25%", report.FirstRemovalReason, StringComparison.Ordinal);
        Assert.True(report.HasCitedClaim);
    }

    [Fact]
    public void A_figure_without_a_marker_in_its_own_sentence_is_rejected_even_when_another_source_has_it()
    {
        var report = Verify(
            "Public, unverified information. Caps of 5-10% are common. Multi-year deals help [1].", "en",
            Source("Most renewals close with a 5-10% uplift cap."));

        Assert.Equal(1, report.SentencesRemoved);
        Assert.Contains(report.Findings, f => f.State == WebFigureState.Rejected && f.Reason.Contains("no [n] marker", StringComparison.Ordinal));
        Assert.DoesNotContain("5-10%", report.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void A_figure_is_checked_against_the_source_it_cites_not_against_any_source()
    {
        // The 7% is in source 2's quote but the sentence cites source 1.
        var report = Verify(
            "Public, unverified information. Notice is 60 days and uplift 7% [1].", "en",
            Source("Notice is typically 60 days."),
            Source("Uplift of 7% is the median.", n: 2));

        Assert.Equal(1, report.SentencesRemoved);
        Assert.False(report.HasCitedClaim);
    }

    [Fact]
    public void A_union_of_the_markers_of_the_sentence_is_the_evidence()
    {
        var report = Verify(
            "Public, unverified information. Notice is 60 days [1] and uplift is capped at 7% [2].", "en",
            Source("Notice is typically 60 days."),
            Source("Uplift of 7% is the median.", n: 2));

        Assert.Equal(0, report.SentencesRemoved);
        Assert.Equal(1, report.Findings.Count(f => f.Raw == "7%" && f.State == WebFigureState.Verified));
    }

    [Fact]
    public void An_explicit_figure_with_no_quote_anywhere_is_reported_and_kept_only_when_allowed()
    {
        const string summary = "Public, unverified information. Caps of 25% are common [1].";

        var strict = WebFigureGuard.Verify(summary, [NoQuote()], "en", allowReported: false);
        var lenient = WebFigureGuard.Verify(summary, [NoQuote()], "en", allowReported: true);

        Assert.Equal(WebFigureState.Reported, Assert.Single(strict.Findings).State);
        Assert.Equal(1, strict.SentencesRemoved);
        Assert.Equal(WebFigureState.Reported, Assert.Single(lenient.Findings).State);
        Assert.Equal(0, lenient.SentencesRemoved);
        Assert.Equal(1, lenient.Reported);
        Assert.Equal(summary, lenient.Markdown);
    }

    [Theory]
    [InlineData("The list price is $36 per user [1].", "$36")]
    [InlineData("The list price is €36 per user [1].", "€36")]
    [InlineData("The list price is 36 euros per user [1].", "36 euros")]
    [InlineData("The list price is 36 dollars per user [1].", "36 dollars")]
    [InlineData("Contracts average 1,5M EUR [1].", "1,5M EUR")]
    public void A_symbol_word_or_shorthand_figure_with_no_quote_is_rejected_even_when_reported_figures_are_allowed(string sentence, string figure)
    {
        var report = WebFigureGuard.Verify("Public, unverified information. " + sentence, [NoQuote()], "en", allowReported: true);

        var finding = Assert.Single(report.Findings, f => f.Raw.Contains(figure.Split(' ')[0], StringComparison.Ordinal));
        if (figure.Contains("EUR", StringComparison.Ordinal))
        {
            // An ISO-coded amount is explicit: reported, kept, flagged.
            Assert.Equal(WebFigureState.Reported, finding.State);
        }
        else
        {
            Assert.Equal(WebFigureState.Rejected, finding.State);
            Assert.Equal(1, report.SentencesRemoved);
            Assert.DoesNotContain(figure, report.Markdown, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_summary_without_a_figure_is_untouched_whatever_the_quotes()
    {
        const string summary = "Public, unverified information. Buyers often trade term for price [1]. No marker here either.";

        var report = Verify(summary, "en", NoQuote());

        Assert.Equal(summary, report.Markdown);
        Assert.Empty(report.Findings);
        Assert.Equal(0, report.SentencesRemoved);
    }

    // ---- Symbols, words, shorthand: the figures the old grammar let through --------------------

    [Theory]
    [InlineData("The Pro plan costs $36 per user [1].", "Pro costs $36 per user per month.", true)]
    [InlineData("The Pro plan costs $36 per user [1].", "Pro costs USD 36 per user per month.", true)]
    [InlineData("The Pro plan costs $36 per user [1].", "Pro costs 36 dollars per user per month.", true)]
    [InlineData("The Pro plan costs $36 per user [1].", "Pro costs €36 per user per month.", false)]
    [InlineData("The Pro plan costs $36 per user [1].", "Pro costs $360 per user per year.", false)]
    [InlineData("The Pro plan costs $36 per user [1].", "Pro costs $36.50 per user per month.", false)]
    [InlineData("Seats cost €12 each [1].", "Each seat costs 12 euros.", true)]
    [InlineData("Seats cost €12 each [1].", "Each seat costs 12 EUR.", true)]
    [InlineData("Seats cost 12 euro each [1].", "Each seat costs €12.", true)]
    [InlineData("Seats cost EUR 12 each [1].", "Each seat costs 12 USD.", false)]
    [InlineData("Seats cost £9 each [1].", "Seats are priced at GBP 9.", true)]
    [InlineData("Seats cost 15 euros each [1].", "Seats are priced at 1.500 euros.", false)]
    public void Symbol_and_word_amounts_are_checked_against_the_quote_with_the_same_currency(
        string sentence, string quote, bool expectedVerified)
    {
        var report = Verify("Public, unverified information. " + sentence, "en", Source(quote));

        var amount = report.Findings.First(f => f.Kind == WebFigureKind.Amount);
        Assert.Equal(expectedVerified ? WebFigureState.Verified : WebFigureState.Rejected, amount.State);
        Assert.Equal(expectedVerified ? 0 : 1, report.SentencesRemoved);
    }

    [Theory]
    [InlineData("Savings reach EUR 40k a year [1].", "Customers save EUR 40,000 per year.", true)]
    [InlineData("Savings reach EUR 40k a year [1].", "Customers save EUR 41,000 per year.", false)]
    [InlineData("Contracts average EUR 1,5M [1].", "The average contract is worth EUR 1.5 million.", true)]
    [InlineData("Contracts average EUR 1,5M [1].", "The average contract is worth EUR 2.5 million.", false)]
    [InlineData("Contracts average 2 Mio. EUR [1].", "Der Durchschnitt liegt bei 2.000.000 EUR.", true)]
    [InlineData("Contracts average 3 millions EUR [1].", "La valeur moyenne est de 3 000 000 EUR.", true)]
    public void Magnitude_shorthand_is_expanded_before_it_is_compared(string sentence, string quote, bool expectedVerified)
    {
        var report = Verify("Public, unverified information. " + sentence, "en", Source(quote));

        Assert.Equal(expectedVerified ? 0 : 1, report.SentencesRemoved);
        Assert.Contains(report.Findings, f => f.Kind == WebFigureKind.Amount && (f.State == WebFigureState.Verified) == expectedVerified);
    }

    // ---- Percentages: ranges, words, locale --------------------------------------------------------

    [Theory]
    [InlineData("Caps of 5% to 10% are common [1].", "Uplift caps run 5-10% in most deals.", true)]
    [InlineData("Caps of 5-10% are common [1].", "Uplift caps run from 5 to 10 percent.", true)]
    [InlineData("Caps of 5-10% are common [1].", "Uplift caps run up to 10%.", false)]
    [InlineData("Caps of 3-10% are common [1].", "Uplift caps run 5-10% in most deals.", false)]
    [InlineData("Caps of 5% are common [1].", "Uplift caps run 15% in some deals.", false)]
    [InlineData("Caps of 5,5% are common [1].", "Uplift caps average 5.5% in most deals.", true)]
    [InlineData("Caps of 5,5 % are common [1].", "Uplift caps average 5,5 % in most deals.", true)]
    [InlineData("Caps of 12 per cento are common [1].", "Uplift caps average 12% in most deals.", true)]
    [InlineData("Caps of 12 Prozent sind üblich [1].", "Uplift caps average 12% in most deals.", true)]
    public void Percentages_in_ranges_and_other_notations_are_both_bounds_checked(string sentence, string quote, bool expectedVerified)
    {
        var report = Verify("Public, unverified information. " + sentence, "en", Source(quote));

        Assert.Equal(expectedVerified ? 0 : 1, report.SentencesRemoved);
    }

    [Theory]
    [InlineData("Caps of ten percent are common [1].", "Uplift caps are 10% in most deals.", true)]
    [InlineData("Caps of ten percent are common [1].", "Uplift caps are 12% in most deals.", false)]
    [InlineData("Caps of twenty-five percent are common [1].", "Uplift caps are 25% in some deals.", true)]
    [InlineData("Caps of twenty-five percent are common [1].", "Uplift caps are 15% in some deals.", false)]
    [InlineData("Caps of one hundred and fifty percent are common [1].", "Uplift caps reach 150% in some deals.", true)]
    [InlineData("I cap sono del dieci per cento [1].", "Uplift caps are 10% in most deals.", true)]
    [InlineData("I cap sono del venticinque per cento [1].", "Uplift caps are 25% in most deals.", true)]
    [InlineData("Les plafonds sont de vingt-cinq pour cent [1].", "Uplift caps are 25% in most deals.", true)]
    [InlineData("Les plafonds sont de quatre-vingt-dix pour cent [1].", "Uplift caps are 90% in most deals.", true)]
    [InlineData("Los límites son del treinta y cinco por ciento [1].", "Uplift caps are 35% in most deals.", true)]
    [InlineData("Die Obergrenzen liegen bei fünfundzwanzig Prozent [1].", "Uplift caps are 25% in most deals.", true)]
    [InlineData("Die Obergrenzen liegen bei fünfundzwanzig Prozent [1].", "Uplift caps are 52% in most deals.", false)]
    public void A_percentage_spelled_in_words_is_read_in_the_five_languages_and_checked_like_digits(
        string sentence, string quote, bool expectedVerified)
    {
        var report = Verify("Public, unverified information. " + sentence, null, Source(quote));

        Assert.Contains(report.Findings, f => f.Kind == WebFigureKind.Percentage);
        Assert.Equal(expectedVerified ? 0 : 1, report.SentencesRemoved);
    }

    [Fact]
    public void Between_two_spelled_numbers_the_and_does_not_add_them_up()
    {
        var report = Verify(
            "Public, unverified information. Caps run between five and ten percent [1].", "en",
            Source("Caps run between 5 and 10 percent."));

        var finding = Assert.Single(report.Findings);
        Assert.Equal("ten percent", finding.Raw);
        Assert.Equal(WebFigureState.Verified, finding.State);
    }

    // ---- Dates ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("The increase starts on 1 August 2025 [1].", "Effective August 1, 2025, prices rise.", "en", true)]
    [InlineData("The increase starts on August 1, 2025 [1].", "Prices rise on 1 August 2025.", "en", true)]
    [InlineData("The increase starts on 1 agosto 2025 [1].", "Effective August 1, 2025, prices rise.", "it", true)]
    [InlineData("L'augmentation commence le 1er août 2025 [1].", "Effective August 1, 2025, prices rise.", "fr", true)]
    [InlineData("El aumento empieza el 1 de agosto de 2025 [1].", "Effective August 1, 2025, prices rise.", "es", true)]
    [InlineData("Die Erhöhung beginnt am 1. August 2025 [1].", "Effective August 1, 2025, prices rise.", "de", true)]
    [InlineData("The increase starts on 2025-08-01 [1].", "Effective August 1, 2025, prices rise.", "en", true)]
    [InlineData("The increase starts on 8/1/2025 [1].", "Effective August 1, 2025, prices rise.", null, true)]
    [InlineData("L'aumento parte dal 01/08/2025 [1].", "Effective August 1, 2025, prices rise.", "it", true)]
    [InlineData("The increase starts on 2 August 2025 [1].", "Effective August 1, 2025, prices rise.", "en", false)]
    [InlineData("The increase starts on 1 August 2026 [1].", "Effective August 1, 2025, prices rise.", "en", false)]
    public void Dates_in_any_of_the_five_languages_and_the_us_order_match_the_same_calendar_day(
        string sentence, string quote, string? language, bool expectedVerified)
    {
        var report = Verify("Public, unverified information. " + sentence, language, Source(quote));

        Assert.Equal(expectedVerified ? 0 : 1, report.SentencesRemoved);
    }

    [Theory]
    [InlineData("Prices change in August 2025 [1].", "Effective August 1, 2025, prices rise.", true)]
    [InlineData("Prices change in August 2025 [1].", "Prices rise in August 2025.", true)]
    [InlineData("Prices change in agosto 2025 [1].", "Prices rise in August 2025.", true)]
    [InlineData("Les prix changent en août 2025 [1].", "Prices rise in August 2025.", true)]
    [InlineData("Los precios cambian en agosto de 2025 [1].", "Prices rise in August 2025.", true)]
    [InlineData("Die Preise ändern sich im August 2025 [1].", "Prices rise in August 2025.", true)]
    [InlineData("Prices change in September 2025 [1].", "Prices rise in August 2025.", false)]
    [InlineData("Prices change in August 2026 [1].", "Prices rise in August 2025.", false)]
    public void A_month_and_year_must_be_in_the_quote_as_a_month_and_year_or_a_date_in_that_month(
        string sentence, string quote, bool expectedVerified)
    {
        var report = Verify("Public, unverified information. " + sentence, null, Source(quote));

        Assert.Contains(report.Findings, f => f.Kind == WebFigureKind.MonthYear);
        Assert.Equal(expectedVerified ? 0 : 1, report.SentencesRemoved);
    }

    [Fact]
    public void A_full_date_is_not_also_read_as_a_month_and_year()
    {
        var report = Verify(
            "Public, unverified information. The increase starts on 1 August 2025 [1].", "en",
            Source("Effective 1 August 2025."));

        var finding = Assert.Single(report.Findings);
        Assert.Equal(WebFigureKind.Date, finding.Kind);
    }

    // ---- Locale ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Il canone è di EUR 667.000,00 [1].", "it", "The annual fee is €667,000.00.")]
    [InlineData("The fee is EUR 667,000.00 [1].", "en", "Il canone annuo è di 667.000,00 euro.")]
    [InlineData("Le tarif est de 667 000,00 EUR [1].", "fr", "The annual fee is EUR 667,000.")]
    [InlineData("La tarifa es de EUR 667.000 [1].", "es", "The annual fee is 667,000 EUR.")]
    [InlineData("Die Gebühr beträgt 667.000,00 EUR [1].", "de", "The annual fee is USD 1 but EUR 667,000.00 in Europe.")]
    public void Amounts_are_compared_by_value_across_the_decimal_conventions(string sentence, string language, string quote)
    {
        var report = Verify("Public, unverified information. " + sentence, language, Source(quote));

        Assert.Equal(0, report.SentencesRemoved);
        Assert.Contains(report.Findings, f => f.Kind == WebFigureKind.Amount && f.State == WebFigureState.Verified);
    }

    [Fact]
    public void A_percentage_is_not_verified_by_a_longer_number_that_contains_it()
    {
        var report = Verify(
            "Public, unverified information. Caps of 5% are common [1].", "en",
            Source("Caps of 15% were seen in 2019."));

        Assert.Equal(1, report.SentencesRemoved);
    }

    // ---- Sentences and markdown ---------------------------------------------------------------------

    [Fact]
    public void A_marker_after_the_full_stop_belongs_to_the_sentence_before_it()
    {
        var report = Verify(
            "Public, unverified information. Caps of 5-10% are common. [1]", "en",
            Source("Most renewals close with a 5-10% uplift cap."));

        Assert.Equal(0, report.SentencesRemoved);
    }

    [Fact]
    public void A_decimal_point_an_abbreviation_and_a_domain_do_not_end_a_sentence()
    {
        var report = Verify(
            "Public, unverified information. Per example.com, uplift averages 5.5% in approx. 80 deals [1].", "en",
            Source("Uplift averages 5.5% across deals."));

        Assert.Equal(0, report.SentencesRemoved);
        Assert.Contains(report.Findings, f => f.Raw == "5.5%" && f.State == WebFigureState.Verified);
    }

    [Fact]
    public void A_german_day_ordinal_and_a_mio_abbreviation_do_not_end_a_sentence()
    {
        var date = Verify(
            "Die Preise steigen am 1. August 2025 [1].", "de", Source("Effective August 1, 2025."));
        var shorthand = Verify(
            "Der Schnitt liegt bei 2,5 Mio. EUR [1].", "de", Source("Der Schnitt liegt bei 2.500.000 EUR."));

        var dateFinding = Assert.Single(date.Findings);
        Assert.Equal(WebFigureKind.Date, dateFinding.Kind);
        Assert.Equal(WebFigureState.Verified, dateFinding.State);
        Assert.Equal(WebFigureState.Verified, Assert.Single(shorthand.Findings).State);
    }

    [Fact]
    public void A_list_item_whose_only_sentence_is_removed_leaves_no_empty_bullet()
    {
        var summary =
            "Public, unverified information.\n" +
            "- Caps run 5-10% [1].\n" +
            "- Some deals reach 40% [1].\n" +
            "- Multi-year terms help [1].";

        var report = Verify(summary, "en", Source("Caps run 5-10% in most deals."));

        Assert.Equal(1, report.SentencesRemoved);
        Assert.Equal(
            "Public, unverified information.\n- Caps run 5-10% [1].\n- Multi-year terms help [1].",
            report.Markdown);
    }

    [Fact]
    public void A_paragraph_keeps_its_other_sentences_when_one_is_removed()
    {
        var report = Verify(
            "Public, unverified information. Some deals reach 40% [1]. Multi-year terms help [1].", "en",
            Source("Caps run 5-10% in most deals."));

        Assert.Equal("Public, unverified information. Multi-year terms help [1].", report.Markdown);
    }

    [Fact]
    public void When_every_cited_sentence_is_removed_the_report_says_no_claim_is_left()
    {
        var report = Verify(
            "Public, unverified information. Some deals reach 40% [1].", "en",
            Source("Caps run 5-10% in most deals."));

        Assert.False(report.HasCitedClaim);
        Assert.Equal("Public, unverified information.", report.Markdown);
    }

    [Fact]
    public void Markers_outside_the_source_list_never_count_as_evidence()
    {
        var report = Verify(
            "Public, unverified information. Caps run 5-10% [4].", "en",
            Source("Caps run 5-10% in most deals."));

        Assert.Equal(1, report.SentencesRemoved);
    }

    [Fact]
    public void The_page_title_and_a_provider_snippet_also_ground_a_figure()
    {
        var withSnippet = new AiWebSource("https://example.com/a", "Page", "Caps run 5-10% in most deals.");
        var withTitle = new AiWebSource("https://example.com/b", "Salesforce raises list prices 9% in August 2025", string.Empty);

        var snippetReport = Verify("Public, unverified information. Caps run 5-10% [1].", "en", withSnippet);
        var titleReport = Verify("Public, unverified information. Salesforce raised prices 9% [1].", "en", withTitle);

        Assert.Equal(0, snippetReport.SentencesRemoved);
        Assert.Equal(0, titleReport.SentencesRemoved);
    }
}
