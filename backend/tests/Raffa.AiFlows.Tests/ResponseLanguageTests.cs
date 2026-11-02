using Raffa.AiFlows.Ask.Answering;
using Raffa.AiFlows.Ask.Interview;
using Raffa.Chat.Application.Language;
using Raffa.SharedKernel.Language;

namespace Raffa.AiFlows.Tests;

/// <summary>The interface language (IT/EN) decides the language of Raffa's replies, not the words of the question.</summary>
public sealed class ResponseLanguageTests
{
    private const string EnglishQuestion = "When is the notice deadline for the DocuSign contract?";
    private const string ItalianQuestion = "Quando scade la disdetta del contratto con DocuSign?";

    [Fact]
    public void Nothing_chosen_keeps_the_language_of_the_question()
    {
        Assert.Null(ResponseLanguage.Current);
        Assert.Null(ResponseLanguage.Directive);
        Assert.Equal("en", QuestionLanguage.Detect(EnglishQuestion));
        Assert.Equal("it", QuestionLanguage.Detect(ItalianQuestion));
        Assert.True(LanguageHint.IsItalian(ItalianQuestion));
        Assert.False(LanguageHint.IsItalian(EnglishQuestion));
    }

    [Fact]
    public void Italian_chosen_wins_over_an_English_question_everywhere()
    {
        using var _ = ResponseLanguage.Use("it");
        Assert.Equal("it", ResponseLanguage.Current);
        Assert.Contains("Italian", ResponseLanguage.Directive);
        Assert.Equal("it", QuestionLanguage.Detect(EnglishQuestion));
        Assert.True(LanguageHint.IsItalian(EnglishQuestion));
        Assert.True(GroundedFallbackAnswer.IsItalian(EnglishQuestion));
        Assert.True(HelpfulFallbackAnswer.IsItalian(EnglishQuestion));
    }

    [Fact]
    public void English_chosen_wins_over_an_Italian_question_everywhere()
    {
        using var _ = ResponseLanguage.Use("en");
        Assert.Contains("English", ResponseLanguage.Directive);
        Assert.Equal("en", QuestionLanguage.Detect(ItalianQuestion));
        Assert.False(LanguageHint.IsItalian(ItalianQuestion));
        Assert.False(GroundedFallbackAnswer.IsItalian(ItalianQuestion));
        Assert.False(HelpfulFallbackAnswer.IsItalian(ItalianQuestion));
    }

    [Fact]
    public async Task The_choice_ends_with_its_scope_and_follows_awaits_but_not_other_calls()
    {
        using (ResponseLanguage.Use("it"))
        {
            await Task.Delay(1);
            Assert.Equal("it", ResponseLanguage.Current);
            using (ResponseLanguage.Use("en"))
            {
                Assert.Equal("en", ResponseLanguage.Current);
            }

            Assert.Equal("it", ResponseLanguage.Current);
        }

        Assert.Null(ResponseLanguage.Current);
    }

    [Theory]
    [InlineData("")]
    [InlineData("fr")]
    [InlineData(null)]
    public void Anything_but_it_or_en_chooses_nothing(string? value)
    {
        using var _ = ResponseLanguage.Use(value);
        Assert.Null(ResponseLanguage.Current);
    }
}
