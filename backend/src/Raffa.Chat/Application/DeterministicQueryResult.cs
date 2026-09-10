using Raffa.Chat.Domain;
using Raffa.SharedKernel;

namespace Raffa.Chat.Application;

/// <summary>
/// The outcome of <see cref="DeterministicQueryHandler.Handle"/> — parent story
/// us-01-query-router AC-2 ("Structured questions hit deterministic queries/filters (no LLM)")
/// made concrete and testable: every field here is computed by a pure filter/aggregation over the
/// <see cref="ContractFact"/> list the caller supplied, never by a language model.
/// </summary>
/// <param name="Question">The question this result answers, unchanged from the routed decision.</param>
/// <param name="Kind">Which deterministic query family produced this result.</param>
/// <param name="MatchedContractIds">
/// The contracts the filter/aggregation matched — every contract summed into
/// <see cref="AggregateAnnualSpend"/> for <see cref="DeterministicQueryKind.AnnualSpend"/>, or
/// every contract renewing inside the window for <see cref="DeterministicQueryKind.RenewalWindow"/>.
/// Always empty for <see cref="DeterministicQueryKind.Unsupported"/>.
/// </param>
/// <param name="AggregateAnnualSpend">
/// The summed <c>Contract.AnnualSpend</c> for <see cref="DeterministicQueryKind.AnnualSpend"/>
/// (zero when zero contracts matched); null for every other <see cref="Kind"/> — "no aggregate
/// was computed" and "the matched contracts spent nothing" must stay distinguishable.
/// </param>
/// <param name="Explanation">Human-readable trace of what the handler did — not meant to be
/// shown to an end user as-is, but enough for a test (or a developer) to see *why* a result has
/// the shape it does without re-deriving it, the same role <see cref="QueryRouteDecision.Reason"/>
/// plays for routing.</param>
/// <param name="SupplierScopeUnresolved">
/// True only for <see cref="DeterministicQueryKind.AnnualSpend"/>, and only when the question
/// named a specific supplier (<see cref="DeterministicQuery.AnnualSpend.RequestedSupplierName"/>)
/// that could not be resolved to a <see cref="DeterministicQuery.AnnualSpend.SupplierId"/> —
/// meaning <see cref="AggregateAnnualSpend"/> is a company-wide total, not a total for the named
/// supplier, even though the question asked about one. A caller must not present the aggregate as
/// if it answered the named-supplier question when this is true (Appendix C rule 10: return
/// uncertainty, not fabricated precision, rather than silently answering a different question
/// than the one asked). Always false when no supplier was named, when a resolved
/// <c>SupplierId</c> was supplied, and for every <see cref="Kind"/> other than
/// <see cref="DeterministicQueryKind.AnnualSpend"/>.
/// </param>
public sealed record DeterministicQueryResult(
    string Question,
    DeterministicQueryKind Kind,
    IReadOnlyList<EntityId> MatchedContractIds,
    decimal? AggregateAnnualSpend,
    string Explanation,
    bool SupplierScopeUnresolved = false);
