namespace Raffa.AiFlows.Ask.Orchestration;

/// <summary>
/// <c>Chat:AskTurn</c> — the switches of the Ask turn's behaviour changes (F1 Ask split). Every
/// default reproduces today's behaviour: a host that never sets the section gets exactly the turn it
/// had before. The properties below are consumed by the PRs that introduce each behaviour (PR-9
/// budget before the agents, PR-10 deadlines, PR-11 memoized reads); until then they have no effect.
/// </summary>
public sealed class AskTurnOptions
{
    public const string SectionName = "Chat:AskTurn";

    /// <summary>A-04: cap on the whole turn. <see langword="null"/> = no cap (today).</summary>
    public TimeSpan? TurnDeadline { get; set; }

    /// <summary>A-04: cap on the agent phase. <see langword="null"/> = today.</summary>
    public TimeSpan? AgentPhaseDeadline { get; set; }

    /// <summary>A-04: cap on the answer phase. <see langword="null"/> = today.</summary>
    public TimeSpan? AnswerDeadline { get; set; }

    /// <summary>F1-T03: bound the intent pack before the agents read it. <see langword="false"/> = today.</summary>
    public bool BudgetBeforeAgents { get; set; }

    /// <summary>F1-T03: tokens kept free for the agents' own items when the budget runs first. 0 = today.</summary>
    public int AgentPackReserveTokens { get; set; }

    /// <summary>F1-T03: drop a council play whose cited items the budget cut. <see langword="false"/> = today.</summary>
    public bool RevalidatePlaysAfterBudget { get; set; }

    /// <summary>F1-T01: read each contract once per turn. <see langword="false"/> = today.</summary>
    public bool MemoizeTurnReads { get; set; }

    /// <summary>A-05: the answer role's calls run as runner steps (<c>answer-composer</c>), audited with the
    /// step name. The request to the gateway and the guard policy are unchanged. <see langword="false"/> = today.</summary>
    public bool AnswerOnRunner { get; set; }

    /// <summary>A-05: backstop deadline of one answer-composer step when <see cref="AnswerOnRunner"/> is on;
    /// far above the gateway's own timeout, so it bounds a hung call and changes no normal turn.</summary>
    public TimeSpan AnswerStepDeadline { get; set; } = TimeSpan.FromMinutes(10);
}
