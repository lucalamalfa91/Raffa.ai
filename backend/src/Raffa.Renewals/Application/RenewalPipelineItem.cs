using Raffa.Renewals.Domain;
using Raffa.SharedKernel;

namespace Raffa.Renewals.Application;

/// <summary>
/// One row of the <c>GET /api/renewals</c> pipeline (task E03/F03/US01/T01,
/// us-01-renewal-dashboard-api AC-1: "returns pipeline (supplier/renewal/days/spend/deadline/
/// action)"; product spec §10.1 "Upcoming Renewals: Actionable renewal pipeline"). Built by
/// <see cref="RenewalPipelineBuilder.Build"/> from a <see cref="RenewalDashboardCandidate"/> plus
/// <see cref="RenewalEngine"/>'s deterministic calculation.
///
/// <see cref="Status"/>/<see cref="RenewalDate"/>/<see cref="DaysUntilRenewal"/> come straight from
/// <see cref="RenewalCalculationResult"/> — never recomputed ad hoc, so this row and
/// <c>Raffa.Renewals.Tests.RenewalEngineTests</c> agree by construction.
/// <see cref="CancellationDeadline"/>/<see cref="DaysUntilCancellationDeadline"/> are independent
/// of that calculation (see <see cref="RenewalDashboardCandidate.CancellationDeadline"/>'s own doc
/// comment for why) and can be present even when <see cref="Status"/> is
/// <see cref="RenewalCalculationStatus.CannotDetermine"/> — an already-known fact is never
/// suppressed just because a *different* value could not be derived (Appendix C rule 10; same
/// "independently determinable" rule <see cref="RenewalCalculationResult.CancellationDeadline"/>
/// already documents for its own, narrower case).
/// </summary>
public sealed record RenewalPipelineItem(
    EntityId ContractId,
    EntityId? SupplierId,
    RenewalCalculationStatus Status,
    DateOnly? RenewalDate,
    int? DaysUntilRenewal,
    decimal? AnnualSpend,
    DateOnly? CancellationDeadline,
    int? DaysUntilCancellationDeadline,
    bool AutoRenewal,
    RenewalInsightCard InsightCard);

/// <summary>
/// The renewal insight card (task E03/F03/US01/T01 AC-2: "Insight card separates facts from
/// recommendations"; product spec §9.3). <see cref="Facts"/> holds only values this codebase
/// actually knows (extracted data or deterministic arithmetic over it); <see cref="Recommendations"/>
/// holds derived/suggested values — never mixed into one flat bag, so a UI (or a caller) can always
/// tell "what we know" apart from "what we suggest" without inspecting field names one by one.
/// </summary>
public sealed record RenewalInsightCard(RenewalInsightFacts Facts, RenewalInsightRecommendations Recommendations);

/// <summary>
/// The insight card's facts group (spec §9.3 rows "Supplier / renewal", "Annual spend",
/// "Cancellation deadline") — every value here is either an extracted fact
/// (<see cref="SupplierId"/>, <see cref="AnnualSpend"/>, <see cref="CancellationDeadline"/>) or
/// deterministic arithmetic over one (<see cref="RenewalDate"/>, <see cref="DaysUntilRenewal"/>,
/// <see cref="DaysUntilCancellationDeadline"/> — Appendix C rule 6, never an LLM guess).
/// </summary>
public sealed record RenewalInsightFacts(
    EntityId? SupplierId,
    DateOnly? RenewalDate,
    int? DaysUntilRenewal,
    decimal? AnnualSpend,
    DateOnly? CancellationDeadline,
    int? DaysUntilCancellationDeadline);

/// <summary>
/// The insight card's recommendations group (spec §9.3 rows "Annual uplift", "Market position",
/// "Potential savings", "Recommended action"). <see cref="RecommendedAction"/>/
/// <see cref="Explanation"/> are real, computed by <see cref="RenewalPipelineBuilder"/>'s own
/// deterministic urgency rule (Appendix C rule 6 — code, not an LLM). <see cref="AnnualUpliftPercent"/>/
/// <see cref="MarketPosition"/>/<see cref="PotentialSavingsRange"/> are always <see langword="null"/>
/// in this wave: they need the Benchmark Service (<c>Raffa.Benchmark</c>, "IBenchmarkService only
/// — fixture adapter is later (R3)" per <c>backend/README.md</c>'s solution layout) and the Savings
/// module (<c>Raffa.Savings</c>, scaffold, R3), neither wired to this task's own dependency
/// (<c>renewal-engine</c> only). Present as explicit <see langword="null"/> fields rather than
/// omitted or fabricated — the same "honestly absent, not guessed" convention
/// <c>Contract360Result.Benchmark</c>/<c>.Activity</c> already use for their own R3/R4 gaps
/// (Appendix C rule 10) — so a caller can tell "not known yet" apart from "known to be zero" and
/// the wire shape does not have to change again once a later task fills them in.
/// </summary>
public sealed record RenewalInsightRecommendations(
    string RecommendedAction,
    string Explanation,
    decimal? AnnualUpliftPercent,
    string? MarketPosition,
    string? PotentialSavingsRange);
