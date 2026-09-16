namespace Raffa.Savings.Domain;

/// <summary>
/// The Procurement workflow lifecycle for one <see cref="SavingsOpportunity"/> (task
/// E04/F02/US02/T01, savings-opportunity; parent story us-02-savings-opportunity; product spec
/// §4.3 "Create a trackable SavingsOpportunity with status, owner and realized outcome" and its own
/// dashboard KPI line "Display ... savings identified, savings realized, savings in progress ...").
///
/// <para>
/// Documented assumption (no ADR/spec vocabulary fixes an exact enum — spec §6's core data model
/// table names only a bare <c>status</c> column): this three-state shape is read directly off spec
/// §4.3's own three named KPI buckets ("savings identified" / "savings in progress" / "savings
/// realized"), the same "closed set because this module's own pipeline/API is the only writer,
/// never free-form extraction" reasoning <c>Raffa.Renewals.Domain.RenewalActionStatus</c> already
/// documents for its own three-state shape. No "rejected/dismissed" terminal state exists yet — spec
/// §4.3 names exactly three buckets, not four, and inventing a fourth here would be a product
/// decision this task does not own (Appendix C rule 10); a dismissal state is a follow-up, not
/// attempted by this task.
/// </para>
/// </summary>
public enum SavingsOpportunityStatus
{
    /// <summary>A candidate saving has been computed (typically from
    /// <c>Raffa.Savings.Application.PriceNormalizationCalculator</c>'s output, task
    /// E04/F02/US01/T01) but Procurement has not yet reviewed/actioned it — spec §4.3's "savings
    /// identified" KPI bucket. The initial status every <see cref="SavingsOpportunity"/> is created
    /// with (<c>Raffa.Savings.Application.SavingsOpportunityService.CreateAsync</c>).</summary>
    Identified,

    /// <summary>Procurement is actively pursuing this opportunity (negotiating, consolidating,
    /// switching supplier, etc.) — spec §4.3's "savings in progress" KPI bucket. Distinct from
    /// <see cref="Identified"/> so a dashboard can separate "needs a decision" from "already being
    /// worked".</summary>
    InProgress,

    /// <summary>The saving was actually achieved — spec §4.3's "savings realized" KPI bucket.
    /// Setting this status alone (via `PATCH /api/savings/{id}`) does not write a verified amount:
    /// that lands on a separate <see cref="RealizedSavings"/> row when a non-null
    /// <c>realizedAmount</c> is supplied. The Savings KPI reads those rows, not this status, for
    /// the verified-money figure (task E20/F01/US01/T01).</summary>
    Realized,
}
