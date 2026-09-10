using Raffa.Chat.Application.Guards;
using Raffa.Chat.Application.Pack;

namespace Raffa.Chat.Tests.Guards;

/// <summary>
/// Proves task E13/F06/US01/T01's numeric guard (ask-engine coding objective point 5;
/// `inputs/requirements.md` R-ASK-06 point 2; Appendix C rule 10): every currency amount,
/// percentage and date in an answer's markdown must equal a pack value, normalized and
/// currency-aware — otherwise <see cref="NumericGuard.Validate"/> fails so the caller
/// (<c>Answering.AnswerComposer</c>) can hand the violation to <see cref="RegenerateOnce"/>.
/// Includes this task's own Definition of Done scenario: a fake "P50 CHF 140" against a pack whose
/// real value is CHF 132 fails the guard and, chained through <see cref="RegenerateOnce.DowngradeToAbstain"/>,
/// downgrades to an honest abstain naming the pack's own real figure rather than the fabricated one.
/// </summary>
public sealed class NumericGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Raffa.AiGateway.Contracts.AiCallMetadata Metadata =
        new("fixture-answer-model", "v1", "answer-v2.1", Now, "deadbeef");

    private static PackItem MarketDeal(string citationKey, string snippet, params PackValue[] values) =>
        new(
            citationKey,
            PackCorpus.Market,
            "Category · Market feed",
            "Representative market data · mock feed",
            Page: null,
            Section: null,
            Snippet: snippet,
            Href: null,
            PreviewUrl: null,
            RecordId: "record-1",
            Provenance: "representative market data · mock feed · updated 2026-09-01",
            Values: values);

    [Fact]
    public void A_fake_p50_amount_fails_against_the_packs_real_value_and_downgrades_to_an_honest_abstain()
    {
        // This task's own Definition of Done: "numeric guard downgrades a fake 'P50 CHF 140' vs
        // pack 132".
        var pack = new[]
        {
            MarketDeal(
                "market:deal-1",
                "Market P50 unit price for this category is CHF 132.",
                new PackValue("unitPriceP50", "132", PackValueKind.Amount, "CHF")),
        };

        var verdict = NumericGuard.Validate("The market P50 for this category is CHF 140 per unit.", pack);

        Assert.False(verdict.Passed);
        Assert.NotNull(verdict.Violation);
        Assert.Contains("140", verdict.Violation);

        var downgraded = RegenerateOnce.DowngradeToAbstain(Metadata, pack, verdict.Violation!);

        Assert.False(downgraded.CanDetermine);
        Assert.Null(downgraded.AnswerMarkdown);
        Assert.Contains("140", downgraded.AbstainReason);
        Assert.Contains("132", downgraded.AbstainReason);
        Assert.Equal(Metadata, downgraded.Metadata);
    }

    [Fact]
    public void An_amount_that_equals_the_packs_value_passes()
    {
        var pack = new[]
        {
            MarketDeal(
                "market:deal-1",
                "Market P50 unit price is CHF 132.",
                new PackValue("unitPriceP50", "132", PackValueKind.Amount, "CHF")),
        };

        var verdict = NumericGuard.Validate("The market P50 for this category is CHF 132 per unit.", pack);

        Assert.True(verdict.Passed);
        Assert.Null(verdict.Violation);
    }

    [Fact]
    public void An_amount_is_currency_aware_a_matching_number_in_a_different_currency_does_not_ground()
    {
        var pack = new[]
        {
            MarketDeal(
                "market:deal-1",
                "Market P50 unit price is EUR 132.",
                new PackValue("unitPriceP50", "132", PackValueKind.Amount, "EUR")),
        };

        var verdict = NumericGuard.Validate("The market P50 for this category is CHF 132 per unit.", pack);

        Assert.False(verdict.Passed);
        Assert.Contains("CHF", verdict.Violation);
    }

    [Fact]
    public void An_amount_quoted_verbatim_from_a_pack_snippet_grounds_even_without_a_structured_value()
    {
        // Exercises NumericGuard's own documented fallback (see the currency-amount loop's remarks):
        // a figure copied straight out of a cited clause/market-note excerpt is grounded even when
        // it was never separately structured into PackValue.
        var pack = new[]
        {
            MarketDeal("market:deal-1", "The liability cap is CHF 1,000,000 per the master agreement."),
        };

        var verdict = NumericGuard.Validate("The liability cap is CHF 1,000,000.", pack);

        Assert.True(verdict.Passed);
    }

    [Fact]
    public void An_amount_absent_from_both_values_and_every_snippet_fails()
    {
        var pack = new[]
        {
            MarketDeal(
                "market:deal-1",
                "Market P50 unit price is CHF 132.",
                new PackValue("unitPriceP50", "132", PackValueKind.Amount, "CHF")),
        };

        var verdict = NumericGuard.Validate("A totally invented figure of CHF 999,999 was mentioned.", pack);

        Assert.False(verdict.Passed);
        Assert.Contains("999,999", verdict.Violation);
    }

    [Fact]
    public void A_percentage_that_equals_the_packs_value_passes()
    {
        var pack = new[]
        {
            MarketDeal(
                "market:deal-1",
                "The uplift cap is 7%.",
                new PackValue("upliftCapPercent", "7", PackValueKind.Percentage)),
        };

        var verdict = NumericGuard.Validate("The uplift cap here is 7%.", pack);

        Assert.True(verdict.Passed);
    }

    [Fact]
    public void A_percentage_quoted_verbatim_from_a_pack_snippet_grounds_even_without_a_structured_value()
    {
        var pack = new[] { MarketDeal("market:deal-1", "Typical annual uplift across this category is 7%.") };

        var verdict = NumericGuard.Validate("Typical annual uplift across this category is 7%.", pack);

        Assert.True(verdict.Passed);
    }

    [Fact]
    public void A_percentage_matching_neither_a_value_nor_any_snippet_fails()
    {
        var pack = new[]
        {
            MarketDeal(
                "market:deal-1",
                "The uplift cap is 7%.",
                new PackValue("upliftCapPercent", "7", PackValueKind.Percentage)),
        };

        var verdict = NumericGuard.Validate("The uplift cap here is 25%.", pack);

        Assert.False(verdict.Passed);
        Assert.Contains("25%", verdict.Violation);
    }

    [Fact]
    public void An_iso_date_that_equals_the_packs_value_passes()
    {
        var pack = new[]
        {
            MarketDeal(
                "tenant:contract-1",
                "Renewal date noted in the contract.",
                new PackValue("endDate", "2027-01-15", PackValueKind.Date)),
        };

        var verdict = NumericGuard.Validate("This contract ends on 2027-01-15.", pack);

        Assert.True(verdict.Passed);
    }

    [Fact]
    public void A_long_form_date_that_equals_the_packs_iso_value_passes()
    {
        var pack = new[]
        {
            MarketDeal(
                "tenant:contract-1",
                "Renewal date noted in the contract.",
                new PackValue("endDate", "2027-01-15", PackValueKind.Date)),
        };

        var verdict = NumericGuard.Validate("This contract ends on 15 January 2027.", pack);

        Assert.True(verdict.Passed);
    }

    [Fact]
    public void A_date_quoted_verbatim_from_a_pack_snippet_grounds_even_without_a_structured_value()
    {
        // Exercises NumericGuard's own documented fallback (see the currency-amount loop's own
        // remarks), extended to dates by this task's review pass: a calendar date copied straight
        // out of a cited clause excerpt is grounded even when it was never separately structured
        // into a PackValue — e.g. an MSA's own opening line ("... effective 2026-01-01, governed
        // by ...") with no Values at all
        // (Raffa.IntegrationTests.R1EndToEndTests's own fixture text, R1ExtractionFixtures.cs).
        var pack = new[]
        {
            MarketDeal(
                "tenant:contract-1",
                "MASTER SERVICES AGREEMENT between Acme Corp and Contoso Ltd, effective " +
                "2026-01-01, governed by the laws of the State of Delaware."),
        };

        var verdict = NumericGuard.Validate("The agreement is effective 2026-01-01.", pack);

        Assert.True(verdict.Passed);
    }

    [Fact]
    public void A_date_that_does_not_equal_any_pack_value_fails()
    {
        var pack = new[]
        {
            MarketDeal(
                "tenant:contract-1",
                "Renewal date noted in the contract.",
                new PackValue("endDate", "2027-01-15", PackValueKind.Date)),
        };

        var verdict = NumericGuard.Validate("This contract ends on 2027-03-01.", pack);

        Assert.False(verdict.Passed);
        Assert.Contains("2027-03-01", verdict.Violation);
    }

    [Fact]
    public void An_empty_pack_still_fails_a_fabricated_amount_rather_than_throw()
    {
        var verdict = NumericGuard.Validate("The market P50 is CHF 140.", pack: []);

        Assert.False(verdict.Passed);
    }

    [Fact]
    public void Markdown_with_no_numeric_shape_at_all_passes_trivially()
    {
        var verdict = NumericGuard.Validate("Nothing numeric to check here.", pack: []);

        Assert.True(verdict.Passed);
    }

    [Fact]
    public void Null_markdown_is_treated_as_empty_and_passes()
    {
        var verdict = NumericGuard.Validate(null, pack: []);

        Assert.True(verdict.Passed);
    }

    [Fact]
    public void Validate_rejects_a_null_pack()
    {
        Assert.Throws<ArgumentNullException>(() => NumericGuard.Validate("CHF 140", null!));
    }
}
