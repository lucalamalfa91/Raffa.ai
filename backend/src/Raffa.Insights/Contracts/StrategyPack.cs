using Raffa.SharedKernel;

namespace Raffa.Insights.Contracts;

/// <summary>
/// The outcome of <c>Raffa.Insights.Strategy.StrategyPackBuilder.Build</c> (task
/// E13/F07/US01/T01, insights-calculators; parent story us-01-insights AC-4). Sections in the
/// council-decided order (story's own "Council decisions carried into this story"): <b>When you
/// must move</b> → <b>Where you can push</b> → <b>Targets</b> → <b>Next steps</b>. The model (a
/// later Ask task) narrates this pack; every number in it is this builder's own deterministic
/// arithmetic (Appendix C rule 6) — never the model's.
/// </summary>
/// <param name="ContractId">Which contract this pack is for.</param>
/// <param name="WhenYouMustMove">Dates section — see <see cref="WhenYouMustMove"/>.</param>
/// <param name="WhereYouCanPush">Levers across every priced line on the contract, flattened in
/// <see cref="StrategyInputs.PricedLines"/> order — never empty when at least one priced line
/// exists (parent story AC-4).</param>
/// <param name="Targets">One <see cref="PricedLineTarget"/> per priced line, in
/// <see cref="StrategyInputs.PricedLines"/> order.</param>
/// <param name="NextSteps">The four tracker steps, in order (<c>raffa-v2/app.jsx</c>
/// <c>stepDefs</c>).</param>
/// <param name="OpenWeakFacts">Citation keys for every below-threshold critical fact
/// (<see cref="StrategyInputs.CriticalFacts"/> filtered the same way
/// <c>Raffa.Insights.Criticality.CriticalityScoreCalculator</c>'s own open-critical-facts
/// component filters — weak-confidence threshold 0.8) — empty when nothing is weak.</param>
public sealed record StrategyPack(
    EntityId ContractId,
    WhenYouMustMove WhenYouMustMove,
    IReadOnlyList<ContractNegotiationLever> WhereYouCanPush,
    IReadOnlyList<PricedLineTarget> Targets,
    IReadOnlyList<NextStep> NextSteps,
    IReadOnlyList<string> OpenWeakFacts);

/// <summary>
/// "When you must move" — the dates section (parent story AC-4: "a passed deadline is stated as
/// passed", never hidden). Echoes <see cref="StrategyInputs"/>'s own renewal/cancellation fields
/// verbatim — this builder never re-derives a date <c>Raffa.Renewals.Application.RenewalEngine</c>
/// (via the composition root) already computed.
/// </summary>
/// <param name="RenewalDate">Echoes <see cref="StrategyInputs.RenewalDate"/>.</param>
/// <param name="CancellationDeadline">Echoes <see cref="StrategyInputs.CancellationDeadline"/>.</param>
/// <param name="DaysLeft">The more urgent of <see cref="StrategyInputs.DaysUntilCancellationDeadline"/>
/// / <see cref="StrategyInputs.DaysUntilRenewal"/> (cancellation deadline preferred when known — the
/// date that actually forces a decision, mirrors
/// <c>Raffa.Renewals.Application.RenewalPipelineBuilder.DetermineRecommendation</c>'s own
/// preference) — <see langword="null"/> when neither is known.</param>
/// <param name="PassedDeadline"><see langword="true"/> exactly when <see cref="DaysLeft"/> is
/// negative — the relevant date already passed (AC-4's own "stated as passed, not hidden").</param>
/// <param name="Explanation">Human-readable trace of which date drove <see cref="DaysLeft"/> and
/// why, including the honest "no renewal/no date known" cases (Appendix C rule 10).</param>
public sealed record WhenYouMustMove(
    DateOnly? RenewalDate,
    DateOnly? CancellationDeadline,
    int? DaysLeft,
    bool PassedDeadline,
    string Explanation);

/// <summary>
/// "Targets" — one priced line's opening/range/walk-away (parent story AC-4: "opening / range /
/// walk-away per priced line when a band exists, else 'insufficient market data'"). Echoes
/// <c>Raffa.Insights.Contracts.PricedLineNegotiationResult</c>'s own numeric fields for this line —
/// <see cref="Description"/> identifies which line, since <see cref="StrategyPack.Targets"/> is a
/// flat list, not keyed by an id (a <c>Raffa.Benchmark.Contracts.PricedLine</c> carries no id of
/// its own — it is a normalized DTO, not a persisted row).
/// </summary>
public sealed record PricedLineTarget(
    string Description,
    decimal? OpeningTarget,
    decimal? AcceptableRangeLow,
    decimal? AcceptableRangeHigh,
    decimal? WalkAwayThreshold,
    string Explanation);

/// <summary>
/// "Next steps" — one of the four tracker steps mirroring the Contract 360 tracker
/// (<c>raffa-v2/app.jsx</c> <c>stepDefs</c>, verbatim order: Notify → Request revised pricing and
/// licence mix → Counter with the market benchmark → Sign, or send non-renewal notice).
/// </summary>
/// <param name="Label">The step's own label — the first step names
/// <see cref="StrategyInputs.SupplierName"/> when known, generic phrasing ("the supplier")
/// otherwise (Appendix C rule 10: never fabricate a name that was not resolved).</param>
/// <param name="DueHint">"this week" / "+10 days" / "+20 days" / "by &lt;cancellation deadline&gt;"
/// (or "by the cancellation deadline" when that date is unknown) — <c>app.jsx</c>'s own literal
/// hints, not a computed date (the prototype names these as relative hints, not absolute
/// dates).</param>
public sealed record NextStep(string Label, string DueHint);
