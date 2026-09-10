using Raffa.Renewals.Domain;
using Raffa.SharedKernel;

namespace Raffa.Renewals.Application;

/// <summary>
/// The deterministic renewal-date / cancellation-deadline calculator (task E03/F01/US01/T01, the
/// wave-spec's <c>renewal-engine</c> artifact; parent story us-01-deterministic-dates). Pure and
/// synchronous: no database call, no HTTP call, no LLM call anywhere in
/// <see cref="Calculate"/> or <see cref="CalculateMany"/>, so the same
/// <see cref="ContractRenewalTerms"/> plus the same "now" always produce the same
/// <see cref="RenewalCalculationResult"/> (Appendix C rule 6 — "prefer deterministic
/// arithmetic/date calculations to LLM reasoning" — and product spec §9.1 "Renewal generation":
/// "calculate renewal date, calculate cancellation deadline, calculate days remaining"). Takes
/// <see cref="IClock"/> (not <see cref="DateTimeOffset.UtcNow"/> directly) for "today", the same
/// determinism convention every other date-sensitive service in this solution already follows
/// (for example <c>Raffa.Chat.Application.DeterministicQueryHandler</c>), so a test can fix "now"
/// instead of racing the wall clock.
///
/// <para>
/// Never fabricates: a date this engine cannot compute from
/// <see cref="ContractRenewalTerms"/> comes back <see langword="null"/> with
/// <see cref="RenewalCalculationResult.Explanation"/> saying why, rather than a best guess
/// (Appendix C rule 10; parent story AC-3). See <see cref="RenewalCalculationStatus"/> for the
/// three-way outcome this produces, and <see cref="ContractRenewalTerms"/>'s own doc comment for
/// why this type takes a small DTO rather than the real
/// <c>Raffa.Documents.Contracts.Domain.Contract</c> (ADR-002 dependency direction).
/// </para>
/// </summary>
public sealed class RenewalEngine(IClock clock)
{
    /// <summary>
    /// Computes <paramref name="terms"/>'s renewal date, cancellation deadline, and days-until for
    /// each (relative to <see cref="IClock.UtcNow"/>). See the decision table in this method's
    /// body comments; every branch is covered by <c>Raffa.Renewals.Tests.RenewalEngineTests</c>.
    /// </summary>
    public RenewalCalculationResult Calculate(ContractRenewalTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        var asOf = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        // AutoRenewal is a known, validated fact (not a data gap): a contract that does not
        // auto-renew simply has no renewal date or cancellation-out-of-auto-renewal deadline, full
        // stop — regardless of what EndDate/CancellationNoticeDays say. See
        // RenewalCalculationStatus.NoRenewal's own doc comment for why this is deliberately not
        // folded into CannotDetermine.
        if (!terms.AutoRenewal)
        {
            return new RenewalCalculationResult(
                terms.ContractId,
                RenewalCalculationStatus.NoRenewal,
                RenewalDate: null,
                CancellationDeadline: null,
                DaysUntilRenewal: null,
                DaysUntilCancellationDeadline: null,
                "AutoRenewal is false: this contract does not renew, so no renewal date or " +
                "cancellation deadline applies (a determined fact, not missing data — Appendix C " +
                "rule 10).");
        }

        // AutoRenewal is true but EndDate — the one input every date below is anchored to — is
        // unknown. Nothing downstream can be computed without fabricating it (Appendix C rule 10;
        // parent story AC-3).
        if (terms.EndDate is not { } endDate)
        {
            return new RenewalCalculationResult(
                terms.ContractId,
                RenewalCalculationStatus.CannotDetermine,
                RenewalDate: null,
                CancellationDeadline: null,
                DaysUntilRenewal: null,
                DaysUntilCancellationDeadline: null,
                "AutoRenewal is true but EndDate is unknown: renewal date and cancellation " +
                "deadline cannot be computed from missing structured data rather than guessed " +
                "(Appendix C rule 10).");
        }

        // Determined: RenewalDate == EndDate. Reproduces
        // Raffa.Documents.Contracts.Application.PortfolioListItem.RenewalDate's own convention
        // on purpose (same fact, same derivation, computed here instead of duplicated ad hoc).
        var renewalDate = endDate;
        var daysUntilRenewal = DaysBetween(asOf, renewalDate);

        var (cancellationDeadline, daysUntilCancellationDeadline, deadlineExplanation) =
            CalculateCancellationDeadline(asOf, endDate, terms.CancellationNoticeDays);

        return new RenewalCalculationResult(
            terms.ContractId,
            RenewalCalculationStatus.Determined,
            renewalDate,
            cancellationDeadline,
            daysUntilRenewal,
            daysUntilCancellationDeadline,
            $"RenewalDate = EndDate ({endDate:yyyy-MM-dd}) because AutoRenewal is true " +
            "(deterministic arithmetic, Appendix C rule 6 — no LLM). " + deadlineExplanation);
    }

    /// <summary>Convenience batch form of <see cref="Calculate"/> for product spec §9.1's "daily
    /// scheduler for each active contract" shape — one result per input, in the same order, no
    /// aggregation. Which contracts are "active" (in scope to call this with) is the caller's
    /// decision, not this engine's: a threshold-scheduler/dashboard task filters before calling,
    /// the same way this engine takes whatever <see cref="ContractRenewalTerms"/> it is
    /// given.</summary>
    public IReadOnlyList<RenewalCalculationResult> CalculateMany(IEnumerable<ContractRenewalTerms> contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        return contracts.Select(Calculate).ToList();
    }

    private static (DateOnly? Deadline, int? DaysUntil, string Explanation) CalculateCancellationDeadline(
        DateOnly asOf, DateOnly endDate, int? cancellationNoticeDays)
    {
        if (cancellationNoticeDays is not { } noticeDays)
        {
            return (null, null,
                "CancellationDeadline could not be determined: CancellationNoticeDays is unknown " +
                "(Appendix C rule 10) — RenewalDate alone is still determined.");
        }

        if (noticeDays < 0)
        {
            return (null, null,
                $"CancellationDeadline could not be determined: CancellationNoticeDays " +
                $"({noticeDays}) is negative, which is not a valid notice period — invalid " +
                "structured data is treated the same as missing data, not guessed at (Appendix C " +
                "rule 10).");
        }

        var deadline = endDate.AddDays(-noticeDays);
        var daysUntil = DaysBetween(asOf, deadline);

        return (deadline, daysUntil,
            $"CancellationDeadline = EndDate ({endDate:yyyy-MM-dd}) - CancellationNoticeDays " +
            $"({noticeDays}) = {deadline:yyyy-MM-dd} (deterministic arithmetic, Appendix C rule " +
            "6 — no LLM).");
    }

    /// <summary><paramref name="target"/> minus <paramref name="asOf"/>, in days — negative when
    /// <paramref name="target"/> already passed. <see cref="DateOnly.DayNumber"/> (days since
    /// 0001-01-01) makes this a plain integer subtraction, no calendar-aware library needed.</summary>
    private static int DaysBetween(DateOnly asOf, DateOnly target) => target.DayNumber - asOf.DayNumber;
}
