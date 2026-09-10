using Raffa.Renewals.Domain;

namespace Raffa.Renewals.Application;

/// <summary>
/// Turns a <see cref="RenewalEngine"/> calculation into a <see cref="RenewalOpportunity"/> — task
/// E03/F01/US01/T02 (the wave-spec's <c>renewal-opportunity</c> artifact; parent story
/// us-01-deterministic-dates), product spec §9.1's daily-scheduler step "create/update renewal
/// opportunity" made concrete. Pure and synchronous, exactly like <see cref="RenewalEngine"/>
/// itself: no database call, no HTTP call, no LLM call anywhere in <see cref="Generate"/> or
/// <see cref="GenerateMany"/>.
///
/// <para>
/// Never fabricates: this generator does not compute anything <see cref="RenewalEngine"/> did not
/// already determine. When the engine abstains
/// (<see cref="RenewalCalculationStatus.CannotDetermine"/>), generation abstains too — it returns a
/// <see cref="RenewalOpportunity"/> whose <see cref="RenewalOpportunity.Status"/> is
/// <see cref="RenewalOpportunityStatus.CannotDetermine"/> and whose dates are all null, never a
/// best guess (Appendix C rule 10; parent story AC-3, this task's own "abstain cannot-determine
/// when missing" objective). See <see cref="RenewalOpportunityStatus"/> for the three-way outcome
/// this produces and <see cref="RenewalOpportunity"/>'s own doc comment for what this type
/// deliberately does not (yet) carry.
/// </para>
/// </summary>
public sealed class RenewalOpportunityGenerator(RenewalEngine engine)
{
    /// <summary>
    /// Computes <paramref name="terms"/>'s renewal calculation via <see cref="RenewalEngine.Calculate"/>
    /// and maps it onto a <see cref="RenewalOpportunity"/> — see <see cref="FromCalculation"/> for
    /// the mapping rule itself, which is exposed separately (static, no <see cref="RenewalEngine"/>
    /// dependency) for a caller that already ran the engine for its own purposes (for example a
    /// future threshold-scheduler or dashboard task) and wants an opportunity from that same result
    /// without recomputing it.
    /// </summary>
    public RenewalOpportunity Generate(ContractRenewalTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        return FromCalculation(engine.Calculate(terms));
    }

    /// <summary>Convenience batch form of <see cref="Generate"/> for product spec §9.1's "daily
    /// scheduler for each active contract" shape — one opportunity per input, in the same order, no
    /// aggregation. Mirrors <see cref="RenewalEngine.CalculateMany"/> exactly (same "which contracts
    /// are active is the caller's decision" rule).</summary>
    public IReadOnlyList<RenewalOpportunity> GenerateMany(IEnumerable<ContractRenewalTerms> contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        return contracts.Select(Generate).ToList();
    }

    /// <summary>
    /// Maps an already-computed <see cref="RenewalCalculationResult"/> onto a
    /// <see cref="RenewalOpportunity"/> — the whole of this generator's business rule, in one place,
    /// callable without a <see cref="RenewalEngine"/>/<c>IClock</c> dependency. Static and pure: the
    /// same <paramref name="calculation"/> always produces the same <see cref="RenewalOpportunity"/>
    /// (Appendix C rule 6).
    /// </summary>
    public static RenewalOpportunity FromCalculation(RenewalCalculationResult calculation)
    {
        ArgumentNullException.ThrowIfNull(calculation);

        return calculation.Status switch
        {
            // Determined: the engine could compute a renewal date, so there is a real opportunity
            // for Procurement to track. RenewalOpportunityStatus.Open, not Determined — see that
            // enum's own doc comment for why the name changes here but the other two do not.
            RenewalCalculationStatus.Determined => new RenewalOpportunity(
                calculation.ContractId,
                RenewalOpportunityStatus.Open,
                calculation.RenewalDate,
                calculation.CancellationDeadline,
                calculation.DaysUntilRenewal,
                calculation.DaysUntilCancellationDeadline,
                "Open renewal opportunity: " + calculation.Explanation),

            // NoRenewal: a determined non-event (AutoRenewal is false), not a data gap — the same
            // fact, same name, at the opportunity layer. No dates: there is nothing to track.
            RenewalCalculationStatus.NoRenewal => new RenewalOpportunity(
                calculation.ContractId,
                RenewalOpportunityStatus.NoRenewal,
                RenewalDate: null,
                CancellationDeadline: null,
                DaysUntilRenewal: null,
                DaysUntilCancellationDeadline: null,
                "No renewal opportunity: " + calculation.Explanation),

            // CannotDetermine: required structured data is missing upstream. Abstain — never
            // fabricate an opportunity from a date the engine itself could not determine (Appendix
            // C rule 10; parent story AC-3; this task's own objective).
            RenewalCalculationStatus.CannotDetermine => new RenewalOpportunity(
                calculation.ContractId,
                RenewalOpportunityStatus.CannotDetermine,
                RenewalDate: null,
                CancellationDeadline: null,
                DaysUntilRenewal: null,
                DaysUntilCancellationDeadline: null,
                "Cannot determine a renewal opportunity: " + calculation.Explanation),

            _ => throw new ArgumentOutOfRangeException(
                nameof(calculation),
                calculation.Status,
                $"Unrecognized {nameof(RenewalCalculationStatus)} value — every case must map to " +
                "an explicit RenewalOpportunityStatus rather than silently falling through."),
        };
    }
}
