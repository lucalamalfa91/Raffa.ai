using System.Text.RegularExpressions;
using Raffa.AiGateway.Contracts;
using Raffa.Chat.Application.Answering;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Guards;
using Raffa.Chat.Application.Pack;
using Raffa.Chat.Tests.TestSupport;

namespace Raffa.Chat.Tests.Answering;

/// <summary>
/// Proves <see cref="GroundedFallbackAnswer"/>: when the model tried to answer but could not be
/// trusted, Raffa acknowledges the question and answers from the pack's own facts — the validated
/// contracts, the calculators and the market feed — with real <c>[n]</c> citations, no engineer
/// chrome, and a result that passes the very guards a model answer must pass.
/// </summary>
public sealed class GroundedFallbackAnswerTests
{
    private static readonly AiCallMetadata Metadata =
        new("fixture-answer-model", "v1", AnswerPromptV2.Version, new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero), "deadbeef");

    private static readonly Regex Marker = new(@"\[(\d+)\]", RegexOptions.Compiled);

    [Fact]
    public void Screenshot_question_gets_an_acknowledged_cited_answer_instead_of_an_abstain()
    {
        var pack = ScreenshotSavingsPack.Build();

        var result = GroundedFallbackAnswer.Compose("Where can I save the most this quarter?", pack, Metadata);

        Assert.NotNull(result);
        Assert.True(result!.CanDetermine);
        var markdown = result.AnswerMarkdown!;

        Assert.StartsWith(
            "Here's where you can save and what to negotiate, based on your validated contracts and the market data.",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains("Over the next 90 days: 2 contracts can be acted on", markdown, StringComparison.Ordinal);
        Assert.Contains("**Where to act**", markdown, StringComparison.Ordinal);
        Assert.Contains("**Play 1 — Oracle uplift cap**", markdown, StringComparison.Ordinal);
        Assert.Contains("Databricks", markdown, StringComparison.Ordinal);
        Assert.Contains("**Oracle — #1 inside the window**", markdown, StringComparison.Ordinal);
        Assert.Contains("**Market check**", markdown, StringComparison.Ordinal);
        Assert.Contains("EUR 132.5", markdown, StringComparison.Ordinal);
        Assert.Contains("EUR 15365 to EUR 30730", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Answer_never_shows_engineer_chrome()
    {
        var result = GroundedFallbackAnswer.Compose("Where can I save the most this quarter?", ScreenshotSavingsPack.Build(), Metadata)!;
        var markdown = result.AnswerMarkdown!;

        Assert.DoesNotContain("actionKey", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("R-SYS", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Grounded in", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("calc:", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("raffa:", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("=", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("I don't have data I trust", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Window 90 days, no amount named", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(ScreenshotSavingsPack.ContractId, markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Answer_passes_the_grounding_and_numeric_guards()
    {
        var pack = ScreenshotSavingsPack.Build();

        var result = GroundedFallbackAnswer.Compose("Where can I save the most this quarter?", pack, Metadata)!;

        Assert.True(GroundingGuard.Validate(result, pack).Passed, GroundingGuard.Validate(result, pack).Violation);
        Assert.True(NumericGuard.Validate(result.AnswerMarkdown, pack).Passed, NumericGuard.Validate(result.AnswerMarkdown, pack).Violation);
    }

    [Fact]
    public void Every_marker_resolves_to_a_cited_pack_item_in_order()
    {
        var pack = ScreenshotSavingsPack.Build();

        var result = GroundedFallbackAnswer.Compose("Where can I save the most this quarter?", pack, Metadata)!;
        var markers = Marker.Matches(result.AnswerMarkdown!).Select(m => int.Parse(m.Groups[1].Value)).Distinct().ToList();

        Assert.Equal(Enumerable.Range(1, result.CitationKeys!.Count), markers);
        Assert.All(result.CitationKeys!, key => Assert.Contains(pack, item => item.CitationKey == key));
        Assert.DoesNotContain(result.CitationKeys!, key => key.StartsWith("raffa:", StringComparison.Ordinal));
        Assert.Equal("calc:portfolio-target", result.CitationKeys![0]);
    }

    [Fact]
    public void Actions_are_the_packs_feature_capabilities_and_follow_ups_lead_somewhere()
    {
        var result = GroundedFallbackAnswer.Compose("Where can I save the most this quarter?", ScreenshotSavingsPack.Build(), Metadata)!;

        Assert.Equal([CapabilityCatalog.RenewalsKey, CapabilityCatalog.SavingsKey, CapabilityCatalog.PortfolioKey], result.ActionKeys);
        Assert.Equal(["Which of these should we start first?", "Which levers save the most?"], result.FollowUps);
        Assert.Equal(Metadata, result.Metadata);
    }

    [Fact]
    public void An_italian_question_gets_an_italian_acknowledgement()
    {
        var result = GroundedFallbackAnswer.Compose(
            "Dove posso risparmiare di più questo trimestre?", ScreenshotSavingsPack.Build(), Metadata)!;

        Assert.StartsWith("Ecco dove puoi risparmiare e cosa negoziare", result.AnswerMarkdown!, StringComparison.Ordinal);
        Assert.Contains("Nei prossimi 90 giorni:", result.AnswerMarkdown!, StringComparison.Ordinal);
        Assert.Contains("**Dove agire**", result.AnswerMarkdown!, StringComparison.Ordinal);
        Assert.Contains("**Confronto con il mercato**", result.AnswerMarkdown!, StringComparison.Ordinal);
        Assert.Equal(["Quale contratto è prioritario?", "Quali leve fanno risparmiare di più?"], result.FollowUps);
    }

    [Fact]
    public void The_question_is_never_echoed_so_its_numbers_cannot_trip_the_numeric_guard()
    {
        var pack = ScreenshotSavingsPack.Build();

        var result = GroundedFallbackAnswer.Compose("How do I cut costs by 40k or 20% this quarter?", pack, Metadata)!;

        Assert.DoesNotContain("40k", result.AnswerMarkdown!, StringComparison.Ordinal);
        Assert.DoesNotContain("20%", result.AnswerMarkdown!, StringComparison.Ordinal);
        Assert.True(NumericGuard.Validate(result.AnswerMarkdown, pack).Passed);
    }

    [Fact]
    public void A_pack_of_only_raffa_items_has_nothing_to_answer_from()
    {
        var pack = ScreenshotSavingsPack.Build().Where(item => item.Corpus == PackCorpus.Raffa).ToList();

        Assert.Null(GroundedFallbackAnswer.Compose("Where can I save the most this quarter?", pack, Metadata));
        Assert.Null(GroundedFallbackAnswer.Compose("Where can I save the most this quarter?", [], Metadata));
    }

    [Fact]
    public void A_clause_pack_is_answered_from_the_validated_contracts_with_a_generic_acknowledgement()
    {
        var clause = new PackItem(
            "fact:contract-1:clause[0]",
            PackCorpus.Tenant,
            "Salesforce · Liability clause",
            "p.12",
            12, "§8.4",
            "The liability cap is CHF 450000 under this agreement, see note [3] of the schedule.",
            "/contracts/contract-1", null, null,
            "validated contract",
            []);

        var result = GroundedFallbackAnswer.Compose("what liability coverage do we have on file", [clause], Metadata)!;

        // No market item was cited, so the acknowledgement does not claim one.
        Assert.StartsWith("Here's what your validated contracts show on this.", result.AnswerMarkdown!, StringComparison.Ordinal);
        Assert.Contains("**From your validated contracts**", result.AnswerMarkdown!, StringComparison.Ordinal);
        Assert.Contains("CHF 450000", result.AnswerMarkdown!, StringComparison.Ordinal);
        Assert.Contains("note (3)", result.AnswerMarkdown!, StringComparison.Ordinal);
        Assert.Equal(["fact:contract-1:clause[0]"], result.CitationKeys);
        Assert.True(GroundingGuard.Validate(result, [clause]).Passed);
        Assert.True(NumericGuard.Validate(result.AnswerMarkdown, [clause]).Passed);
    }

    [Fact]
    public void A_long_snippet_is_cut_on_a_boundary_and_stays_guard_safe()
    {
        var longText = string.Join(" ", Enumerable.Repeat("The renewal price rises by CHF 1200 a year unless capped in writing.", 8));
        var clause = new PackItem(
            "fact:contract-1:clause[1]",
            PackCorpus.Tenant,
            "Acme · Price increase clause",
            null, null, null,
            longText,
            null, null, null,
            "validated contract",
            []);

        var result = GroundedFallbackAnswer.Compose("what does the price increase clause say", [clause], Metadata)!;
        var bullet = result.AnswerMarkdown!.Split('\n').Single(line => line.StartsWith("- ", StringComparison.Ordinal));

        Assert.True(bullet.Length < longText.Length);
        Assert.EndsWith("in writing. [1]", bullet, StringComparison.Ordinal);
        Assert.True(NumericGuard.Validate(result.AnswerMarkdown, [clause]).Passed);
    }

    [Theory]
    [InlineData("Where can I save the most this quarter?", false)]
    [InlineData("how do I cut costs by 40k this quarter across my active contracts?", false)]
    [InlineData("Dove possiamo risparmiare?", true)]
    [InlineData("Qual e il massimale di responsabilita nel contratto Microsoft?", true)]
    [InlineData("come faccio a risparmiare 20k?", true)]
    public void Language_is_read_from_the_question(string question, bool italian)
    {
        Assert.Equal(italian, GroundedFallbackAnswer.IsItalian(question));
    }

    [Fact]
    public void Suggested_questions_follow_the_capability_and_the_language()
    {
        Assert.Equal(
            ["Where can we save?", "What is the largest saving right now?"],
            GroundedFallbackAnswer.SuggestedQuestions("how much did we pay in legal fees last year?", CapabilityCatalog.SavingsKey));
        Assert.Equal(
            ["Which contracts are most critical?", "Where can we save?"],
            GroundedFallbackAnswer.SuggestedQuestions("how much did we pay in legal fees last year?", null));
        Assert.Equal(
            ["Quali contratti sono mal posizionati sul mercato?", "Dove possiamo risparmiare?"],
            GroundedFallbackAnswer.SuggestedQuestions("Il contratto Salesforce è in linea con il mercato?", CapabilityCatalog.QuoteCheckKey));
    }

    [Fact]
    public void A_suggested_question_identical_to_the_one_just_asked_is_left_out()
    {
        Assert.Equal(
            ["What is the largest saving right now?"],
            GroundedFallbackAnswer.SuggestedQuestions("where can we save", CapabilityCatalog.SavingsKey));
    }

    [Fact]
    public void A_line_that_is_no_longer_verbatim_is_dropped_and_the_rest_still_answers()
    {
        // "CHF\n450000" collapses to "CHF 450000" — no longer in any snippet or value, so that one
        // line would fail the numeric guard; it is left out rather than sinking the whole answer.
        var broken = new PackItem(
            "fact:contract-1:clause[0]",
            PackCorpus.Tenant,
            "Salesforce · Liability clause",
            null, null, null,
            "The liability cap is CHF\n450000 under this agreement.",
            null, null, null,
            "validated contract",
            []);
        var pack = ScreenshotSavingsPack.Build().Append(broken).ToList();

        var result = GroundedFallbackAnswer.Compose("Where can I save the most this quarter?", pack, Metadata)!;

        Assert.DoesNotContain("450000", result.AnswerMarkdown!, StringComparison.Ordinal);
        Assert.DoesNotContain("fact:contract-1:clause[0]", result.CitationKeys!);
        Assert.True(NumericGuard.Validate(result.AnswerMarkdown, pack).Passed);
    }

    [Fact]
    public void The_which_contract_was_picked_sentence_always_leads()
    {
        var disambiguation = new PackItem(
            "calc:multi-contract-resolution",
            PackCorpus.Calc,
            "Which Oracle contract",
            null, null, null,
            "You have two Oracle contracts; this answer is about the one whose notice comes first.",
            null, null, null,
            "deterministic calculator",
            []);
        var pack = ScreenshotSavingsPack.Build().Append(disambiguation).ToList();

        var result = GroundedFallbackAnswer.Compose("Where can I save with Oracle?", pack, Metadata)!;
        var paragraphs = result.AnswerMarkdown!.Split("\n\n");

        Assert.StartsWith("You have two Oracle contracts;", paragraphs[1], StringComparison.Ordinal);
        Assert.Equal("calc:multi-contract-resolution", result.CitationKeys![0]);
    }

    [Theory]
    [InlineData("The target is not supported inside the window by the grounded levers; say so and name the gap.")]
    [InlineData("The target is not supported by the grounded levers; say so and name what is missing.")]
    public void The_calculators_instruction_to_the_model_is_never_quoted(string explanation)
    {
        var target = new PackItem(
            "calc:savings-target",
            PackCorpus.Calc,
            "Oracle — saving target and lever coverage",
            "target not supported by the evidence",
            null, null,
            explanation,
            null, null, null,
            "deterministic calculator",
            [new PackValue("targetAmount", "40000", PackValueKind.Amount, "EUR")]);

        var result = GroundedFallbackAnswer.Compose("How do I save 40k with Oracle?", [target], Metadata)!;

        Assert.DoesNotContain("say so", result.AnswerMarkdown!, StringComparison.Ordinal);
        Assert.Contains("not supported", result.AnswerMarkdown!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_council_verdict_is_only_quoted_when_the_user_named_a_goal()
    {
        var noGoal = GroundedFallbackAnswer.Compose("Where can I save the most this quarter?", ScreenshotSavingsPack.Build(), Metadata)!;
        Assert.DoesNotContain("calc:council:verdict", noGoal.CitationKeys!);

        var withGoal = ScreenshotSavingsPack.Build()
            .Select(item => item.CitationKey == "calc:portfolio-target"
                ? item with { Values = [.. item.Values, new PackValue("targetAmount", "20000", PackValueKind.Amount, "EUR")] }
                : item)
            .ToList();
        var goal = GroundedFallbackAnswer.Compose("How do I save 20k this quarter?", withGoal, Metadata)!;
        Assert.Contains("calc:council:verdict", goal.CitationKeys!);
    }

    [Fact]
    public void Savings_shape_is_read_from_the_pack()
    {
        Assert.True(GroundedFallbackAnswer.IsSavingsShaped(ScreenshotSavingsPack.Build()));
        Assert.False(GroundedFallbackAnswer.IsSavingsShaped(
            ScreenshotSavingsPack.Build().Where(item => item.Corpus != PackCorpus.Calc).ToList()));
    }
}
