using Contigo.Chat.Domain;

namespace Contigo.Chat.Application.Gate;

/// <summary>
/// The outcome of <see cref="DomainGate.Classify"/> — which of the six <see cref="GateLabel"/>
/// values a turn was assigned, why, and (only for <see cref="GateLabel.NeedsDocument"/>) which
/// supplier name the question named but could not resolve.
/// </summary>
/// <param name="Label">The assigned label.</param>
/// <param name="Reason">Human-readable trace of which lexicon/rule fired — not shown to the end
/// user, but enough for a test/developer to see why without re-deriving it (same role
/// <see cref="QueryRouteDecision.Reason"/> already plays for the older query router).</param>
/// <param name="NamedSupplier">The capitalized candidate name the question appears to name,
/// whether or not it resolved — non-null whenever the deterministic supplier-name check found a
/// candidate at all (set for <see cref="GateLabel.NeedsDocument"/> and, when a candidate happened
/// to resolve, also for <see cref="GateLabel.InDomain"/>), so a caller does not have to
/// re-extract the same candidate a second time for the planner's own supplier-scoped intents.</param>
public sealed record DomainGateResult(GateLabel Label, string Reason, string? NamedSupplier = null);
