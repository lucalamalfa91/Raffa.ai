namespace Raffa.Renewals.Domain;

/// <summary>
/// Outcome discriminator for <see cref="Raffa.Renewals.Application.RenewalEngine.Calculate"/>
/// (task E03/F01/US01/T01, parent story us-01-deterministic-dates). Three cases, not two, because
/// "we know this contract has no renewal" and "we do not have enough data to know" are different
/// facts and must stay distinguishable (Appendix C rule 10: return uncertainty instead of
/// fabricated precision — collapsing them into one null/not-null bit would silently treat a
/// determined "no renewal" the same as an honest "cannot determine").
/// </summary>
public enum RenewalCalculationStatus
{
    /// <summary>Renewal date was computed; cancellation deadline was computed too whenever its own
    /// required input (<see cref="Raffa.Renewals.Application.ContractRenewalTerms.CancellationNoticeDays"/>)
    /// was present and valid — see
    /// <see cref="Raffa.Renewals.Application.RenewalCalculationResult.CancellationDeadline"/>'s
    /// own doc comment for why that one field can still be null inside a <see cref="Determined"/>
    /// result.</summary>
    Determined,

    /// <summary>
    /// <see cref="Application.ContractRenewalTerms.AutoRenewal"/> is <see langword="false"/> — a
    /// definite, known fact, not a data gap. The contract simply ends on its end date; there is no
    /// renewal date or cancellation-out-of-auto-renewal deadline to compute (same rule
    /// <c>Raffa.Documents.Contracts.Application.PortfolioListItem.RenewalDate</c> already
    /// documents: "a contract that does not auto-renew has no next renewal date, only an end
    /// date").
    /// </summary>
    NoRenewal,

    /// <summary>Required structured data is missing (today: <c>EndDate</c>) — the engine abstains
    /// rather than fabricate a date (Appendix C rule 10; parent story AC-3).</summary>
    CannotDetermine,
}
