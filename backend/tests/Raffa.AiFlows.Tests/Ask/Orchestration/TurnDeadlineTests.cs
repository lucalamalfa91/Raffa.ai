using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiGateway.Telemetry;

namespace Raffa.AiFlows.Tests.Ask.Orchestration;

/// <summary>F1 Ask split PR-10 (A-04): token composition and the cancellation truth table of <see cref="TurnDeadline"/>.</summary>
public sealed class TurnDeadlineTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(60);

    private static async Task WaitUntilCancelledAsync(CancellationToken token)
    {
        for (var i = 0; i < 200 && !token.IsCancellationRequested; i++)
        {
            await Task.Delay(25);
        }
    }

    [Fact]
    public void With_no_cap_both_phase_tokens_are_the_callers_own_and_the_turn_is_inactive()
    {
        using var caller = new CancellationTokenSource();
        using var deadline = TurnDeadline.Start(new AskTurnOptions(), caller.Token);

        Assert.False(deadline.Active);
        Assert.Equal(caller.Token, deadline.ForAgents());
        Assert.Equal(caller.Token, deadline.ForAnswer());
        Assert.Equal("none", deadline.ExpiredPhase);
    }

    [Fact]
    public void A_null_options_object_is_no_cap()
    {
        using var deadline = TurnDeadline.Start(null, CancellationToken.None);

        Assert.False(deadline.Active);
    }

    [Fact]
    public async Task An_agent_cap_cancels_only_the_agent_token()
    {
        using var deadline = TurnDeadline.Start(new AskTurnOptions { AgentPhaseDeadline = Short }, CancellationToken.None);
        var agents = deadline.ForAgents();
        var answer = deadline.ForAnswer();

        await WaitUntilCancelledAsync(agents);

        Assert.True(agents.IsCancellationRequested);
        Assert.False(answer.IsCancellationRequested);
        Assert.True(deadline.Expired(agents));
        Assert.False(deadline.Expired(answer));
    }

    [Fact]
    public async Task An_answer_cap_cancels_only_the_answer_token()
    {
        using var deadline = TurnDeadline.Start(new AskTurnOptions { AnswerDeadline = Short }, CancellationToken.None);
        var agents = deadline.ForAgents();
        var answer = deadline.ForAnswer();

        await WaitUntilCancelledAsync(answer);

        Assert.True(answer.IsCancellationRequested);
        Assert.False(agents.IsCancellationRequested);
    }

    [Fact]
    public async Task The_turn_cap_cancels_both_phases()
    {
        using var deadline = TurnDeadline.Start(new AskTurnOptions { TurnDeadline = Short }, CancellationToken.None);
        var agents = deadline.ForAgents();
        var answer = deadline.ForAnswer();

        await WaitUntilCancelledAsync(agents);
        await WaitUntilCancelledAsync(answer);

        Assert.True(agents.IsCancellationRequested);
        Assert.True(answer.IsCancellationRequested);
        Assert.True(deadline.Expired(answer));
    }

    [Fact]
    public void The_callers_cancellation_reaches_the_phase_tokens_but_is_never_an_expiry()
    {
        using var caller = new CancellationTokenSource();
        using var deadline = TurnDeadline.Start(new AskTurnOptions { AgentPhaseDeadline = TimeSpan.FromMinutes(5) }, caller.Token);
        var agents = deadline.ForAgents();

        Assert.False(deadline.Expired(agents));
        caller.Cancel();

        Assert.True(agents.IsCancellationRequested);
        Assert.False(deadline.Expired(agents));
    }

    [Fact]
    public void The_first_cut_wins_and_a_turn_cap_that_already_fired_names_the_turn()
    {
        using var deadline = TurnDeadline.Start(new AskTurnOptions { AgentPhaseDeadline = TimeSpan.FromMinutes(5) }, CancellationToken.None);

        Assert.True(deadline.Mark("agents"));
        deadline.Mark("answer");

        Assert.Equal("agents", deadline.ExpiredPhase);
        Assert.StartsWith("deadline=agents turnMs=", deadline.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_fragment_is_only_written_when_a_cap_is_configured()
    {
        using var turn = RunContext.BeginTurn();
        using (var inactive = TurnDeadline.Start(new AskTurnOptions(), CancellationToken.None))
        {
            inactive.WriteDetail();
        }

        Assert.Null(RunContext.TurnDetail);

        using (var active = TurnDeadline.Start(new AskTurnOptions { AnswerDeadline = TimeSpan.FromMinutes(1) }, CancellationToken.None))
        {
            active.WriteDetail();
        }

        Assert.Matches(@"^deadline=none turnMs=\d+$", RunContext.TurnDetail);
    }

    [Fact]
    public void The_degraded_lead_says_the_gap_first_and_keeps_the_data_check_lead()
    {
        var lead = TurnDeadline.DegradedLead("The X contract has no end date on file.\n\n", italian: false);

        Assert.StartsWith(TurnDeadline.AgentsSkippedSentence(italian: false), lead, StringComparison.Ordinal);
        Assert.EndsWith("The X contract has no end date on file.\n\n", lead, StringComparison.Ordinal);
        Assert.Equal(TurnDeadline.AgentsSkippedSentence(italian: true) + "\n\n", TurnDeadline.DegradedLead(null, italian: true));
    }
}
