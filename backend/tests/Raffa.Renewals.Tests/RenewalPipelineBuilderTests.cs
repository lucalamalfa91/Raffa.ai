using Raffa.Renewals.Application;
using Raffa.Renewals.Domain;
using Raffa.Renewals.Tests.TestSupport;
using Raffa.SharedKernel;

namespace Raffa.Renewals.Tests;

/// <summary>
/// Proves task E03/F03/US01/T01's execution step: <see cref="RenewalPipelineBuilder"/> turns a
/// batch of <see cref="RenewalDashboardCandidate"/> into <c>GET /api/renewals</c> pipeline rows —
/// parent story us-01-renewal-dashboard-api AC-1 ("returns pipeline (supplier/renewal/days/spend/
/// deadline/action)") and AC-2 ("Insight card separates facts from recommendations") — with no
/// database, no HTTP call and no LLM call anywhere in the path (Appendix C rule 6).
/// </summary>
public sealed class RenewalPipelineBuilderTests
{
    private static readonly DateOnly AsOf = new(2026, 1, 1);
    private static readonly FixedClock Clock = new(new DateTimeOffset(AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

    private readonly RenewalPipelineBuilder _builder = new(new RenewalEngine(Clock), Clock);

    [Fact]
    public void Reproduces_spec_9_3_worked_example_134_days_to_renewal_90_day_notice()
    {
        // product spec §9.3's own example: "Salesforce -- 134 days [to renewal] ... Cancellation
        // deadline 90 days ... Recommended action: Start negotiation now". A 90-day notice period
        // off a 134-day-out end date lands the cancellation deadline 44 days out.
        var supplierId = EntityId.New();
        var candidate = new RenewalDashboardCandidate(
            EntityId.New(), supplierId, AsOf.AddDays(134), AutoRenewal: true,
            AnnualSpend: 640_000m, CancellationDeadline: AsOf.AddDays(44));

        var result = _builder.Build([candidate]);

        var item = Assert.Single(result);
        Assert.Equal(RenewalCalculationStatus.Determined, item.Status);
        Assert.Equal(AsOf.AddDays(134), item.RenewalDate);
        Assert.Equal(134, item.DaysUntilRenewal);
        Assert.Equal(AsOf.AddDays(44), item.CancellationDeadline);
        Assert.Equal(44, item.DaysUntilCancellationDeadline);
        Assert.Equal(640_000m, item.AnnualSpend);
        Assert.Equal(supplierId, item.SupplierId);
        Assert.Equal("Start negotiation now", item.InsightCard.Recommendations.RecommendedAction);
    }

    [Fact]
    public void Insight_card_separates_facts_from_recommendations()
    {
        var supplierId = EntityId.New();
        var candidate = new RenewalDashboardCandidate(
            EntityId.New(), supplierId, AsOf.AddDays(134), AutoRenewal: true,
            AnnualSpend: 640_000m, CancellationDeadline: AsOf.AddDays(44));

        var item = Assert.Single(_builder.Build([candidate]));
        var facts = item.InsightCard.Facts;
        var recommendations = item.InsightCard.Recommendations;

        // Facts: only values this codebase actually knows (spec §9.3 "Supplier / renewal",
        // "Annual spend", "Cancellation deadline").
        Assert.Equal(supplierId, facts.SupplierId);
        Assert.Equal(AsOf.AddDays(134), facts.RenewalDate);
        Assert.Equal(134, facts.DaysUntilRenewal);
        Assert.Equal(640_000m, facts.AnnualSpend);
        Assert.Equal(AsOf.AddDays(44), facts.CancellationDeadline);
        Assert.Equal(44, facts.DaysUntilCancellationDeadline);

        // Recommendations: derived/suggested values (spec §9.3 "Annual uplift", "Market position",
        // "Potential savings", "Recommended action"). The three benchmark/savings-derived fields
        // are honestly null this wave (neither module is wired to this task's dependency) --
        // never fabricated.
        Assert.Equal("Start negotiation now", recommendations.RecommendedAction);
        Assert.False(string.IsNullOrWhiteSpace(recommendations.Explanation));
        Assert.Null(recommendations.AnnualUpliftPercent);
        Assert.Null(recommendations.MarketPosition);
        Assert.Null(recommendations.PotentialSavingsRange);
    }

    [Fact]
    public void Overdue_cancellation_deadline_recommends_acting_immediately()
    {
        var candidate = new RenewalDashboardCandidate(
            EntityId.New(), EntityId.New(), AsOf.AddDays(200), AutoRenewal: true,
            AnnualSpend: 1000m, CancellationDeadline: AsOf.AddDays(-5));

        var item = Assert.Single(_builder.Build([candidate]));

        Assert.Equal(-5, item.DaysUntilCancellationDeadline);
        Assert.Equal("Overdue — act immediately", item.InsightCard.Recommendations.RecommendedAction);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    public void Within_30_days_of_cancellation_deadline_recommends_finalizing_now(int daysOut)
    {
        var candidate = new RenewalDashboardCandidate(
            EntityId.New(), EntityId.New(), AsOf.AddDays(200), AutoRenewal: true,
            AnnualSpend: 1000m, CancellationDeadline: AsOf.AddDays(daysOut));

        var item = Assert.Single(_builder.Build([candidate]));

        Assert.Equal("Finalize decision now", item.InsightCard.Recommendations.RecommendedAction);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(90)]
    public void Within_90_days_of_cancellation_deadline_recommends_starting_negotiation(int daysOut)
    {
        var candidate = new RenewalDashboardCandidate(
            EntityId.New(), EntityId.New(), AsOf.AddDays(200), AutoRenewal: true,
            AnnualSpend: 1000m, CancellationDeadline: AsOf.AddDays(daysOut));

        var item = Assert.Single(_builder.Build([candidate]));

        Assert.Equal("Start negotiation now", item.InsightCard.Recommendations.RecommendedAction);
    }

    [Theory]
    [InlineData(91)]
    [InlineData(180)]
    public void Within_180_days_of_cancellation_deadline_recommends_preparing_strategy(int daysOut)
    {
        var candidate = new RenewalDashboardCandidate(
            EntityId.New(), EntityId.New(), AsOf.AddDays(200), AutoRenewal: true,
            AnnualSpend: 1000m, CancellationDeadline: AsOf.AddDays(daysOut));

        var item = Assert.Single(_builder.Build([candidate]));

        Assert.Equal("Prepare negotiation strategy", item.InsightCard.Recommendations.RecommendedAction);
    }

    [Fact]
    public void Beyond_180_days_of_cancellation_deadline_recommends_monitoring()
    {
        var candidate = new RenewalDashboardCandidate(
            EntityId.New(), EntityId.New(), AsOf.AddDays(300), AutoRenewal: true,
            AnnualSpend: 1000m, CancellationDeadline: AsOf.AddDays(181));

        var item = Assert.Single(_builder.Build([candidate]));

        Assert.Equal("Monitor — no action needed yet", item.InsightCard.Recommendations.RecommendedAction);
    }

    [Fact]
    public void Falls_back_to_renewal_date_urgency_when_cancellation_deadline_is_unknown()
    {
        // No CancellationDeadline fact at all -- urgency must fall back to DaysUntilRenewal (20
        // days, inside the 30-day "finalize" window) rather than treat urgency as unknown.
        var candidate = new RenewalDashboardCandidate(
            EntityId.New(), EntityId.New(), AsOf.AddDays(20), AutoRenewal: true,
            AnnualSpend: 1000m, CancellationDeadline: null);

        var item = Assert.Single(_builder.Build([candidate]));

        Assert.Null(item.CancellationDeadline);
        Assert.Null(item.DaysUntilCancellationDeadline);
        Assert.Equal(20, item.DaysUntilRenewal);
        Assert.Equal("Finalize decision now", item.InsightCard.Recommendations.RecommendedAction);
        Assert.Contains("renewal date", item.InsightCard.Recommendations.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Cannot_determine_still_reports_an_independently_known_cancellation_deadline_fact()
    {
        // EndDate missing => RenewalEngine reports CannotDetermine for RenewalDate/DaysUntilRenewal
        // -- but CancellationDeadline is a raw fact independent of EndDate (already extracted onto
        // Contract directly), so it must NOT be suppressed just because a *different* value could
        // not be derived (Appendix C rule 10; parent story's "never fabricate, never silently
        // drop" convention).
        var candidate = new RenewalDashboardCandidate(
            EntityId.New(), EntityId.New(), EndDate: null, AutoRenewal: true,
            AnnualSpend: 1000m, CancellationDeadline: AsOf.AddDays(10));

        var item = Assert.Single(_builder.Build([candidate]));

        Assert.Equal(RenewalCalculationStatus.CannotDetermine, item.Status);
        Assert.Null(item.RenewalDate);
        Assert.Null(item.DaysUntilRenewal);
        Assert.Equal(AsOf.AddDays(10), item.CancellationDeadline);
        Assert.Equal(10, item.DaysUntilCancellationDeadline);
        Assert.Equal(AsOf.AddDays(10), item.InsightCard.Facts.CancellationDeadline);
        Assert.Equal("Review contract — missing end date", item.InsightCard.Recommendations.RecommendedAction);
    }

    [Fact]
    public void No_renewal_when_auto_renewal_is_false()
    {
        var candidate = new RenewalDashboardCandidate(
            EntityId.New(), EntityId.New(), AsOf.AddDays(30), AutoRenewal: false,
            AnnualSpend: 1000m, CancellationDeadline: AsOf.AddDays(30));

        var item = Assert.Single(_builder.Build([candidate]));

        Assert.Equal(RenewalCalculationStatus.NoRenewal, item.Status);
        Assert.Null(item.RenewalDate);
        Assert.Null(item.DaysUntilRenewal);
        Assert.Equal("No action needed — auto-renewal disabled", item.InsightCard.Recommendations.RecommendedAction);
    }

    [Fact]
    public void Orders_items_by_urgency_soonest_first_with_unknown_urgency_last()
    {
        var soon = new RenewalDashboardCandidate(
            EntityId.New(), EntityId.New(), AsOf.AddDays(200), true, 1m, AsOf.AddDays(5));
        var later = new RenewalDashboardCandidate(
            EntityId.New(), EntityId.New(), AsOf.AddDays(200), true, 1m, AsOf.AddDays(100));
        var soonest = new RenewalDashboardCandidate(
            EntityId.New(), EntityId.New(), AsOf.AddDays(200), true, 1m, AsOf.AddDays(1));
        var unknownUrgency = new RenewalDashboardCandidate(
            EntityId.New(), EntityId.New(), EndDate: null, AutoRenewal: true, AnnualSpend: 1m, CancellationDeadline: null);

        var result = _builder.Build([later, unknownUrgency, soon, soonest]);

        Assert.Equal(
            [soonest.ContractId, soon.ContractId, later.ContractId, unknownUrgency.ContractId],
            result.Select(i => i.ContractId));
    }

    [Fact]
    public void Supplier_and_spend_facts_pass_through_unchanged_including_null_supplier()
    {
        var candidate = new RenewalDashboardCandidate(
            EntityId.New(), SupplierId: null, AsOf.AddDays(50), AutoRenewal: true,
            AnnualSpend: null, CancellationDeadline: null);

        var item = Assert.Single(_builder.Build([candidate]));

        Assert.Null(item.SupplierId);
        Assert.Null(item.AnnualSpend);
        Assert.Null(item.InsightCard.Facts.SupplierId);
        Assert.Null(item.InsightCard.Facts.AnnualSpend);
    }

    [Fact]
    public void Same_inputs_and_the_same_clock_produce_the_same_result_every_time()
    {
        var candidate = new RenewalDashboardCandidate(
            EntityId.New(), EntityId.New(), AsOf.AddDays(134), true, 640_000m, AsOf.AddDays(44));

        var first = _builder.Build([candidate]);
        var second = _builder.Build([candidate]);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Rejects_a_null_candidates_argument()
    {
        Assert.Throws<ArgumentNullException>(() => _builder.Build(null!));
    }

    [Fact]
    public void Rejects_a_null_candidate_element()
    {
        var candidates = new List<RenewalDashboardCandidate> { null! };

        Assert.Throws<ArgumentNullException>(() => _builder.Build(candidates));
    }
}
