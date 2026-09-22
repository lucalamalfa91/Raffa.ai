using Raffa.Chat.Domain;

namespace Raffa.Chat.Application.Planning;

/// <summary>
/// The outcome of <see cref="IntentPlanner.Plan"/> — which fixed <see cref="AskIntent"/> a
/// <see cref="GateLabel.InDomain"/> turn maps to, and the supplier name (if any) it should be
/// scoped to.
/// </summary>
/// <param name="Intent">The assigned intent.</param>
/// <param name="Reason">Human-readable trace of which keyword/rule matched — same debugging role
/// <see cref="Gate.DomainGateResult.Reason"/> plays for the gate.</param>
/// <param name="NamedSupplier">Echoes the gate's own resolved supplier name (see
/// <see cref="Gate.DomainGateResult.NamedSupplier"/>) unchanged — the planner never re-extracts
/// it, it only decides how to use it (scope the pack to this one supplier's contract(s), or leave
/// the intent portfolio-wide when null).</param>
/// <param name="Goal">The quantified saving goal the question carries (amount, percentage,
/// window), parsed by <see cref="SavingsGoalParser"/>; a goal with
/// <see cref="SavingsGoal.HasTarget"/> false for every question that quantifies nothing. Only the
/// savings-family intents (<see cref="AskIntent.Savings"/>,
/// <see cref="AskIntent.PortfolioSavingsTarget"/>, <see cref="AskIntent.RenewalStrategy"/>) read
/// it.</param>
/// <param name="Basis">How <see cref="Intent"/> was decided (see <see cref="IntentPlanBasis"/>) — the
/// structured signal the interview planner reads instead of parsing <see cref="Reason"/>.</param>
/// <param name="Candidates">Every intent whose lexicon matched this question, in planner order
/// (<see cref="IntentPlanner.Candidates"/>); <see cref="Intent"/> is the first of them unless the
/// plan was forced. Empty when nothing matched at all.</param>
public sealed record IntentPlanResult(
    AskIntent Intent,
    string Reason,
    string? NamedSupplier,
    SavingsGoal? Goal = null,
    IntentPlanBasis Basis = IntentPlanBasis.Lexicon,
    IReadOnlyList<AskIntent>? Candidates = null);

/// <summary>How an <see cref="IntentPlanResult"/> was reached.</summary>
public enum IntentPlanBasis
{
    /// <summary>One of <see cref="IntentPlanner"/>'s own lexicons matched.</summary>
    Lexicon,

    /// <summary>No lexicon matched; the legacy query router matched a structured or clause keyword.</summary>
    LegacyRouter,

    /// <summary>Nothing matched anywhere: the legacy router fell back to its own default. The
    /// planner has no evidence about what the user means.</summary>
    Fallback,

    /// <summary>A bare follow-up re-planned on the previous user question.</summary>
    FollowUp,

    /// <summary>Forced by a server-authored interview resolution; no lexicon was consulted.</summary>
    Forced,
}
