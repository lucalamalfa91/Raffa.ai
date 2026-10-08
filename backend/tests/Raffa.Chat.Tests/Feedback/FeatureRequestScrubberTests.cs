using System.Text.RegularExpressions;
using Raffa.Chat.Application.Feedback;
using Raffa.Chat.Application.Gaps;

namespace Raffa.Chat.Tests.Feedback;

/// <summary>
/// F4-T01: the scrub of the free text before it reaches a public GitHub issue. The corpus is the
/// acceptance test: every identifier, name and amount in it must be gone from the text that is
/// published, while an ordinary request keeps its meaning.
/// </summary>
public sealed class FeatureRequestScrubberTests
{
    private static readonly string[] Suppliers = ["Amazon Web Services", "Salesforce", "Acme S.r.l.", "ServiceNow"];

    private const string User = "alice.bianchi@example.com";

    /// <summary>(input, fragments that must not survive).</summary>
    public static TheoryData<string, string[]> Corpus => new()
    {
        // supplier names, in every case the user might type them
        { "Mandare il rinnovo ad Amazon Web Services entro venerdì", ["Amazon", "Web Services"] },
        { "when the salesforce renewal is near, mail the supplier", ["salesforce"] },
        { "Send a reminder for SERVICENOW", ["SERVICENOW", "ServiceNow"] },
        { "Esporta i contratti di Acme S.r.l. in Excel", ["Acme"] },
        // people
        { "Scrivi a Mario Rossi del legale", ["Mario", "Rossi"] },
        { "Ask Dr. Bianchi to approve it", ["Bianchi"] },
        { "Il Sig. Verdi vuole un promemoria", ["Verdi"] },
        { "Mario Rossi wants a weekly export", ["Mario", "Rossi"] },
        { "per la dott.ssa Neri", ["Neri"] },
        { "alice.bianchi mi ha chiesto un report", ["alice", "bianchi"] },
        { "Forward it to @mrossi_legal please", ["mrossi"] },
        // contact data
        { "Invia a mario.rossi@acme-corp.com il riepilogo", ["mario.rossi", "acme-corp", "@"] },
        { "write to ops@vendor.io and cc legal@vendor.io", ["ops@", "vendor.io", "legal@"] },
        { "Chiama il +39 02 1234567 o il 333 123 4567", ["1234567", "333", "4567"] },
        { "call me on (415) 555-0132", ["555", "0132"] },
        { "see https://portal.acme-corp.com/contracts/1234?x=1 and www.acme.it/ordini", ["https", "portal", "acme", "contracts", "1234"] },
        { "the portal is at acme-corp.com", ["acme-corp.com"] },
        // identifiers
        { "IBAN IT60X0542811101000000123456 per il pagamento", ["IT60", "0542811101"] },
        { "partita IVA 12345678901 e CF RSSMRA80A01H501U", ["12345678901", "RSSMRA80A01H501U"] },
        { "contract CT-2024-0042 and PO 887766", ["CT-2024", "0042", "887766"] },
        { "id 3f2504e0-4f89-11d3-9a0c-0305e82c3301 in the log", ["3f2504e0", "4f89", "0305e82c3301"] },
        // amounts, dates, quantities
        { "Se il contratto vale più di 20.000 euro avvisami", ["20.000", "20000", "euro"] },
        { "save at least € 15k and USD 2,5M a year", ["15k", "2,5M", "USD 2"] },
        { "EUR 20000 and 1.2M on 12/03/2026", ["20000", "1.2M", "12/03/2026"] },
        { "entro il 30 settembre 2026 il 12% in meno", ["2026", "12%", "30"] },
        // markup that could break the quote block or inject links
        { "Export it\n# Heading\n[click](https://evil.example/x)", ["evil", "https", "example"] },
    };

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Nothing_that_identifies_a_supplier_a_person_or_a_record_survives(string input, string[] forbidden)
    {
        var scrubbed = FeatureRequestScrubber.Scrub(input, [.. Suppliers, User]);

        foreach (var fragment in forbidden)
        {
            Assert.DoesNotContain(fragment, scrubbed, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("Send the email for me\nfrom my mailbox")]
    [InlineData("Esporta il portafoglio in Excel ogni trimestre")]
    [InlineData("A monthly summary for my manager")]
    [InlineData("Vorrei un promemoria prima della disdetta")]
    [InlineData("Generate a report for the CFO and export it to PDF")]
    [InlineData("Create a purchase order from the contract page")]
    public void An_ordinary_request_keeps_its_meaning_untouched(string input)
    {
        Assert.Equal(input, FeatureRequestScrubber.Scrub(input, Suppliers));
    }

    [Fact]
    public void The_scrub_leaves_a_readable_sentence_with_neutral_markers()
    {
        var scrubbed = FeatureRequestScrubber.Scrub("Invia a mario.rossi@acme-corp.com il riepilogo di Amazon Web Services da 20.000 euro", Suppliers);

        Assert.Equal("Invia a [email] il riepilogo di [name] da [number]", scrubbed);
    }

    [Fact]
    public void Blank_input_gives_an_empty_string_and_names_are_optional()
    {
        Assert.Equal(string.Empty, FeatureRequestScrubber.Scrub(null));
        Assert.Equal(string.Empty, FeatureRequestScrubber.Scrub("   \n  "));
        Assert.Equal("Export to Excel", FeatureRequestScrubber.Scrub("Export to Excel"));
    }

    [Fact]
    public void A_known_name_is_removed_as_a_whole_word_only()
    {
        // "Cloud" is a supplier here, but "cloudy" and "Cloudflare" are other words.
        var scrubbed = FeatureRequestScrubber.Scrub("the cloudy forecast, Cloud and cloud.", ["Cloud"]);

        Assert.Contains("cloudy", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"\bcloud\b", RegexOptions.IgnoreCase), scrubbed.Replace("cloudy", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void The_composed_issue_publishes_only_the_scrubbed_text()
    {
        var answers = new FeedbackAnswers(
            "Mandare a Mario Rossi (mario.rossi@acme-corp.com) il contratto Salesforce da 40.000 euro, IBAN IT60X0542811101000000123456",
            FeedbackQuestions.FrequencyEveryRenewal,
            FeedbackQuestions.ImportanceBlocking);
        var issue = new FeatureRequestIssue(
            "send-supplier", "Send a message to the supplier", "it", "dev", "0123abcd", answers, KnownNames: [.. Suppliers, User]);

        var (title, body) = FeatureRequestIssueText.Compose(issue);

        foreach (var forbidden in new[] { "Mario", "Rossi", "acme-corp", "@", "Salesforce", "40.000", "IT60", "euro" })
        {
            Assert.DoesNotContain(forbidden, title + body, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("> Mandare a [name]", body, StringComparison.Ordinal);
        Assert.Contains("## Human approval", body, StringComparison.Ordinal);
    }

    [Fact]
    public void When_nothing_is_left_after_the_scrub_the_issue_says_so()
    {
        var answers = new FeedbackAnswers("mario.rossi@acme-corp.com", FeedbackQuestions.FrequencySometimes, FeedbackQuestions.ImportanceNiceToHave);
        var issue = new FeatureRequestIssue("export-file", "Export to Excel or Word", "it", "dev", "0123abcd", answers);

        var (_, body) = FeatureRequestIssueText.Compose(issue);

        Assert.DoesNotContain("acme-corp", body, StringComparison.Ordinal);
        Assert.Contains("[email]", body, StringComparison.Ordinal);
    }
}
