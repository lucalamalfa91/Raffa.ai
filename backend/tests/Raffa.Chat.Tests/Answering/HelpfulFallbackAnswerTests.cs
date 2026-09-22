using System.Text.RegularExpressions;
using Raffa.Chat.Application.Answering;
using Raffa.Chat.Application.Capabilities;

namespace Raffa.Chat.Tests.Answering;

/// <summary>
/// The proposal Ask writes itself when no grounded answer exists — persona v2.5's "never end on
/// 'I don't have data I trust enough'". Every variant is a way forward in the question's language,
/// carries no figure that could be wrong about the user's data, and never talks about the machinery.
/// </summary>
public sealed class HelpfulFallbackAnswerTests
{
    private static readonly string?[] Areas =
    [
        null,
        CapabilityCatalog.SavingsKey,
        CapabilityCatalog.RenewalsKey,
        CapabilityCatalog.QuoteCheckKey,
        CapabilityCatalog.ContractDetailKey,
        CapabilityCatalog.DocumentsKey,
        CapabilityCatalog.DocumentsAttentionKey,
    ];

    private static IEnumerable<string> EveryVariant(string question)
    {
        foreach (var area in Areas)
        {
            yield return HelpfulFallbackAnswer.Proposal(question, area);
        }

        yield return HelpfulFallbackAnswer.Proposal(question, null, emptyWorkspace: true);
        yield return HelpfulFallbackAnswer.Proposal("mi aiuti a creare una mail per il rinnovo?", CapabilityCatalog.RenewalsKey);
        yield return HelpfulFallbackAnswer.Proposal("can you draft an email for the renewal?", CapabilityCatalog.RenewalsKey);
        yield return HelpfulFallbackAnswer.ServiceBusy(question);
        yield return HelpfulFallbackAnswer.NoticeDateMissing(question, "Salesforce");
        yield return HelpfulFallbackAnswer.NoticeWithoutContract(question);
    }

    [Theory]
    [InlineData("Where can I save the most this quarter?")]
    [InlineData("Dove possiamo risparmiare sui contratti?")]
    public void No_variant_declines_invents_a_figure_or_names_the_machinery(string question)
    {
        foreach (var text in EveryVariant(question))
        {
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain("I don't have data I trust", text, StringComparison.Ordinal);
            Assert.DoesNotContain("reliable answer", text, StringComparison.Ordinal);
            // "Contract 360" is a screen's name, not a figure about the user's data.
            Assert.DoesNotMatch(new Regex(@"\d"), text.Replace("Contract 360", "Contract", StringComparison.Ordinal));
            Assert.DoesNotMatch(new Regex(@"\bpack\b|citation|guard|canDetermine", RegexOptions.IgnoreCase), text);
        }
    }

    [Fact]
    public void An_email_request_gets_a_ready_to_send_draft_with_placeholders_in_the_questions_language()
    {
        var italian = HelpfulFallbackAnswer.Proposal("mi aiuti a creare una mail che posso inviare per il rinnovo?", CapabilityCatalog.RenewalsKey);
        var english = HelpfulFallbackAnswer.Proposal("Can you help me write an email for the renewal?", null);

        Assert.Contains("**Oggetto:**", italian, StringComparison.Ordinal);
        Assert.Contains("Gentile [nome del referente],", italian, StringComparison.Ordinal);
        Assert.Contains("[data di scadenza]", italian, StringComparison.Ordinal);
        Assert.Contains("**Subject:**", english, StringComparison.Ordinal);
        Assert.Contains("Dear [contact name],", english, StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_contract_yet_uploading_one_is_the_way_forward_even_for_an_email_request()
    {
        var text = HelpfulFallbackAnswer.Proposal("mi scrivi una mail per il rinnovo del contratto?", CapabilityCatalog.RenewalsKey, emptyWorkspace: true);

        Assert.StartsWith("Partiamo dal primo contratto", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_area_answers_in_the_language_of_the_question()
    {
        Assert.StartsWith("Ecco da dove partire per risparmiare", HelpfulFallbackAnswer.Proposal("Dove possiamo risparmiare?", CapabilityCatalog.SavingsKey), StringComparison.Ordinal);
        Assert.StartsWith("Here's where to start saving", HelpfulFallbackAnswer.Proposal("Where can I save the most this quarter?", CapabilityCatalog.SavingsKey), StringComparison.Ordinal);
        Assert.StartsWith("Ecco come preparare il rinnovo", HelpfulFallbackAnswer.Proposal("Come affrontare il prossimo rinnovo?", CapabilityCatalog.RenewalsKey), StringComparison.Ordinal);
        Assert.StartsWith("Happy to help", HelpfulFallbackAnswer.Proposal("how much did we spend on office catering?", null), StringComparison.Ordinal);
        Assert.StartsWith("Per Salesforce la data di disdetta", HelpfulFallbackAnswer.NoticeDateMissing("Quando scade la disdetta con Salesforce?", "Salesforce"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_notice_date_is_said_plainly_and_the_markets_typical_notice_is_labelled_an_estimate()
    {
        var italian = HelpfulFallbackAnswer.NoticeDateMissing("Quando scade la disdetta con Salesforce?", "Salesforce", typicalNoticeDays: 90);
        var english = HelpfulFallbackAnswer.NoticeDateMissing("When is the Salesforce notice due?", "Salesforce", typicalNoticeDays: 90);

        Assert.StartsWith("Per Salesforce la data di disdetta non è ancora tra i dati validati. Dai dati di mercato", italian, StringComparison.Ordinal);
        Assert.Contains("il preavviso tipico è di 90 giorni: è una stima, non un dato del tuo contratto.", italian, StringComparison.Ordinal);
        Assert.Contains("typically have a 90-day notice period: an estimate, not a term of your contract.", english, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mi aiuti a creare una mail che posso inviare per il rinnovo?", CapabilityCatalog.RenewalsKey)]
    [InlineData("Which contracts renew in the next 120 days?", CapabilityCatalog.RenewalsKey)]
    [InlineData("Dove possiamo risparmiare?", CapabilityCatalog.SavingsKey)]
    [InlineData("Is our price above market?", CapabilityCatalog.QuoteCheckKey)]
    [InlineData("What liability do we have with AWS?", CapabilityCatalog.ContractDetailKey)]
    [InlineData("how much did we spend on office catering?", null)]
    public void The_area_follows_the_questions_own_words(string question, string? expected)
    {
        Assert.Equal(expected, HelpfulFallbackAnswer.AreaFor(question));
    }
}
