using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Chat.Application.Answering;
using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Application.WebResearch;
using Raffa.Chat.Domain.Conversations;
using Raffa.Chat.Tests.TestSupport;
using Raffa.SharedKernel;

namespace Raffa.Chat.Tests.WebResearch;

/// <summary>
/// F3-D03 — the history handed to the Ask engine never carries web text: a web-research turn is cut
/// (a combined reply keeps its contracts half) or replaced by a neutral placeholder, and the user's
/// own turns, contract answers and the window size are untouched.
/// </summary>
public sealed class WebHistoryIsolationTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);
    private const string WebCitation = """[{"n":1,"corpus":"web","title":"example.com · SaaS renewal benchmarks","href":"https://example.com/a"}]""";
    private const string ContractCitation = """[{"n":1,"corpus":"documents","title":"MSA","page":3}]""";
    private const string WebSummary = "Public, unverified: a 5-10% uplift cap is common on enterprise SaaS renewals [1].";

    private static ConversationMessageResult Message(
        ConversationRole role, string markdown, string citations = "[]", string? promptVersion = null) =>
        new(EntityId.New(), EntityId.New(), role, ConversationMessageKind.Answer, markdown, citations, "[]", null, promptVersion, null, At);

    [Fact]
    public void A_web_only_reply_is_replaced_by_a_placeholder_that_carries_no_web_text()
    {
        var history = new[]
        {
            Message(ConversationRole.You, "search the web for typical uplift caps on saas renewals"),
            Message(ConversationRole.Raffa, WebSummary, WebCitation, WebResearchPrompt.Version),
        };

        var prompt = WebHistoryIsolation.ForPrompt(history, limit: 6);

        Assert.Equal(2, prompt.Count);
        Assert.Equal(history[0].Markdown, prompt[0].Markdown);
        Assert.Equal(WebHistoryIsolation.Placeholder, prompt[1].Markdown);
        Assert.DoesNotContain("5-10%", prompt[1].Markdown, StringComparison.Ordinal);
        Assert.Equal(ConversationRole.Raffa, prompt[1].Role);
    }

    [Theory]
    [InlineData(WebResearchPrompt.Version)]
    [InlineData(WebResearchPrompt.OpenVersion)]
    public void A_web_abstain_or_refusal_with_no_citations_is_still_a_web_turn_by_its_prompt_version(string version)
    {
        var abstain = Message(ConversationRole.Raffa, "I found these public sources on the topic: example.com.", "[]", version);

        Assert.True(WebHistoryIsolation.IsWebTurn(abstain));
        Assert.Equal(WebHistoryIsolation.Placeholder, WebHistoryIsolation.ForPrompt([abstain], 6).Single().Markdown);
    }

    [Theory]
    [InlineData("research-v1")]
    [InlineData("research-open-v1")]
    public void F3_T01_a_turn_stored_under_the_previous_persona_version_is_still_a_web_turn(string version)
    {
        var stored = Message(ConversationRole.Raffa, "I found these public sources on the topic: example.com.", "[]", version);

        Assert.True(WebHistoryIsolation.IsWebTurn(stored));
        Assert.Equal(WebHistoryIsolation.Placeholder, WebHistoryIsolation.ForPrompt([stored], 6).Single().Markdown);
    }

    [Fact]
    public void A_combined_reply_keeps_only_its_contracts_half()
    {
        foreach (var italian in new[] { true, false })
        {
            var markdown =
                "**From your contracts and Raffa's data**\n\nThe notice period is 90 days [1].\n\n" +
                $"**{WebModeReplyBuilder.WebSectionTitle(italian)}**\n\n{WebSummary.Replace("[1]", "[2]")}";
            var combined = Message(
                ConversationRole.Raffa,
                markdown,
                ContractCitation.TrimEnd(']') + "," + WebCitation.TrimStart('[').Replace("\"n\":1", "\"n\":2"),
                "answer-v3+research-open-v1");

            var kept = WebHistoryIsolation.ForPrompt([combined], 6).Single().Markdown;

            Assert.Contains("The notice period is 90 days [1].", kept, StringComparison.Ordinal);
            Assert.DoesNotContain("5-10%", kept, StringComparison.Ordinal);
            Assert.DoesNotContain(WebModeReplyBuilder.WebSectionTitle(italian), kept, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Contract_answers_user_turns_and_non_web_citations_pass_through_unchanged()
    {
        var history = new[]
        {
            Message(ConversationRole.You, "what is the notice period?"),
            Message(ConversationRole.Raffa, "The notice period is 90 days [1].", ContractCitation, "answer-v3"),
            Message(ConversationRole.You, "and the uplift cap?"),
            // A user message that happens to mention a research prompt version or the web heading is still the user's.
            Message(ConversationRole.You, "**From the public web · unverified** research-v1"),
        };

        var prompt = WebHistoryIsolation.ForPrompt(history, 6);

        Assert.Equal(history.Select(m => m.Markdown), prompt.Select(m => m.Markdown));
        Assert.All(history, m => Assert.False(WebHistoryIsolation.IsWebTurn(m)));
    }

    [Fact]
    public void The_window_is_the_last_n_messages_and_web_turns_inside_it_are_still_cut()
    {
        var history = new List<ConversationMessageResult>
        {
            Message(ConversationRole.You, "q0"),
            Message(ConversationRole.Raffa, "a0 [1]", ContractCitation, "answer-v3"),
            Message(ConversationRole.You, "q1"),
            Message(ConversationRole.Raffa, WebSummary, WebCitation, WebResearchPrompt.Version),
            Message(ConversationRole.You, "q2"),
            Message(ConversationRole.Raffa, "a2 [1]", ContractCitation, "answer-v3"),
        };

        var prompt = WebHistoryIsolation.ForPrompt(history, limit: 4);

        Assert.Equal(["q1", WebHistoryIsolation.Placeholder, "q2", "a2 [1]"], prompt.Select(m => m.Markdown));
    }

    [Fact]
    public void A_malformed_citation_list_is_not_a_reason_to_throw_or_to_cut()
    {
        var odd = Message(ConversationRole.Raffa, "A plain answer.", "not json", "answer-v3");

        Assert.False(WebHistoryIsolation.IsWebTurn(odd));
        Assert.Equal("A plain answer.", WebHistoryIsolation.ForPrompt([odd], 6).Single().Markdown);
    }

    [Fact]
    public void The_stored_conversation_is_not_modified()
    {
        var web = Message(ConversationRole.Raffa, WebSummary, WebCitation, WebResearchPrompt.Version);

        _ = WebHistoryIsolation.ForPrompt([web], 6);

        Assert.Equal(WebSummary, web.Markdown);
    }

    [Fact]
    public async Task F3_D03_a_web_turn_does_not_reach_the_answer_prompt_in_the_next_turn()
    {
        var history = new[]
        {
            Message(ConversationRole.You, "search the web for typical uplift caps on saas renewals"),
            Message(ConversationRole.Raffa, WebSummary, WebCitation, WebResearchPrompt.Version),
            Message(ConversationRole.You, "what is the notice period in my Oracle contract?"),
            Message(ConversationRole.Raffa, "The notice period is 90 days [1].", ContractCitation, "answer-v3"),
        };
        var gateway = new CapturingAnswerGateway();

        // Exactly what the endpoint hands the Ask engine for the next typed turn.
        var recentTurns = WebHistoryIsolation.ForPrompt(history, limit: 6)
            .Select(m => (Role: m.Role == ConversationRole.You ? "user" : "assistant", m.Markdown))
            .ToList();
        await new AnswerComposer(gateway).AnswerAsync("and the uplift cap?", ScreenshotSavingsPack.Build(), recentTurns);

        var prompt = Assert.Single(gateway.Questions);
        Assert.DoesNotContain("5-10%", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("enterprise SaaS renewals", prompt, StringComparison.Ordinal);
        Assert.Contains(WebHistoryIsolation.Placeholder, prompt, StringComparison.Ordinal);
        // The contracts conversation is still there for the follow-up to resolve against.
        Assert.Contains("The notice period is 90 days [1].", prompt, StringComparison.Ordinal);
        Assert.Contains("what is the notice period in my Oracle contract?", prompt, StringComparison.Ordinal);
        Assert.EndsWith("Current question: and the uplift cap?", prompt, StringComparison.Ordinal);
    }

    private sealed class CapturingAnswerGateway : IAiGateway
    {
        public List<string> Questions { get; } = [];

        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default)
        {
            Questions.Add(request.Question);
            return Task.FromResult(Result<AiAnswerResult>.Failure("captured; no answer needed"));
        }

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
