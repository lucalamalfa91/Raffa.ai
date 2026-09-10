using Raffa.Renewals.Domain;
using Raffa.SharedKernel;

namespace Raffa.Renewals.Application;

/// <summary>
/// Builds the <c>GET /api/renewals</c> pipeline (task E03/F03/US01/T01, us-01-renewal-dashboard-api
/// AC-1/AC-2; product spec §9.3 "Renewal insight card", §10.1 "Upcoming Renewals: Actionable
/// renewal pipeline") from a batch of <see cref="RenewalDashboardCandidate"/>. Pure and synchronous
/// — no database call, no HTTP call, no LLM call (Appendix C rule 6), same convention
/// <see cref="RenewalEngine"/> itself follows; this class's only job beyond delegating to
/// <see cref="RenewalEngine"/> is the "days until an already-known cancellation deadline" arithmetic
/// that engine has no method for (see <see cref="RenewalDashboardCandidate.CancellationDeadline"/>'s
/// own doc comment) and the deterministic recommended-action rule below.
///
/// <c>Raffa.ArchitectureTests.DependencyDirectionTests</c>'s allow-list for
/// <c>Raffa.Renewals</c> is exactly <c>[SharedKernel, Benchmark]</c>, so this type only ever sees
/// the small <see cref="RenewalDashboardCandidate"/> DTO — never the real
/// <c>Raffa.Documents.Contracts.Domain.Contract</c> — the same dependency-direction reason
/// <see cref="ContractRenewalTerms"/> exists.
/// </summary>
public sealed class RenewalPipelineBuilder(RenewalEngine renewalEngine, IClock clock)
{
    /// <summary>
    /// Below this many days until the relevant date (cancellation deadline, or renewal date when no
    /// deadline is known), the recommendation escalates to "finalize now". Narrower than product
    /// spec §9.1's smallest default threshold window (30 days) on purpose — this is the last chance
    /// to act, not a first alert.
    /// </summary>
    private const int FinalizeWindowDays = 30;

    /// <summary>
    /// Mirrors product spec §9.3's own worked example: "Salesforce — 134 days [to renewal] ...
    /// Cancellation deadline 90 days ... Recommended action: Start negotiation now". 90 days is one
    /// of spec §9.1's named default threshold windows (365/270/180/120/90/60/30).
    /// </summary>
    private const int StartNegotiationWindowDays = 90;

    /// <summary>Another of spec §9.1's named default threshold windows — inside this many days,
    /// negotiation prep should already be underway even if it need not be started yet.</summary>
    private const int PrepareWindowDays = 180;

    /// <summary>
    /// Builds one <see cref="RenewalPipelineItem"/> per <paramref name="candidates"/> entry, most
    /// urgent first (ascending by days until the relevant date — cancellation deadline when known,
    /// else renewal date; unknown-urgency rows sort last). Order-preserving-by-urgency, not
    /// input-order — an "actionable pipeline" is read top-to-bottom as a to-do list (spec §10.1),
    /// so the row needing action soonest belongs first regardless of how the caller enumerated
    /// contracts. Never drops a candidate: a row this method cannot compute a date for still comes
    /// back with an honest <see cref="RenewalCalculationStatus.CannotDetermine"/>/
    /// <see cref="RenewalCalculationStatus.NoRenewal"/> status and action text rather than being
    /// silently excluded (Appendix C rule 10).
    /// </summary>
    public IReadOnlyList<RenewalPipelineItem> Build(IEnumerable<RenewalDashboardCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var items = candidates.Select(BuildItem);

        return items
            .OrderBy(item => item.DaysUntilCancellationDeadline ?? item.DaysUntilRenewal ?? int.MaxValue)
            .ThenBy(item => item.ContractId.Value)
            .ToList();
    }

    private RenewalPipelineItem BuildItem(RenewalDashboardCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        // CancellationNoticeDays is deliberately null: Contract has no persisted column for it yet
        // (ContractRenewalTerms's own doc comment) — RenewalDashboardCandidate.CancellationDeadline
        // (the raw extracted fact) covers that gap independently, below.
        var terms = new ContractRenewalTerms(
            candidate.ContractId, candidate.EndDate, candidate.AutoRenewal, CancellationNoticeDays: null);
        var calculation = renewalEngine.Calculate(terms);

        var daysUntilCancellationDeadline = DaysUntil(candidate.CancellationDeadline);

        var (action, explanation) = DetermineRecommendation(calculation, daysUntilCancellationDeadline);

        var facts = new RenewalInsightFacts(
            candidate.SupplierId,
            calculation.RenewalDate,
            calculation.DaysUntilRenewal,
            candidate.AnnualSpend,
            candidate.CancellationDeadline,
            daysUntilCancellationDeadline);

        // Benchmark/Savings fields are always null this wave — see RenewalInsightRecommendations's
        // own doc comment for why (neither module is wired to this task's dependency).
        var recommendations = new RenewalInsightRecommendations(
            action, explanation, AnnualUpliftPercent: null, MarketPosition: null, PotentialSavingsRange: null);

        return new RenewalPipelineItem(
            candidate.ContractId,
            candidate.SupplierId,
            calculation.Status,
            calculation.RenewalDate,
            calculation.DaysUntilRenewal,
            candidate.AnnualSpend,
            candidate.CancellationDeadline,
            daysUntilCancellationDeadline,
            candidate.AutoRenewal,
            new RenewalInsightCard(facts, recommendations));
    }

    /// <summary><paramref name="target"/> minus "today" (<c>clock</c>), in days — negative
    /// when <paramref name="target"/> already passed, null when <paramref name="target"/> itself is
    /// null. Same unclamped-signed-day-count convention as
    /// <see cref="RenewalCalculationResult.DaysUntilRenewal"/>, computed independently here because
    /// <see cref="RenewalEngine"/> has no method that accepts an already-known date (it only ever
    /// derives one from <see cref="ContractRenewalTerms"/>).</summary>
    private int? DaysUntil(DateOnly? target)
    {
        if (target is not { } date)
        {
            return null;
        }

        var asOf = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        return date.DayNumber - asOf.DayNumber;
    }

    /// <summary>
    /// The deterministic (Appendix C rule 6 — no LLM) recommended-action rule this task adds. Only
    /// urgency-based: product spec §9.2's full Priority Score ("Spend Weight + Time Urgency +
    /// Benchmark Opportunity + Price Increase Risk + Contract Risk") additionally needs the
    /// Benchmark Service and a spend-weighting policy neither of which this task's dependency
    /// (<c>renewal-engine</c> only) provides — a follow-up task's scope, not attempted here.
    /// </summary>
    private static (string Action, string Explanation) DetermineRecommendation(
        RenewalCalculationResult calculation, int? daysUntilCancellationDeadline)
    {
        if (calculation.Status == RenewalCalculationStatus.CannotDetermine)
        {
            return ("Review contract — missing end date", calculation.Explanation);
        }

        if (calculation.Status == RenewalCalculationStatus.NoRenewal)
        {
            return ("No action needed — auto-renewal disabled", calculation.Explanation);
        }

        // Determined. Prefer urgency against the cancellation deadline (the date that actually
        // forces a decision) over the renewal date itself; fall back to the renewal date only when
        // no cancellation deadline is known at all.
        var usingCancellationDeadline = daysUntilCancellationDeadline is not null;
        var urgencyDays = daysUntilCancellationDeadline ?? calculation.DaysUntilRenewal;
        var label = usingCancellationDeadline ? "cancellation deadline" : "renewal date";

        if (urgencyDays is not { } days)
        {
            return ("Monitor — insufficient data for urgency", $"No {label} could be computed yet.");
        }

        if (days < 0)
        {
            return ("Overdue — act immediately", $"The {label} passed {-days} day(s) ago.");
        }

        if (days <= FinalizeWindowDays)
        {
            return ("Finalize decision now",
                $"The {label} is {days} day(s) away (within the {FinalizeWindowDays}-day window).");
        }

        if (days <= StartNegotiationWindowDays)
        {
            return ("Start negotiation now",
                $"The {label} is {days} day(s) away (within the {StartNegotiationWindowDays}-day window).");
        }

        if (days <= PrepareWindowDays)
        {
            return ("Prepare negotiation strategy",
                $"The {label} is {days} day(s) away (within the {PrepareWindowDays}-day window).");
        }

        return ("Monitor — no action needed yet",
            $"The {label} is {days} day(s) away, outside the {PrepareWindowDays}-day planning window.");
    }
}
