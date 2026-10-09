using Raffa.Chat.Application.Guards;
using Raffa.Chat.Application.Pack;

namespace Raffa.Chat.Tests.Guards;

/// <summary>
/// LANG-01 / F1-T07 (F1-D03's locale matrix): <see cref="NumericGuard"/> and its parsers read amounts,
/// percentages and dates in the five supported languages (IT, EN, FR, ES, DE) and look for a quoted
/// figure only in the snippets of the items the answer cites. One corpus per language, each line
/// 100% recognised and grounded against the pack value; the shapes the old reader got wrong
/// (<c>667.000,00</c> read as 667) are the first rows.
/// </summary>
public sealed class NumericGuardLocaleTests
{
    private const string NarrowNbsp = " ";
    private const string Nbsp = " ";

    private static PackItem Item(string key, string snippet, params PackValue[] values) =>
        new(key, PackCorpus.Tenant, "Salesforce · MSA", null, null, null, snippet, null, null, null, "validated contract", values);

    private static PackItem Amount(decimal value, string currency = "EUR") =>
        Item("calc:amount", "Deterministic calculator output.", new PackValue("annualValue", value.ToString(System.Globalization.CultureInfo.InvariantCulture), PackValueKind.Amount, currency));

    private static PackItem Percent(string value) =>
        Item("calc:pct", "Deterministic calculator output.", new PackValue("uplift", value, PackValueKind.Percentage));

    private static PackItem DateItem(string iso) =>
        Item("calc:date", "Deterministic calculator output.", new PackValue("endDate", iso, PackValueKind.Date));

    // ---------------------------------------------------------------- amounts: one corpus per language

    public static TheoryData<string, string> AmountCorpus => new()
    {
        // Italian
        { "it", "Il canone è EUR 667.000,00 all'anno." },
        { "it", "Il canone è 667.000,00 EUR all'anno." },
        { "it", "Il canone è 667.000 euro all'anno." },
        { "it", "Il canone è EUR 667.000 all'anno." },
        { "it", "Il canone è € 667.000,00 all'anno." },
        { "it", "Il canone è 667.000,00 € all'anno." },
        { "it", "Il canone è €667.000 all'anno." },
        { "it", "Il canone è 667 mila euro all'anno." },
        { "it", "Il canone è circa EUR 0,667 milioni all'anno." },
        { "it", "Il canone è EUR 667000 all'anno." },
        // English
        { "en", "The fee is EUR 667,000.00 a year." },
        { "en", "The fee is EUR 667,000 a year." },
        { "en", "The fee is 667,000 euros a year." },
        { "en", "The fee is 667,000 EUR a year." },
        { "en", "The fee is €667,000 a year." },
        { "en", "The fee is EUR 667k a year." },
        { "en", "The fee is 667k EUR a year." },
        { "en", "The fee is EUR 0.667M a year." },
        { "en", "The fee is EUR 0.667 million a year." },
        { "en", "The fee is 667 thousand euros a year." },
        // French: narrow no-break space (U+202F), no-break space (U+00A0), plain space
        { "fr", $"Le tarif est de EUR 667{NarrowNbsp}000,00 par an." },
        { "fr", $"Le tarif est de EUR 667{Nbsp}000,00 par an." },
        { "fr", "Le tarif est de EUR 667 000,00 par an." },
        { "fr", $"Le tarif est de 667{NarrowNbsp}000 euros par an." },
        { "fr", $"Le tarif est de 667{NarrowNbsp}000,00{NarrowNbsp}€ par an." },
        { "fr", $"Le tarif est de 667{Nbsp}000,00{Nbsp}EUR par an." },
        { "fr", "Le tarif est de 667 000 € par an." },
        { "fr", "Le tarif est de 0,667 million EUR par an." },
        // Spanish
        { "es", "La tarifa es de EUR 667.000,00 al año." },
        { "es", "La tarifa es de 667.000,00 euros al año." },
        { "es", "La tarifa es de 667.000 € al año." },
        { "es", "La tarifa es de 667 mil euros al año." },
        { "es", "La tarifa es de 0,667 millones EUR al año." },
        // German
        { "de", "Die Gebühr beträgt EUR 667.000,00 pro Jahr." },
        { "de", "Die Gebühr beträgt 667.000,00 EUR pro Jahr." },
        { "de", "Die Gebühr beträgt 667.000 Euro pro Jahr." },
        { "de", "Die Gebühr beträgt 667.000 € pro Jahr." },
        { "de", "Die Gebühr beträgt 667 Tsd. EUR pro Jahr." },
        { "de", "Die Gebühr beträgt 0,667 Mio. EUR pro Jahr." },
        { "de", "Die Gebühr beträgt EUR 667.000 pro Jahr." },
    };

    [Theory]
    [MemberData(nameof(AmountCorpus))]
    public void An_amount_in_any_of_the_five_languages_is_recognised_and_grounded_with_and_without_the_language(
        string language, string text)
    {
        var money = Assert.Single(NumericTokenExtractor.Money(text, NumericLocale.ConventionFor(language)));
        Assert.Equal("EUR", money.Currency);
        Assert.InRange(money.Value, 667_000m - money.Tolerance, 667_000m + money.Tolerance);

        var pack = new[] { Amount(667_000m) };
        Assert.True(NumericGuard.Validate(text, pack, language: language).Passed, text);
        Assert.True(NumericGuard.Validate(text, pack).Passed, text);
    }

    [Theory]
    [MemberData(nameof(AmountCorpus))]
    public void A_different_amount_or_currency_in_the_same_format_is_still_refused(string language, string text)
    {
        // The recogniser is lenient about format, never about the value or the currency.
        Assert.False(NumericGuard.Validate(text, [Amount(66_700m)], language: language).Passed, text);
        Assert.False(NumericGuard.Validate(text, [Amount(667_000m, "CHF")], language: language).Passed, text);
    }

    [Fact]
    public void The_old_misreading_667_000_comma_00_as_667_is_gone()
    {
        Assert.True(LocaleNumberParser.TryParse("667.000,00", DecimalConvention.Unknown, false, out var value));
        Assert.Equal(667_000.00m, value);
        Assert.False(NumericGuard.Validate("EUR 667.000,00", [Amount(667m)]).Passed);
    }

    // ---------------------------------------------------------------- the number reader itself

    [Theory]
    [InlineData("1.234,56", DecimalConvention.Unknown, 1234.56)]
    [InlineData("1,234.56", DecimalConvention.Unknown, 1234.56)]
    [InlineData("1.234.567,89", DecimalConvention.Unknown, 1234567.89)]
    [InlineData("1,234,567.89", DecimalConvention.Unknown, 1234567.89)]
    [InlineData("1 234,56", DecimalConvention.Unknown, 1234.56)]
    [InlineData("1 234,56", DecimalConvention.Unknown, 1234.56)]
    [InlineData("1 234,56", DecimalConvention.Unknown, 1234.56)]
    [InlineData("1 234 567", DecimalConvention.Unknown, 1234567)]
    [InlineData("1'234.56", DecimalConvention.Unknown, 1234.56)]
    [InlineData("1’234.56", DecimalConvention.Unknown, 1234.56)]
    [InlineData("667,000", DecimalConvention.Unknown, 667000)]
    [InlineData("667.000", DecimalConvention.Unknown, 667000)]
    [InlineData("132,50", DecimalConvention.Unknown, 132.5)]
    [InlineData("132.50", DecimalConvention.Unknown, 132.5)]
    [InlineData("1,5", DecimalConvention.Unknown, 1.5)]
    [InlineData("0,500", DecimalConvention.Unknown, 0.5)]
    [InlineData("0.125", DecimalConvention.Unknown, 0.125)]
    [InlineData("1 234.567", DecimalConvention.Unknown, 1234.567)]
    [InlineData("1.250", DecimalConvention.Point, 1.25)]
    [InlineData("1,250", DecimalConvention.Point, 1250)]
    [InlineData("1.250", DecimalConvention.Comma, 1250)]
    [InlineData("1,250", DecimalConvention.Comma, 1.25)]
    [InlineData("42", DecimalConvention.Unknown, 42)]
    public void The_reader_resolves_each_separator_style(string text, DecimalConvention convention, double expected)
    {
        Assert.True(LocaleNumberParser.TryParse(text, convention, preferDecimal: false, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    [InlineData("1,23,456")]
    [InlineData("1.234,56,7")]
    [InlineData(",5")]
    [InlineData("5,")]
    public void A_malformed_number_is_refused_not_guessed(string text)
    {
        Assert.False(LocaleNumberParser.TryParse(text, DecimalConvention.Unknown, false, out _));
    }

    [Theory]
    [InlineData("it", DecimalConvention.Comma)]
    [InlineData("fr", DecimalConvention.Comma)]
    [InlineData("es-ES", DecimalConvention.Comma)]
    [InlineData("de_DE", DecimalConvention.Comma)]
    [InlineData("en", DecimalConvention.Point)]
    [InlineData("EN-gb", DecimalConvention.Point)]
    [InlineData(null, DecimalConvention.Unknown)]
    [InlineData("ja", DecimalConvention.Unknown)]
    public void The_locale_matrix_maps_each_language_to_its_decimal_convention(string? language, DecimalConvention expected)
    {
        Assert.Equal(expected, NumericLocale.ConventionFor(language));
    }

    [Fact]
    public void The_ambiguous_single_separator_follows_the_language_when_it_is_known()
    {
        // 1.250 euros: one thousand two hundred fifty in IT/ES/DE/FR, one and a quarter in EN.
        Assert.True(NumericGuard.Validate("EUR 1.250", [Amount(1250m)], language: "it").Passed);
        Assert.False(NumericGuard.Validate("EUR 1.250", [Amount(1.25m)], language: "it").Passed);
        Assert.True(NumericGuard.Validate("EUR 1.250", [Amount(1.25m)], language: "en").Passed);
        Assert.False(NumericGuard.Validate("EUR 1.250", [Amount(1250m)], language: "en").Passed);
        // No language: read as thousands, the way contracts mean it.
        Assert.True(NumericGuard.Validate("EUR 1.250", [Amount(1250m)]).Passed);
    }

    // ---------------------------------------------------------------- shorthand

    [Theory]
    [InlineData("EUR 40k", "40000")]
    [InlineData("40k EUR", "40000")]
    [InlineData("EUR 40K", "40000")]
    [InlineData("40 K€", "40000")]
    [InlineData("EUR 1,5M", "1500000")]
    [InlineData("EUR 1.5M", "1500000")]
    [InlineData("1,5M €", "1500000")]
    [InlineData("EUR 1,5 Mio", "1500000")]
    [InlineData("1,5 Mio. EUR", "1500000")]
    [InlineData("1.5 million euros", "1500000")]
    [InlineData("1,5 millions EUR", "1500000")]
    [InlineData("EUR 2 Mrd", "2000000000")]
    [InlineData("EUR 2 bn", "2000000000")]
    public void A_shorthand_amount_is_expanded_and_grounded(string text, string packValue)
    {
        var pack = new[] { Item("calc:a", "x", new PackValue("v", packValue, PackValueKind.Amount, "EUR")) };

        Assert.True(NumericGuard.Validate(text, pack).Passed, text);
    }

    [Fact]
    public void A_shorthand_is_a_rounding_of_the_pack_value_never_a_different_figure()
    {
        // "40k" shows thousands: 40,250 rounds to it, 41,000 does not.
        Assert.True(NumericGuard.Validate("EUR 40k", [Amount(40_250m)]).Passed);
        Assert.False(NumericGuard.Validate("EUR 40k", [Amount(41_000m)]).Passed);
        Assert.True(NumericGuard.Validate("EUR 1,5M", [Amount(1_540_000m)]).Passed);
        Assert.False(NumericGuard.Validate("EUR 1,5M", [Amount(1_700_000m)]).Passed);
        // An exact figure keeps the cent tolerance.
        Assert.False(NumericGuard.Validate("EUR 40.000", [Amount(40_250m)]).Passed);
    }

    // ---------------------------------------------------------------- percentages

    [Theory]
    [InlineData("L'aumento è del 12,5%.")]
    [InlineData("The uplift is 12.5%.")]
    [InlineData("L'augmentation est de 12,5 %.")]
    [InlineData("L'augmentation est de 12,5 %.")]
    [InlineData("L'augmentation est de 12,5 %.")]
    [InlineData("La subida es del 12,5 por ciento.")]
    [InlineData("Die Erhöhung beträgt 12,5 Prozent.")]
    [InlineData("L'augmentation est de 12,5 pour cent.")]
    [InlineData("L'aumento è del 12,5 per cento.")]
    [InlineData("The uplift is 12.5 percent.")]
    [InlineData("The uplift is 12.5 per cent.")]
    public void A_percentage_in_any_of_the_five_languages_is_recognised_and_grounded(string text)
    {
        Assert.Single(NumericTokenExtractor.Percentages(text));
        Assert.True(NumericGuard.Validate(text, [Percent("12.5")]).Passed, text);
        Assert.False(NumericGuard.Validate(text, [Percent("12")]).Passed, text);
    }

    [Fact]
    public void A_percentage_with_three_decimals_is_a_decimal_not_a_thousand()
    {
        Assert.True(NumericGuard.Validate("Lo spread è 0,125%.", [Percent("0.125")]).Passed);
        Assert.True(NumericGuard.Validate("The rate is 1.250%.", [Percent("1.25")]).Passed);
    }

    // ---------------------------------------------------------------- dates

    [Theory]
    [InlineData("2027-01-15")]
    [InlineData("15 January 2027")]
    [InlineData("15th January 2027")]
    [InlineData("January 15, 2027")]
    [InlineData("Jan 15 2027")]
    [InlineData("15 gennaio 2027")]
    [InlineData("15 gen 2027")]
    [InlineData("1er janvier 2027")]
    [InlineData("15 janvier 2027")]
    [InlineData("15 de enero de 2027")]
    [InlineData("15 enero 2027")]
    [InlineData("15. Januar 2027")]
    [InlineData("15 Januar 2027")]
    [InlineData("15/01/2027")]
    [InlineData("15.01.2027")]
    [InlineData("15-01-2027")]
    [InlineData("01/15/2027")]
    public void A_date_in_any_of_the_five_languages_is_recognised_and_grounded(string written)
    {
        var date = written.StartsWith("1er", StringComparison.Ordinal) ? "2027-01-01" : "2027-01-15";
        var text = $"La scadenza è il {written}.";

        Assert.Single(NumericTokenExtractor.Dates(text));
        Assert.True(NumericGuard.Validate(text, [DateItem(date)]).Passed, text);
        Assert.False(NumericGuard.Validate(text, [DateItem("2027-02-15")]).Passed, text);
    }

    [Theory]
    [InlineData("5. März 2027")]
    [InlineData("5 marzo 2027")]
    [InlineData("5 mars 2027")]
    [InlineData("5 de marzo de 2027")]
    [InlineData("March 5, 2027")]
    [InlineData("5 mar 2027")]
    [InlineData("05/03/2027")]
    public void The_same_date_reads_the_same_in_every_language(string written)
    {
        Assert.True(NumericGuard.Validate($"Ends {written}.", [DateItem("2027-03-05")]).Passed, written);
    }

    [Fact]
    public void A_numeric_date_follows_the_language_for_the_day_month_order()
    {
        // 03/04/2027: 3 April in IT/FR/ES/DE, March 4 in EN; no language accepts either reading.
        Assert.True(NumericGuard.Validate("Il 03/04/2027.", [DateItem("2027-04-03")], language: "it").Passed);
        Assert.False(NumericGuard.Validate("Il 03/04/2027.", [DateItem("2027-03-04")], language: "it").Passed);
        Assert.True(NumericGuard.Validate("On 03/04/2027.", [DateItem("2027-03-04")], language: "en").Passed);
        Assert.True(NumericGuard.Validate("On 03/04/2027.", [DateItem("2027-04-03")]).Passed);
        Assert.True(NumericGuard.Validate("On 03/04/2027.", [DateItem("2027-03-04")]).Passed);
        Assert.False(NumericGuard.Validate("On 03/04/2027.", [DateItem("2027-05-03")]).Passed);
    }

    [Fact]
    public void Text_that_only_looks_like_a_date_is_skipped_not_refused()
    {
        Assert.True(NumericGuard.Validate("See 15 foo 2027 and 31/02/2027.", []).Passed);
    }

    // ---------------------------------------------------------------- snippets: only the cited items

    private static PackItem Clause(string key, string snippet) => Item(key, snippet);

    [Fact]
    public void A_figure_quoted_from_the_snippet_of_an_uncited_item_is_refused()
    {
        var pack = new[]
        {
            Clause("tenant:a", "The renewal fee is EUR 12,000."),
            Clause("tenant:b", "The liability cap is EUR 1,000,000."),
        };
        const string answer = "The liability cap is EUR 1,000,000 [1].";

        // Cites only item a: the cap lives in item b, which the answer does not cite.
        Assert.False(NumericGuard.Validate(answer, pack, citedKeys: ["tenant:a"]).Passed);
        Assert.True(NumericGuard.Validate(answer, pack, citedKeys: ["tenant:b"]).Passed);
        Assert.True(NumericGuard.Validate(answer, pack, citedKeys: ["tenant:a", "tenant:b"]).Passed);
    }

    [Fact]
    public void Without_citations_the_whole_pack_is_still_searched_so_nothing_that_passed_before_breaks()
    {
        var pack = new[] { Clause("tenant:b", "The liability cap is EUR 1,000,000.") };
        const string answer = "The liability cap is EUR 1,000,000.";

        Assert.True(NumericGuard.Validate(answer, pack).Passed);
        Assert.True(NumericGuard.Validate(answer, pack, citedKeys: []).Passed);
        Assert.True(NumericGuard.Validate(answer, pack, citedKeys: ["not-in-the-pack"]).Passed);
    }

    [Fact]
    public void A_structured_value_of_any_pack_item_still_grounds_a_figure_whatever_is_cited()
    {
        // Calculator outputs are not snippets: a Values entry grounds the figure pack-wide.
        var pack = new[] { Amount(667_000m), Clause("tenant:a", "Some clause.") };

        Assert.True(NumericGuard.Validate("EUR 667.000,00 [1]", pack, citedKeys: ["tenant:a"]).Passed);
    }

    [Fact]
    public void A_cited_snippet_grounds_the_same_figure_written_in_another_format()
    {
        var pack = new[] { Clause("tenant:a", "The annual fee is EUR 667,000.00 payable in advance.") };

        Assert.True(NumericGuard.Validate("Il canone è EUR 667.000,00 [1].", pack, citedKeys: ["tenant:a"]).Passed);
        Assert.True(NumericGuard.Validate("Le tarif est de 667 000 euros [1].", pack, citedKeys: ["tenant:a"]).Passed);
        Assert.False(NumericGuard.Validate("Il canone è EUR 667.001,00 [1].", pack, citedKeys: ["tenant:a"]).Passed);
        // Currency-aware inside snippets too.
        Assert.False(NumericGuard.Validate("The fee is CHF 667,000 [1].", pack, citedKeys: ["tenant:a"]).Passed);
    }

    [Fact]
    public void A_cited_snippet_grounds_a_percentage_and_a_date_in_another_format()
    {
        var pack = new[] { Clause("tenant:a", "Uplift capped at 7,5 % and the term ends on 15 January 2027.") };

        Assert.True(NumericGuard.Validate("Cap: 7.5%. Ends 2027-01-15 [1].", pack, citedKeys: ["tenant:a"]).Passed);
        Assert.True(NumericGuard.Validate("Cap: 7.5%. Ends 15/01/2027 [1].", pack, citedKeys: ["tenant:a"]).Passed);
        Assert.False(NumericGuard.Validate("Cap: 8%. [1]", pack, citedKeys: ["tenant:a"]).Passed);
        Assert.False(NumericGuard.Validate("Ends 2027-01-16 [1].", pack, citedKeys: ["tenant:a"]).Passed);
    }

    [Fact]
    public void A_figure_split_across_a_line_break_in_a_snippet_is_not_a_quote()
    {
        // Kept from GroundedFallbackAnswer's contract: "CHF\n450000" is not the amount "CHF 450000".
        var pack = new[] { Clause("tenant:a", "The liability cap is CHF\n450000 under this agreement.") };

        Assert.False(NumericGuard.Validate("CHF 450000 [1]", pack, citedKeys: ["tenant:a"]).Passed);
    }

    [Fact]
    public void Plain_numbers_without_a_currency_are_still_not_checked()
    {
        Assert.True(NumericGuard.Validate("See page 667.000 and clause 12.5.", []).Passed);
    }
}
