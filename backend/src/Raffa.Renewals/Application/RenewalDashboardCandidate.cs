using Raffa.SharedKernel;

namespace Raffa.Renewals.Application;

/// <summary>
/// The per-contract facts <see cref="RenewalPipelineBuilder"/> needs to build one
/// <c>GET /api/renewals</c> pipeline row (task E03/F03/US01/T01, us-01-renewal-dashboard-api
/// AC-1/AC-2; product spec §9.3/§10.1). Same shape decision as <see cref="ContractRenewalTerms"/>
/// and <c>Raffa.Chat.Application.ContractFact</c>: a small DTO, not the real
/// <c>Raffa.Documents.Contracts.Domain.Contract</c> — ADR-002 forbids <c>Raffa.Renewals</c>
/// from referencing <c>Raffa.Documents.Contracts</c> at all
/// (<c>Raffa.ArchitectureTests.DependencyDirectionTests</c>'s allow-list for this module is
/// exactly <c>[SharedKernel, Benchmark]</c>). <c>Raffa.Api.RenewalsEndpointExtensions</c> — "the
/// one project allowed to reference every module" (<c>backend/README.md</c> "Dependency
/// direction") — maps a real, tenant-scoped
/// <c>Raffa.Documents.Contracts.Application.PortfolioListItem</c> row onto this DTO 1:1.
/// </summary>
/// <param name="ContractId">The source <c>Contract.Id</c> — echoed back on
/// <see cref="RenewalPipelineItem"/> so a caller processing many contracts can correlate a result
/// to its input.</param>
/// <param name="SupplierId">The source <c>Contract.SupplierId</c>. Null passes straight through —
/// same honest "no supplier-name resolution yet" gap <c>PortfolioListItem.SupplierId</c> already
/// documents (Suppliers/Products is still an empty scaffold).</param>
/// <param name="EndDate">The source <c>Contract.EndDate</c>, forwarded to
/// <see cref="RenewalEngine"/> (via <see cref="ContractRenewalTerms"/>) to compute
/// <see cref="RenewalCalculationResult.RenewalDate"/>/<see cref="RenewalCalculationResult.DaysUntilRenewal"/>.</param>
/// <param name="AutoRenewal">The source <c>Contract.AutoRenewal</c>, forwarded the same way.</param>
/// <param name="AnnualSpend">The source <c>Contract.AnnualSpend</c> — a fact, carried through
/// unchanged onto both the pipeline row and the insight card's <c>Facts</c> group (spec §9.3
/// "Annual spend").</param>
/// <param name="CancellationDeadline">
/// The source <c>Contract.CancellationDeadline</c> — an already-extracted raw fact, independent of
/// <see cref="RenewalEngine"/>'s own notice-day derivation. Deliberately carried through as-is
/// rather than recomputed: <c>Contract</c> has no persisted <c>CancellationNoticeDays</c> column
/// today (see <see cref="ContractRenewalTerms.CancellationNoticeDays"/>'s own doc comment), so
/// <see cref="RenewalEngine"/> cannot derive this value — but the extraction pipeline already wrote
/// it directly onto <c>Contract</c>, and dropping a known fact because the engine cannot
/// independently re-derive it would be exactly the kind of information loss Appendix C rule 10
/// warns against. <see cref="RenewalPipelineBuilder"/> computes "days until" this date itself
/// (plain <c>DateOnly</c> arithmetic against the same <c>IClock</c> — Appendix C rule 6), since
/// <see cref="RenewalEngine"/> exposes no method that accepts an already-known deadline.
/// </param>
/// <param name="MarketBand">
/// The resolved market position band, or <see langword="null"/> when the key was incomplete or
/// the benchmark adapter abstained (insufficient comparables). Resolved by
/// <c>Raffa.Api.RenewalsEndpointExtensions</c> before <see cref="RenewalPipelineBuilder.Build"/>
/// so the builder stays pure (ADR-002 w17 clause 2): it never looks up a benchmark itself.
/// <see cref="RenewalPipelineBuilder"/> formats this into
/// <see cref="RenewalInsightRecommendations.MarketPosition"/> — either a representative position
/// string or the explicit <c>"insufficient market data"</c> abstention (AC-3, ADR-001 w17 clause 4).
/// </param>
public sealed record RenewalDashboardCandidate(
    EntityId ContractId,
    EntityId? SupplierId,
    DateOnly? EndDate,
    bool AutoRenewal,
    decimal? AnnualSpend,
    DateOnly? CancellationDeadline,
    ResolvedMarketBand? MarketBand = null);

/// <summary>
/// A resolved market position band carried on <see cref="RenewalDashboardCandidate"/> and
/// consumed by <see cref="RenewalPipelineBuilder"/> to fill
/// <see cref="RenewalInsightRecommendations.MarketPosition"/>.
///
/// <para>
/// Resolved in the host (<c>Raffa.Api</c>) against the Benchmark Service and comparing
/// <see cref="RenewalDashboardCandidate.AnnualSpend"/> to the distribution, then passed in as a
/// pure value so the builder stays synchronous and dependency-free (ADR-002 w17 clause 2,
/// ADR-001 w17 clause 4). The <c>"representative"</c> label is applied by the builder when it
/// formats the final string — a fixture or market-feed figure is never presented as live market
/// truth (ADR-001).
/// </para>
/// </summary>
/// <param name="Position">One of <c>"above market"</c>, <c>"in line with market"</c>,
/// or <c>"below market"</c> — derived from AnnualSpend vs. the P25/P75 band.</param>
/// <param name="Adapter">The benchmark adapter's own source identifier (e.g. <c>"fixture"</c>,
/// <c>"market-feed"</c>) — surfaced so a reader can evaluate the provenance.</param>
/// <param name="SampleSize">The number of comparables behind the result, when the adapter reports
/// one; <see langword="null"/> when the adapter did not supply a count.</param>
/// <param name="AsOf">When the underlying comparable data was last refreshed.</param>
public sealed record ResolvedMarketBand(
    string Position,
    string Adapter,
    int? SampleSize,
    DateTimeOffset AsOf);
