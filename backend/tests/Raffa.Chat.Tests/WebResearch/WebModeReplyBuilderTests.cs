using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Pack;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Application.WebResearch;

namespace Raffa.Chat.Tests.WebResearch;

/// <summary>
/// ADR-032: the off-context pointer is two outbound links and no model call; a combined web-mode
/// reply keeps the tenant's answer and the web research under their own headings, and every
/// <c>[n]</c> still resolves to its own card after the web citations are renumbered.
/// </summary>
public sealed class WebModeReplyBuilderTests
{
    private static ReplyCitation Citation(int n, string corpus, string title) =>
        new(n, corpus, title, null, title + " snippet", null, null, null, null, null, corpus == PackCorpus.Web ? "https://example.com/" + n : null, null);

    private static CopilotReply InternalAnswer() => new(
        ReplyKind.Answer,
        "Your Salesforce MSA renews on 2026-12-31 with a 5% uplift cap [1][2].",
        [Citation(1, PackCorpus.Tenant, "Salesforce MSA"), Citation(2, PackCorpus.Market, "SaaS band")],
        [new CopilotAction("Open Contract 360 →", "/contracts/abc", CopilotActionKind.Navigate)],
        new ReplyProvenance([PackCorpus.Tenant, PackCorpus.Market], "gpt-answer", "answer-v2.5", "hash-1"),
        ["What are my levers?"]);

    private static WebResearchOutcome WebAnswer() => new(
        WebResearchOutcomeKind.Answered,
        "Public, unverified: vendors announced list-price increases [1]; buyers push back with multi-year terms [2].",
        [Citation(1, PackCorpus.Web, "example.com · News"), Citation(2, PackCorpus.Web, "example.org · Levers")],
        new ReplyProvenance([PackCorpus.Web], "gpt-research", WebResearchPrompt.OpenVersion, "hash-2", Unverified: true),
        SourceCount: 2,
        GuardIntervened: false,
        GuardViolation: null,
        Error: null);

    private static WebResearchOutcome WebOutcome(WebResearchOutcomeKind kind) => new(
        kind, "not shown", [], ReplyProvenance.NoModelCall([]), 0, kind == WebResearchOutcomeKind.Abstained, null, null);

    [Fact]
    public void Off_context_is_a_redirect_with_a_google_and_a_perplexity_search_for_the_question()
    {
        var reply = WebModeReplyBuilder.OffContext("dimmi la ricetta della carbonara");

        Assert.Equal(ReplyKind.Redirect, reply.Kind);
        Assert.Empty(reply.Citations);
        Assert.Null(reply.Provenance.ModelId);
        Assert.Contains("Google", reply.AnswerMarkdown, StringComparison.Ordinal);
        Assert.Contains("Perplexity", reply.AnswerMarkdown, StringComparison.Ordinal);
        Assert.Contains("fuori dal perimetro", reply.AnswerMarkdown, StringComparison.Ordinal);

        Assert.Equal(2, reply.Actions.Count);
        Assert.All(reply.Actions, a => Assert.Equal(CopilotActionKind.External, a.Kind));
        Assert.Equal("Cerca su Google →", reply.Actions[0].Label);
        Assert.Equal("https://www.google.com/search?q=dimmi+la+ricetta+della+carbonara", reply.Actions[0].Href);
        Assert.Equal("https://www.perplexity.ai/search?q=dimmi+la+ricetta+della+carbonara", reply.Actions[1].Href);
    }

    [Fact]
    public void Off_context_speaks_english_to_an_english_question()
    {
        var reply = WebModeReplyBuilder.OffContext("tell me a joke about cats");

        Assert.Contains("outside Raffa's scope", reply.AnswerMarkdown, StringComparison.Ordinal);
        Assert.Equal("Search on Google →", reply.Actions[0].Label);
        Assert.Equal("Ask Perplexity →", reply.Actions[1].Label);
    }

    [Theory]
    [InlineData("search the web for a carbonara recipe", "for+a+carbonara+recipe")]
    [InlineData("recipe for 4 people, mail me at a@b.com, costs €40", "recipe+for+4+people+mail+me+at+costs")]
    [InlineData("carbonara?", "carbonara")]
    [InlineData("€40", "")]
    public void The_outbound_query_is_the_questions_words_without_figures_addresses_or_the_web_phrase(string question, string expected) =>
        Assert.Equal(expected, WebModeReplyBuilder.ExternalSearchQuery(question));

    [Fact]
    public void Combine_keeps_both_halves_under_their_own_headings_and_renumbers_the_web_citations()
    {
        var benchmark = new CopilotAction("Open Quote check →", "/quotes", CopilotActionKind.Navigate);

        var reply = WebModeReplyBuilder.Combine(InternalAnswer(), WebAnswer(), [benchmark], italian: false);

        Assert.Equal(ReplyKind.Answer, reply.Kind);
        Assert.StartsWith("**From your contracts and Raffa's data**", reply.AnswerMarkdown, StringComparison.Ordinal);
        Assert.Contains("renews on 2026-12-31 with a 5% uplift cap [1][2].", reply.AnswerMarkdown, StringComparison.Ordinal);
        Assert.Contains("**From the public web · unverified**", reply.AnswerMarkdown, StringComparison.Ordinal);
        Assert.Contains("list-price increases [3]; buyers push back with multi-year terms [4].", reply.AnswerMarkdown, StringComparison.Ordinal);

        Assert.Equal([1, 2, 3, 4], reply.Citations.Select(c => c.N).ToList());
        Assert.Equal([PackCorpus.Tenant, PackCorpus.Market, PackCorpus.Web, PackCorpus.Web], reply.Citations.Select(c => c.Corpus).ToList());
        Assert.Equal("https://example.com/1", reply.Citations[2].Href);

        Assert.Equal([PackCorpus.Tenant, PackCorpus.Market, PackCorpus.Web], reply.Provenance.Sources);
        Assert.True(reply.Provenance.Unverified);
        Assert.Equal("gpt-answer", reply.Provenance.ModelId);
        Assert.Equal("answer-v2.5+research-open-v1", reply.Provenance.PromptVersion);
        Assert.Equal(["Open Contract 360 →", "Open Quote check →"], reply.Actions.Select(a => a.Label).ToList());
        Assert.Equal(["What are my levers?"], reply.FollowUps);
    }

    [Fact]
    public void Combine_in_italian_uses_italian_headings()
    {
        var reply = WebModeReplyBuilder.Combine(InternalAnswer(), WebAnswer(), [], italian: true);

        Assert.StartsWith("**Dai tuoi contratti e dai dati di Raffa**", reply.AnswerMarkdown, StringComparison.Ordinal);
        Assert.Contains("**Dal web pubblico · non verificato**", reply.AnswerMarkdown, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(WebResearchOutcomeKind.Abstained, "no sources clear enough")]
    [InlineData(WebResearchOutcomeKind.Failed, "not available right now")]
    public void When_the_web_adds_nothing_the_tenant_answer_stands_alone_with_one_honest_line(WebResearchOutcomeKind kind, string note)
    {
        var internalAnswer = InternalAnswer();

        var reply = WebModeReplyBuilder.Combine(internalAnswer, WebOutcome(kind), [], italian: false);

        Assert.StartsWith(internalAnswer.AnswerMarkdown, reply.AnswerMarkdown, StringComparison.Ordinal);
        Assert.Contains(note, reply.AnswerMarkdown, StringComparison.Ordinal);
        Assert.Equal(internalAnswer.Citations, reply.Citations);
        Assert.False(reply.Provenance.Unverified);
    }

    [Fact]
    public void A_web_refusal_leaves_the_tenant_answer_untouched()
    {
        var internalAnswer = InternalAnswer();

        var reply = WebModeReplyBuilder.Combine(internalAnswer, WebOutcome(WebResearchOutcomeKind.Refused), [], italian: false);

        Assert.Same(internalAnswer, reply);
    }

    [Theory]
    [InlineData("a [1] b [2]", 0, "a [1] b [2]")]
    [InlineData("a [1] b [2][3]", 2, "a [3] b [4][5]")]
    [InlineData("no markers", 5, "no markers")]
    public void Markers_shift_by_the_offset(string markdown, int offset, string expected) =>
        Assert.Equal(expected, WebModeReplyBuilder.ShiftMarkers(markdown, offset));
}
