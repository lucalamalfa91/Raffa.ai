using Raffa.Chat.Application.Interview;
using Raffa.Chat.Application.Planning;

namespace Raffa.Chat.Tests.Interview;

/// <summary>
/// ADR-030 stage 1. The screenshot sentence ("Did you over all my contract?") must be ambiguous;
/// every question today's paths already answer well must stay clear, so the interview never gets
/// in the way of a tuned answer.
/// </summary>
public sealed class AmbiguityDetectorTests
{
    private readonly IntentPlanner _planner = new();
    private readonly InterviewOptions _options = new();

    private static InterviewContext Context(
        bool hasScope = false,
        int supplierContracts = 0,
        bool previousInterview = false,
        bool notice = false,
        bool empty = false) =>
        new(hasScope, supplierContracts, empty, previousInterview, notice);

    private AmbiguitySignals Detect(string question, string? supplier = null, InterviewContext? context = null) =>
        AmbiguityDetector.Detect(question, _planner.Plan(question, supplier), context ?? Context(), _options);

    [Theory]
    [InlineData("Did you over all my contract?")]
    [InlineData("Tell me everything")]
    [InlineData("Dammi una panoramica di tutti i miei contratti")]
    [InlineData("What about it?")]
    public void A_question_the_planner_has_no_reading_of_is_ambiguous(string question)
    {
        var signals = Detect(question);

        Assert.Equal(AmbiguityVerdict.Ambiguous, signals.Verdict);
        Assert.True(signals.PlannerFellThrough || signals.VagueOrDeictic);
        Assert.NotEmpty(signals.Names);
    }

    [Theory]
    [InlineData("What liabilities do we have?")]
    [InlineData("Which contracts renew in the next 120 days?")]
    [InlineData("Where can I save the most this quarter?")]
    [InlineData("Which contracts have uncapped liability?")]
    [InlineData("When does a contract expire?")]
    [InlineData("Which documents are not askable yet?")]
    [InlineData("Quali contratti sono mal posizionati rispetto al mercato e dove risparmiare?")]
    public void A_question_today_answers_well_is_never_ambiguous(string question)
    {
        var signals = Detect(question);

        Assert.NotEqual(AmbiguityVerdict.Ambiguous, signals.Verdict);
    }

    [Theory]
    [InlineData("How should I approach the Salesforce renewal?")]
    [InlineData("Tell me about Salesforce")]
    [InlineData("Salesforce?")]
    public void A_named_supplier_is_never_ambiguous_on_its_own(string question)
    {
        var signals = Detect(question, "Salesforce");

        Assert.Equal(AmbiguityVerdict.Clear, signals.Verdict);
    }

    [Fact]
    public void A_scoped_conversation_is_never_ambiguous()
    {
        var signals = Detect("Did you over all my contract?", null, Context(hasScope: true));

        Assert.NotEqual(AmbiguityVerdict.Ambiguous, signals.Verdict);
    }

    [Fact]
    public void A_short_unscoped_question_is_only_unsure()
    {
        var signals = Detect("Quanto spendiamo?");

        Assert.Equal(AmbiguityVerdict.Unsure, signals.Verdict);
        Assert.True(signals.ShortAndUnscoped);
    }

    [Fact]
    public void Two_readings_of_one_sentence_are_only_unsure_so_the_planner_keeps_deciding()
    {
        var signals = Detect("Where can we save and how do we compare with the market?");

        Assert.Equal(AmbiguityVerdict.Unsure, signals.Verdict);
        Assert.Equal(2, signals.MultiIntent.Count);
    }

    [Fact]
    public void A_supplier_with_several_contracts_is_ambiguous_until_scoped()
    {
        var unscoped = Detect("When does Salesforce expire?", "Salesforce", Context(supplierContracts: 3));
        var scoped = Detect("When does Salesforce expire?", "Salesforce", Context(hasScope: true, supplierContracts: 3));

        Assert.Equal(AmbiguityVerdict.Ambiguous, unscoped.Verdict);
        Assert.Equal(3, unscoped.SupplierContractCount);
        Assert.Contains("supplier-has-several-contracts", unscoped.Names);
        Assert.Equal(AmbiguityVerdict.Clear, scoped.Verdict);
    }

    [Fact]
    public void An_unscoped_notice_question_is_ambiguous()
    {
        var signals = Detect("When must we give notice?", null, Context(notice: true));

        Assert.Equal(AmbiguityVerdict.Ambiguous, signals.Verdict);
        Assert.True(signals.UnscopedNotice);
    }

    [Fact]
    public void Never_two_interviews_in_a_row_and_never_on_an_empty_portfolio()
    {
        Assert.Equal(AmbiguityVerdict.Clear, Detect("Did you over all my contract?", null, Context(previousInterview: true)).Verdict);
        Assert.Equal(AmbiguityVerdict.Clear, Detect("Did you over all my contract?", null, Context(empty: true)).Verdict);
    }

    [Fact]
    public void The_which_contract_and_notice_questions_can_be_switched_off()
    {
        var options = new InterviewOptions { AskWhichContract = false, AskOnUnscopedNotice = false };
        var plan = _planner.Plan("When must we give notice to Salesforce?", "Salesforce");

        var signals = AmbiguityDetector.Detect(
            "When must we give notice to Salesforce?", plan, Context(supplierContracts: 3, notice: true), options);

        Assert.Equal(AmbiguityVerdict.Clear, signals.Verdict);
    }
}

public sealed class AmbiguousAbstainDetectorTests
{
    [Theory]
    [InlineData("I can't reliably determine what you mean by 'over all my contract' from the context pack. Your question is ambiguous, so I should not guess.")]
    [InlineData("The question is unclear: it could mean the highest spend or the number of contracts.")]
    [InlineData("Non è chiaro cosa intendi con 'tutti i contratti'.")]
    [InlineData("La domanda è troppo vaga.")]
    public void An_abstain_reason_that_says_ambiguous_is_recognised(string reason)
    {
        Assert.True(AmbiguousAbstainDetector.IsAmbiguous(reason));
    }

    [Theory]
    [InlineData("Nothing in the validated contracts supports a reliable answer.")]
    [InlineData("The pack has no market band for this line.")]
    [InlineData("")]
    [InlineData(null)]
    public void Any_other_abstain_reason_is_not(string? reason)
    {
        Assert.False(AmbiguousAbstainDetector.IsAmbiguous(reason));
    }
}
