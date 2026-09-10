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
public sealed record IntentPlanResult(AskIntent Intent, string Reason, string? NamedSupplier);
