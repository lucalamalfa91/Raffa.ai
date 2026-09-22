using System.Text.RegularExpressions;
using Raffa.Chat.Application.Interview;
using Raffa.Chat.Application.Planning;
using Raffa.Chat.Domain;

namespace Raffa.Chat.Tests.Interview;

/// <summary>
/// ADR-030. The key invariant: every option's rewritten question re-plans deterministically to the
/// option's own intent (never the planner's fallback), so an interview answer always lands on a
/// tuned path. Labels never carry a guid (R-ASK-08).
/// </summary>
public sealed class InterviewPlannerTests
{
    private static readonly Regex GuidPattern = new(
        "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase);

    private readonly IntentPlanner _intentPlanner = new();
    private readonly InterviewOptions _options = new();
    private readonly InterviewPlanner _planner;

    public InterviewPlannerTests()
    {
        _planner = new InterviewPlanner(_options);
    }

    private (IntentPlanResult Plan, AmbiguitySignals Signals) Analyse(
        string question, string? supplier = null, InterviewContext? context = null)
    {
        var plan = _intentPlanner.Plan(question, supplier);
        var signals = AmbiguityDetector.Detect(
            question, plan, context ?? new InterviewContext(false, 0, false, false, false), _options);
        return (plan, signals);
    }

    private static InterviewContractChoice Contract(string id, string supplier, string type, string? deadline = null, string? renewal = null) =>
        new(
            id, supplier, type,
            renewal is null ? null : DateOnly.Parse(renewal),
            deadline is null ? null : DateOnly.Parse(deadline),
            null);

    [Fact]
    public void The_screenshot_question_gets_the_interpretation_menu()
    {
        var (plan, signals) = Analyse("Did you over all my contract?");

        var turn = _planner.Plan("Did you over all my contract?", plan, signals, InterviewInputs.Empty);

        Assert.NotNull(turn);
        var question = Assert.Single(turn.Questions);
        Assert.Equal(InterviewPlanner.InterpretationKey, question.Key);
        Assert.Equal(InterviewPresentation.Choice, question.Presentation);
        Assert.True(question.AllowFreeText);
        Assert.Contains(question.Options, o => o.Key == "portfolio-overview");
        Assert.Contains(question.Options, o => o.Key == "total-spend");
        Assert.Contains(question.Options, o => o.Key == "renewals-window");
        Assert.DoesNotContain(question.Options, o => o.Key == InterviewPlanner.WebResearchOptionKey);
        Assert.StartsWith("Before I answer", turn.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void An_italian_question_gets_italian_prompts_and_labels()
    {
        const string question = "Dammi una panoramica di tutti i miei contratti";
        var (plan, signals) = Analyse(question);

        var turn = _planner.Plan(question, plan, signals, InterviewInputs.Empty);

        Assert.NotNull(turn);
        Assert.StartsWith("Prima di rispondere", turn.Prompt, StringComparison.Ordinal);
        Assert.All(turn.Questions[0].Options, o => Assert.DoesNotContain("Which", o.Label, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Did you over all my contract?")]
    [InlineData("Dammi una panoramica di tutti i miei contratti")]
    [InlineData("Where can we save and how do we compare with the market?")]
    public void Every_rewritten_question_re_plans_to_its_own_intent_never_the_fallback(string question)
    {
        var plan = _intentPlanner.Plan(question, null);

        var turn = _planner.PlanInterpretationMenu(question, plan);

        Assert.NotNull(turn);
        Assert.All(turn.Questions[0].Options, option =>
        {
            var replanned = _intentPlanner.Plan(option.ResolvesTo.RewrittenQuestion, null);
            Assert.Equal(option.ResolvesTo.Intent, replanned.Intent);
            Assert.NotEqual(IntentPlanBasis.Fallback, replanned.Basis);
        });
    }

    [Fact]
    public void Two_readings_become_two_options_in_planner_order()
    {
        const string question = "Where can we save and how do we compare with the market?";
        var plan = _intentPlanner.Plan(question, null);

        var turn = _planner.PlanInterpretationMenu(question, plan);

        Assert.NotNull(turn);
        Assert.Equal(["portfolio-overview", "market-position"], turn.Questions[0].Options.Select(o => o.Key).ToList());
    }

    [Fact]
    public void A_supplier_with_several_contracts_is_asked_which_one_without_a_guid_in_sight()
    {
        const string question = "When does Salesforce expire?";
        var (plan, signals) = Analyse(question, "Salesforce", new InterviewContext(false, 3, false, false, false));
        var contracts = new[]
        {
            Contract("11111111-1111-1111-1111-111111111111", "Salesforce", "Msa", deadline: "2027-03-01"),
            Contract("22222222-2222-2222-2222-222222222222", "Salesforce", "OrderForm", deadline: "2026-11-15"),
            Contract("33333333-3333-3333-3333-333333333333", "Salesforce", "Sow", renewal: "2028-01-01"),
        };

        var turn = _planner.Plan(question, plan, signals, new InterviewInputs(contracts, []));

        Assert.NotNull(turn);
        var which = Assert.Single(turn.Questions);
        Assert.Equal(InterviewPlanner.WhichContractKey, which.Key);
        Assert.Equal("Which Salesforce contract do you mean?", which.Prompt);
        Assert.Equal(4, which.Options.Count);
        Assert.All(which.Options, o => Assert.DoesNotMatch(GuidPattern, o.Label));
        Assert.All(which.Options, o => Assert.Equal(question, o.ResolvesTo.RewrittenQuestion));
        Assert.All(which.Options, o => Assert.Equal(plan.Intent, o.ResolvesTo.Intent));

        // Soonest deadline first; the "soonest" shortcut resolves to that same contract.
        Assert.Equal("22222222-2222-2222-2222-222222222222", which.Options[0].ResolvesTo.ContractId);
        Assert.StartsWith("Salesforce · OrderForm (notice by 2026-11-15)", which.Options[0].Label, StringComparison.Ordinal);
        var soonest = which.Options.Single(o => o.Key == InterviewPlanner.SoonestOptionKey);
        Assert.Equal("22222222-2222-2222-2222-222222222222", soonest.ResolvesTo.ContractId);
    }

    [Fact]
    public void An_unscoped_notice_question_is_asked_which_contract_with_the_deadlines_shown()
    {
        const string question = "When must we give notice?";
        var (plan, signals) = Analyse(question, null, new InterviewContext(false, 0, false, false, true));
        var candidates = new[]
        {
            Contract("11111111-1111-1111-1111-111111111111", "Allianz", "Msa", deadline: "2027-03-01"),
            Contract("22222222-2222-2222-2222-222222222222", "Microsoft", "OrderForm", deadline: "2026-11-15"),
        };

        var turn = _planner.Plan(question, plan, signals, new InterviewInputs([], candidates));

        Assert.NotNull(turn);
        var notice = Assert.Single(turn.Questions);
        Assert.Equal(InterviewPlanner.NoticeContractKey, notice.Key);
        Assert.Equal("Microsoft · OrderForm (notice by 2026-11-15)", notice.Options[0].Label);
        Assert.Equal(AskIntent.StructuredFact, notice.Options[0].ResolvesTo.Intent);
        Assert.Equal("22222222-2222-2222-2222-222222222222", notice.Options[0].ResolvesTo.ContractId);

        var all = notice.Options.Last();
        Assert.Equal("all-renewals", all.Key);
        Assert.Equal(AskIntent.StructuredFact, _intentPlanner.Plan(all.ResolvesTo.RewrittenQuestion, null).Intent);
    }

    [Fact]
    public void A_clear_verdict_or_a_disabled_interview_plans_nothing()
    {
        var (plan, signals) = Analyse("Which contracts renew in the next 120 days?");
        Assert.Null(_planner.Plan("Which contracts renew in the next 120 days?", plan, signals, InterviewInputs.Empty));

        var disabled = new InterviewPlanner(new InterviewOptions { Enabled = false });
        var (ambiguousPlan, ambiguousSignals) = Analyse("Did you over all my contract?");
        Assert.Null(disabled.Plan("Did you over all my contract?", ambiguousPlan, ambiguousSignals, InterviewInputs.Empty));
        Assert.Null(disabled.PlanInterpretationMenu("Did you over all my contract?", ambiguousPlan));
    }

    [Fact]
    public void A_web_offer_is_the_last_option_and_carries_the_server_authored_query()
    {
        var (plan, signals) = Analyse("Did you over all my contract?");
        var offer = new WebResearchRequest("saas renewal market practice", "MarketPractice");

        var turn = _planner.Plan("Did you over all my contract?", plan, signals, InterviewInputs.Empty, offer);

        Assert.NotNull(turn);
        var web = turn.Questions[0].Options.Last();
        Assert.Equal(InterviewPlanner.WebResearchOptionKey, web.Key);
        Assert.Equal(offer, web.ResolvesTo.WebResearch);
        Assert.Null(web.ResolvesTo.Intent);
        Assert.NotNull(web.Hint);
    }

    [Fact]
    public void Options_are_capped_by_configuration()
    {
        var capped = new InterviewPlanner(new InterviewOptions { MaxOptionsPerQuestion = 2 });
        var plan = _intentPlanner.Plan("Did you over all my contract?", null);

        var turn = capped.PlanInterpretationMenu("Did you over all my contract?", plan);

        Assert.NotNull(turn);
        Assert.Equal(2, turn.Questions[0].Options.Count);
    }
}

public sealed class LanguageHintTests
{
    [Theory]
    [InlineData("Quali contratti scadono nei prossimi mesi?", true)]
    [InlineData("Dammi una panoramica di tutti i miei contratti", true)]
    [InlineData("Did you over all my contract?", false)]
    [InlineData("Which contracts renew in the next 120 days?", false)]
    public void Two_italian_markers_make_a_question_italian(string question, bool italian)
    {
        Assert.Equal(italian, LanguageHint.IsItalian(question));
    }
}
