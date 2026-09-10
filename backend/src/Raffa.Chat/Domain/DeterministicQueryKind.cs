namespace Raffa.Chat.Domain;

/// <summary>
/// The concrete deterministic query families task E02/F04/US01/T02 (deterministic-queries)
/// implements for a <see cref="QueryIntent.Structured"/> question — product spec §8.3's two
/// structured example rows: "Which contracts renew in the next 120 days?" (dates) and "What is
/// our Microsoft annual spend?" (spend). Every <see cref="QueryIntent.Structured"/> question maps
/// to exactly one of these three outcomes; see
/// <c>Raffa.Chat.Application.DeterministicQueryPlanner</c> for the mapping rules and
/// <see cref="Unsupported"/> for why a third, non-answering outcome exists at all.
/// </summary>
public enum DeterministicQueryKind
{
    /// <summary>
    /// "Which contracts renew/expire in the next N days/months/years?" — a deterministic date
    /// filter over <c>Contract.AutoRenewal</c>/<c>Contract.EndDate</c> (Appendix C rule 6: prefer
    /// deterministic date calculations to LLM reasoning).
    /// </summary>
    RenewalWindow,

    /// <summary>
    /// "What is our annual spend [with &lt;supplier&gt;]?" — a deterministic sum of
    /// <c>Contract.AnnualSpend</c>, optionally scoped to one supplier (spec §8.3 "structured
    /// aggregation on supplier + contract values"). Scoping only happens when a caller already
    /// supplies a resolved supplier id — see
    /// <c>Raffa.Chat.Application.DeterministicQueryResult.SupplierScopeUnresolved</c> for the
    /// case where the question names a supplier this module cannot yet resolve on its own.
    /// </summary>
    AnnualSpend,

    /// <summary>
    /// The router (task E02/F04/US01/T01) classified the question as
    /// <see cref="QueryIntent.Structured"/> — it names a validated contract field, not clause
    /// content — but this task covers only <see cref="RenewalWindow"/> and
    /// <see cref="AnnualSpend"/> (task title: "Deterministic query handlers for dates/spend"). A
    /// question like "What is the total contract value for our SAP agreement?" lands here: it is
    /// honestly reported as not-yet-covered rather than silently answered against the wrong field
    /// (Appendix C rule 10 — uncertainty over fabricated precision), the same way
    /// <c>Raffa.Documents.Contracts.Application.PortfolioFilter</c> documents its own missing
    /// "category" filter instead of quietly ignoring it.
    /// </summary>
    Unsupported,
}
