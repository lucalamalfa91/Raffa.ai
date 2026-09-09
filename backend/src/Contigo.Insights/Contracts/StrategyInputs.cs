using Contigo.Benchmark.Contracts;
using Contigo.SharedKernel;

namespace Contigo.Insights.Contracts;

/// <summary>
/// One contract's raw facts for <c>Contigo.Insights.Strategy.StrategyPackBuilder.Build</c> (task
/// E13/F07/US01/T01, insights-calculators; parent story us-01-insights AC-4; R-STR-01). Same
/// composition-root-only-can-build-this shape as <see cref="ContractCriticalityInputs"/> — see that
/// record's own doc comment for the ADR-002 reasoning.
/// </summary>
/// <param name="ContractId">Which contract this strategy is for — also this pack's citation-key
/// root (<see cref="InsightsCitationKeys.Fact"/>).</param>
/// <param name="SupplierName">The supplier's display name, for the "Notify &lt;supplier&gt; of
/// intent to renegotiate" next step (<c>contigo-v2/app.jsx</c> <c>stepDefs</c>); <see langword="null"/>
/// when no supplier name has been resolved yet (today: always — <c>Contract.SupplierId</c> is a
/// bare id, and Suppliers/Products has no name resolver wired to this composition; see
/// <c>Contigo.Api.InsightsEndpointExtensions</c>'s own doc comment) — the builder falls back to
/// generic phrasing rather than fabricating a name.</param>
/// <param name="RenewalDate">The deterministic renewal date (echoes
/// <c>Contigo.Renewals.Application.RenewalCalculationResult.RenewalDate</c>) — <see langword="null"/>
/// when it cannot be determined.</param>
/// <param name="CancellationDeadline">The deterministic cancellation deadline (echoes
/// <c>RenewalCalculationResult.CancellationDeadline</c>) — independently nullable of
/// <paramref name="RenewalDate"/> (a renewal date needs only an end date + auto-renewal; a
/// cancellation deadline additionally needs a notice period).</param>
/// <param name="DaysUntilRenewal">Signed, unclamped day count to <paramref name="RenewalDate"/> —
/// negative means it already passed (never hidden behind a floor of zero, Appendix C rule
/// 10).</param>
/// <param name="DaysUntilCancellationDeadline">Same rule, relative to
/// <paramref name="CancellationDeadline"/>.</param>
/// <param name="AutoRenewal">Whether this contract auto-renews — a false value here is a known,
/// determined fact ("no renewal date applies"), not a data gap (mirrors
/// <c>Contigo.Renewals.Domain.RenewalCalculationStatus.NoRenewal</c>'s own distinction).</param>
/// <param name="PricedLines">Every priced line on this contract — one
/// <c>Contigo.Insights.Contracts.PricedLineNegotiationResult</c> is computed per entry; empty when
/// the contract has none.</param>
/// <param name="CriticalFacts">This contract's tracked critical-fact confidences — the same list
/// <see cref="ContractCriticalityInputs.CriticalFacts"/> carries, reused here for
/// <c>StrategyPack.OpenWeakFacts</c> so both endpoints report the identical weak-fact set for the
/// same contract (parent story: "Ask, Contract 360 and Renewals show the same numbers").</param>
/// <param name="AsOfDate">"Today", for the quarter-end lever and the passed-deadline flag — the
/// caller's own <c>IClock</c>-derived date, never read from the system clock inside the builder, so
/// it stays pure and testable.</param>
public sealed record StrategyInputs(
    EntityId ContractId,
    string? SupplierName,
    DateOnly? RenewalDate,
    DateOnly? CancellationDeadline,
    int? DaysUntilRenewal,
    int? DaysUntilCancellationDeadline,
    bool AutoRenewal,
    IReadOnlyList<PricedLine> PricedLines,
    IReadOnlyList<CriticalFactConfidence> CriticalFacts,
    DateOnly AsOfDate);
