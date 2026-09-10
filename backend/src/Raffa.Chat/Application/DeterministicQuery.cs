using Raffa.SharedKernel;

namespace Raffa.Chat.Application;

/// <summary>
/// The deterministic query plan <see cref="DeterministicQueryPlanner"/> derives from a
/// <see cref="QueryRouteDecision"/> whose <see cref="Domain.QueryIntent"/> is
/// <see cref="Domain.QueryIntent.Structured"/>. A closed set of three cases (one per
/// <see cref="Domain.DeterministicQueryKind"/> member) so <see cref="DeterministicQueryHandler"/>
/// can pattern-match exhaustively — see that type's <c>Handle</c> method.
/// </summary>
/// <param name="Question">The routed question this plan answers, unchanged from
/// <see cref="QueryRouteDecision.Question"/>.</param>
public abstract record DeterministicQuery(string Question)
{
    /// <summary>"Which contracts renew/expire in the next N days/months/years?"</summary>
    /// <param name="Days">The window size in days — months/years are already normalized to days
    /// by <see cref="DeterministicQueryPlanner"/>, so <see cref="DeterministicQueryHandler"/>
    /// never has to know the original unit.</param>
    public sealed record RenewalWindow(string Question, int Days) : DeterministicQuery(Question);

    /// <summary>"What is our annual spend [with a given supplier]?"</summary>
    /// <param name="SupplierId">Null when no supplier filter could be resolved — see
    /// <see cref="DeterministicQueryPlanner"/>'s doc comment for why free text alone cannot
    /// resolve one today. A caller that already has a resolved id (a future supplier-name
    /// lookup, or a chat session already narrowed to one supplier) can set this directly;
    /// <see cref="DeterministicQueryPlanner"/> itself never does.</param>
    /// <param name="RequestedSupplierName">Non-null when the question text appears to name a
    /// specific supplier (for example "Microsoft", "Acme Corp") that <paramref name="SupplierId"/>
    /// could not be resolved to — see <see cref="DeterministicQueryPlanner"/>. Distinct from "no
    /// supplier requested at all" (both this and <paramref name="SupplierId"/> null): that case is
    /// honestly company-wide, this one is not. <see cref="DeterministicQueryHandler"/> turns this
    /// into <see cref="DeterministicQueryResult.SupplierScopeUnresolved"/> so a caller must notice
    /// before presenting the aggregate as if it answered the named-supplier question (Appendix C
    /// rule 10).</param>
    public sealed record AnnualSpend(string Question, EntityId? SupplierId, string? RequestedSupplierName = null)
        : DeterministicQuery(Question);

    /// <summary>A <see cref="Domain.QueryIntent.Structured"/> question this task does not cover
    /// yet — see <see cref="Domain.DeterministicQueryKind.Unsupported"/> for why this case
    /// exists.</summary>
    /// <param name="Reason">Caller-facing explanation of the gap (never a fabricated
    /// answer — Appendix C rule 10).</param>
    public sealed record Unsupported(string Question, string Reason) : DeterministicQuery(Question);
}
