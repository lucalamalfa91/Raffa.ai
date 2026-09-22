using Raffa.Chat.Application.Language;

namespace Raffa.Chat.Tests.Language;

public sealed class QuestionLanguageTests
{
    [Theory]
    [InlineData("Puoi aiutarmi a scrivere la mail per il rinnovo?")]
    [InlineData("mettimi un promemoria per la disdetta DocuSign")]
    [InlineData("quali leve posso usare per risparmiare 20 k sul rinnovo")]
    [InlineData("Scrivi la mail per il rinnovo Salesforce")]
    public void Italian_question_is_it(string question) => Assert.Equal(QuestionLanguage.Italian, QuestionLanguage.Detect(question));

    [Theory]
    [InlineData("I have to renegotiate with Amazon. Can you help me create an email based on the negotiation leverage?")]
    [InlineData("Export my contracts to Excel")]
    [InlineData("Write the renewal email for Salesforce")]
    public void English_question_is_en(string question) => Assert.Equal(QuestionLanguage.English, QuestionLanguage.Detect(question));

    [Theory]
    [InlineData("")]
    [InlineData("Salesforce")]
    [InlineData("42")]
    public void Ties_and_unknown_default_to_en(string question) => Assert.Equal(QuestionLanguage.English, QuestionLanguage.Detect(question));

    [Fact]
    public void Accents_tip_towards_italian() => Assert.Equal(QuestionLanguage.Italian, QuestionLanguage.Detect("Perché è così?"));
}
