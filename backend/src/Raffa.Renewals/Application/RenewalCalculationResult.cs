using Raffa.Renewals.Domain;
using Raffa.SharedKernel;

namespace Raffa.Renewals.Application;

/// <summary>
/// The outcome of <see cref="RenewalEngine.Calculate"/> — parent story us-01-deterministic-dates
/// AC-1/AC-2/AC-3 made concrete and testable: every date/day-count here is either pure arithmetic
/// over the <see cref="ContractRenewalTerms"/> the caller supplied, or an explicit null with
/// <see cref="Explanation"/> saying why (never a fabricated value — Appendix C rule 10).
/// </summary>
/// <param name="ContractId">Echoes <see cref="ContractRenewalTerms.ContractId"/> unchanged.</param>
/// <param name="Status">Which of the three outcomes this is — see
/// <see cref="RenewalCalculationStatus"/>'s own doc comments.</param>
/// <param name="RenewalDate">
/// Equal to <see cref="ContractRenewalTerms.EndDate"/> when <see cref="Status"/> is
/// <see cref="RenewalCalculationStatus.Determined"/> (the codebase's existing "renewal date"
/// convention — see <c>Raffa.Documents.Contracts.Application.PortfolioListItem.RenewalDate</c>'s
/// doc comment, which this engine reproduces on purpose for consistency); null for
/// <see cref="RenewalCalculationStatus.NoRenewal"/> and
/// <see cref="RenewalCalculationStatus.CannotDetermine"/>.
/// </param>
/// <param name="CancellationDeadline">
/// <see cref="ContractRenewalTerms.EndDate"/> minus <see cref="ContractRenewalTerms.CancellationNoticeDays"/>
/// (Appendix C rule 6 — deterministic arithmetic). Can be null even when <see cref="Status"/> is
/// <see cref="RenewalCalculationStatus.Determined"/>: a renewal date only needs
/// <see cref="ContractRenewalTerms.EndDate"/> and <see cref="ContractRenewalTerms.AutoRenewal"/>,
/// but a cancellation deadline additionally needs
/// <see cref="ContractRenewalTerms.CancellationNoticeDays"/> — when that is missing or negative
/// (invalid), this stays null and <see cref="Explanation"/> says which. "No aggregate was
/// computed" and "the renewal date has no deadline" must stay distinguishable the same way
/// <c>Raffa.Chat.Application.DeterministicQueryResult.AggregateAnnualSpend</c>'s own doc comment
/// already draws that line for a different field.
/// </param>
/// <param name="DaysUntilRenewal">
/// <see cref="RenewalDate"/> minus "today" (the engine's <c>IClock</c>), in days. Null unless
/// <see cref="RenewalDate"/> is set. Deliberately <em>not</em> clamped at zero: a negative value
/// means the renewal date already passed — an honest (if overdue) fact, not hidden behind a
/// fabricated floor (Appendix C rule 10). A caller that wants to treat overdue contracts specially
/// (a follow-up task's concern, not this one) can test the sign itself.
/// </param>
/// <param name="DaysUntilCancellationDeadline">Same rule as <see cref="DaysUntilRenewal"/>, but
/// relative to <see cref="CancellationDeadline"/>; null unless that is set.</param>
/// <param name="Explanation">Human-readable trace of what the engine computed and why — not meant
/// to be shown to an end user as-is, but enough for a test (or a developer) to see why a result
/// has the shape it does without re-deriving it, the same role
/// <c>Raffa.Chat.Application.DeterministicQueryResult.Explanation</c> plays there.</param>
public sealed record RenewalCalculationResult(
    EntityId ContractId,
    RenewalCalculationStatus Status,
    DateOnly? RenewalDate,
    DateOnly? CancellationDeadline,
    int? DaysUntilRenewal,
    int? DaysUntilCancellationDeadline,
    string Explanation);
