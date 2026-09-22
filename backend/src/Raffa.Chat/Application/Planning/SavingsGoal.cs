namespace Raffa.Chat.Application.Planning;

/// <summary>
/// The quantified goal a savings question carries, when it carries one — "risparmiare 20k sul
/// rinnovo", "salvare 40K sul prossimo quarterly", "cut 15% next year". Parsed deterministically by
/// <see cref="SavingsGoalParser"/> and echoed through <see cref="IntentPlanResult.Goal"/> so the
/// composition root can hand it to the savings calculators (target amount, window) without ever
/// asking a model to read the number back out of the question.
/// </summary>
/// <param name="TargetAmount">The absolute amount asked for, normalized (20k → 20000), or null
/// when only a percentage or only a window was given.</param>
/// <param name="TargetPercent">The percentage asked for ("cut 15%"), or null.</param>
/// <param name="Currency">The ISO code when the question named one (€/EUR/USD/CHF/GBP), else
/// null — the calculators fall back to the contract's own currency.</param>
/// <param name="WindowDays">The time window asked for, in days from today: a quarter is 90, a
/// month 30, a year 365, "entro N giorni" N; null when no window was named.</param>
/// <param name="WindowLabel">The window as the user phrased it ("quarterly", "trimestre"), for
/// the answer to echo; null when none.</param>
public sealed record SavingsGoal(
    decimal? TargetAmount,
    decimal? TargetPercent,
    string? Currency,
    int? WindowDays,
    string? WindowLabel)
{
    /// <summary>True when the question quantified anything at all — an amount, a percentage or a
    /// window. A savings question with no goal stays the portfolio-wide "where can we save"
    /// (<see cref="Domain.AskIntent.PortfolioStrategy"/>).</summary>
    public bool HasTarget => TargetAmount is not null || TargetPercent is not null || WindowDays is not null;
}
