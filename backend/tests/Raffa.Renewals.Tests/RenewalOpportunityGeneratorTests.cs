using Raffa.Renewals.Application;
using Raffa.Renewals.Domain;
using Raffa.Renewals.Tests.TestSupport;
using Raffa.SharedKernel;

namespace Raffa.Renewals.Tests;

/// <summary>
/// Proves task E03/F01/US01/T02's execution step: <see cref="RenewalOpportunityGenerator"/> turns a
/// <see cref="RenewalEngine"/> calculation into a <see cref="RenewalOpportunity"/> — parent story
/// us-01-deterministic-dates AC-3 ("Missing dates return 'cannot determine' rather than a
/// fabricated value") applied to the opportunity built from those dates, not just the dates
/// themselves — with no database, no HTTP call and no LLM call anywhere in the path. Mirrors
/// <c>RenewalEngineTests</c>'s structure: an end-to-end group through
/// <see cref="RenewalOpportunityGenerator.Generate"/>/<see cref="RenewalOpportunityGenerator.GenerateMany"/>
/// (a real <see cref="RenewalEngine"/> plus a <see cref="FixedClock"/>), and a pure-mapping group
/// through <see cref="RenewalOpportunityGenerator.FromCalculation"/> that needs neither.
/// </summary>
public sealed class RenewalOpportunityGeneratorTests
{
    private static readonly DateOnly AsOf = new(2026, 1, 1);
    private static readonly FixedClock Clock = new(new DateTimeOffset(AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

    private readonly RenewalOpportunityGenerator _generator = new(new RenewalEngine(Clock));

    [Fact]
    public void Open_opportunity_when_renewal_is_determined_from_end_date_and_notice_days()
    {
        var endDate = AsOf.AddDays(134);
        var terms = new ContractRenewalTerms(EntityId.New(), endDate, AutoRenewal: true, CancellationNoticeDays: 90);

        var opportunity = _generator.Generate(terms);

        Assert.Equal(terms.ContractId, opportunity.ContractId);
        Assert.Equal(RenewalOpportunityStatus.Open, opportunity.Status);
        Assert.Equal(endDate, opportunity.RenewalDate);
        Assert.Equal(134, opportunity.DaysUntilRenewal);
        Assert.Equal(endDate.AddDays(-90), opportunity.CancellationDeadline);
        Assert.Equal(44, opportunity.DaysUntilCancellationDeadline);
        Assert.Contains("Open renewal opportunity", opportunity.Explanation);
    }

    [Fact]
    public void Opportunity_is_still_open_when_only_the_cancellation_deadline_is_unknown()
    {
        // Partial determination: the renewal date needed only EndDate + AutoRenewal, both known —
        // a missing CancellationNoticeDays must not downgrade the whole opportunity to
        // CannotDetermine (same rule RenewalEngineTests proves at the calculation layer).
        var endDate = AsOf.AddDays(50);
        var terms = new ContractRenewalTerms(EntityId.New(), endDate, AutoRenewal: true, CancellationNoticeDays: null);

        var opportunity = _generator.Generate(terms);

        Assert.Equal(RenewalOpportunityStatus.Open, opportunity.Status);
        Assert.Equal(endDate, opportunity.RenewalDate);
        Assert.Equal(50, opportunity.DaysUntilRenewal);
        Assert.Null(opportunity.CancellationDeadline);
        Assert.Null(opportunity.DaysUntilCancellationDeadline);
    }

    [Fact]
    public void No_renewal_opportunity_when_the_contract_does_not_auto_renew()
    {
        var terms = new ContractRenewalTerms(EntityId.New(), AsOf.AddDays(30), AutoRenewal: false, CancellationNoticeDays: 90);

        var opportunity = _generator.Generate(terms);

        Assert.Equal(RenewalOpportunityStatus.NoRenewal, opportunity.Status);
        Assert.Null(opportunity.RenewalDate);
        Assert.Null(opportunity.CancellationDeadline);
        Assert.Null(opportunity.DaysUntilRenewal);
        Assert.Null(opportunity.DaysUntilCancellationDeadline);
        Assert.Contains("No renewal opportunity", opportunity.Explanation);
    }

    [Fact]
    public void Abstains_with_cannot_determine_when_end_date_is_missing_even_if_auto_renewal_is_true()
    {
        var terms = new ContractRenewalTerms(EntityId.New(), EndDate: null, AutoRenewal: true, CancellationNoticeDays: 90);

        var opportunity = _generator.Generate(terms);

        Assert.Equal(RenewalOpportunityStatus.CannotDetermine, opportunity.Status);
        Assert.Null(opportunity.RenewalDate);
        Assert.Null(opportunity.CancellationDeadline);
        Assert.Null(opportunity.DaysUntilRenewal);
        Assert.Null(opportunity.DaysUntilCancellationDeadline);
        Assert.Contains("Cannot determine a renewal opportunity", opportunity.Explanation);
    }

    [Fact]
    public void GenerateMany_computes_one_opportunity_per_contract_preserving_order_and_correlation()
    {
        var open = new ContractRenewalTerms(EntityId.New(), AsOf.AddDays(10), AutoRenewal: true, CancellationNoticeDays: 5);
        var noRenewal = new ContractRenewalTerms(EntityId.New(), AsOf.AddDays(10), AutoRenewal: false, CancellationNoticeDays: null);
        var cannotDetermine = new ContractRenewalTerms(EntityId.New(), EndDate: null, AutoRenewal: true, CancellationNoticeDays: null);

        var opportunities = _generator.GenerateMany([open, noRenewal, cannotDetermine]);

        Assert.Equal(3, opportunities.Count);

        Assert.Equal(open.ContractId, opportunities[0].ContractId);
        Assert.Equal(RenewalOpportunityStatus.Open, opportunities[0].Status);

        Assert.Equal(noRenewal.ContractId, opportunities[1].ContractId);
        Assert.Equal(RenewalOpportunityStatus.NoRenewal, opportunities[1].Status);

        Assert.Equal(cannotDetermine.ContractId, opportunities[2].ContractId);
        Assert.Equal(RenewalOpportunityStatus.CannotDetermine, opportunities[2].Status);
    }

    [Fact]
    public void Same_inputs_and_the_same_clock_produce_the_same_opportunity_every_time()
    {
        var terms = new ContractRenewalTerms(EntityId.New(), AsOf.AddDays(200), AutoRenewal: true, CancellationNoticeDays: 60);

        var first = _generator.Generate(terms);
        var second = _generator.Generate(terms);

        // RenewalOpportunity is a record: this is value equality across every field, including
        // Explanation — the whole point of "deterministic" (Appendix C rule 6).
        Assert.Equal(first, second);
    }

    [Fact]
    public void Rejects_a_null_terms_argument()
    {
        Assert.Throws<ArgumentNullException>(() => _generator.Generate(null!));
    }

    [Fact]
    public void Rejects_a_null_contract_list_in_the_batch_form()
    {
        Assert.Throws<ArgumentNullException>(() => _generator.GenerateMany(null!));
    }

    // ------- FromCalculation: the pure mapping rule, with no RenewalEngine/IClock in the loop -------

    [Fact]
    public void FromCalculation_maps_Determined_to_Open_and_copies_every_field()
    {
        var contractId = EntityId.New();
        var calculation = new RenewalCalculationResult(
            contractId,
            RenewalCalculationStatus.Determined,
            RenewalDate: AsOf.AddDays(134),
            CancellationDeadline: AsOf.AddDays(44),
            DaysUntilRenewal: 134,
            DaysUntilCancellationDeadline: 44,
            Explanation: "some calculation trace");

        var opportunity = RenewalOpportunityGenerator.FromCalculation(calculation);

        Assert.Equal(contractId, opportunity.ContractId);
        Assert.Equal(RenewalOpportunityStatus.Open, opportunity.Status);
        Assert.Equal(calculation.RenewalDate, opportunity.RenewalDate);
        Assert.Equal(calculation.CancellationDeadline, opportunity.CancellationDeadline);
        Assert.Equal(calculation.DaysUntilRenewal, opportunity.DaysUntilRenewal);
        Assert.Equal(calculation.DaysUntilCancellationDeadline, opportunity.DaysUntilCancellationDeadline);
        Assert.Contains("some calculation trace", opportunity.Explanation);
    }

    [Fact]
    public void FromCalculation_maps_NoRenewal_to_NoRenewal_with_no_dates()
    {
        var calculation = new RenewalCalculationResult(
            EntityId.New(),
            RenewalCalculationStatus.NoRenewal,
            RenewalDate: null,
            CancellationDeadline: null,
            DaysUntilRenewal: null,
            DaysUntilCancellationDeadline: null,
            Explanation: "does not auto-renew");

        var opportunity = RenewalOpportunityGenerator.FromCalculation(calculation);

        Assert.Equal(RenewalOpportunityStatus.NoRenewal, opportunity.Status);
        Assert.Null(opportunity.RenewalDate);
        Assert.Null(opportunity.CancellationDeadline);
    }

    [Fact]
    public void FromCalculation_maps_CannotDetermine_to_CannotDetermine_and_never_fabricates_a_date()
    {
        var calculation = new RenewalCalculationResult(
            EntityId.New(),
            RenewalCalculationStatus.CannotDetermine,
            RenewalDate: null,
            CancellationDeadline: null,
            DaysUntilRenewal: null,
            DaysUntilCancellationDeadline: null,
            Explanation: "end date unknown");

        var opportunity = RenewalOpportunityGenerator.FromCalculation(calculation);

        Assert.Equal(RenewalOpportunityStatus.CannotDetermine, opportunity.Status);
        Assert.Null(opportunity.RenewalDate);
        Assert.Null(opportunity.CancellationDeadline);
        Assert.Null(opportunity.DaysUntilRenewal);
        Assert.Null(opportunity.DaysUntilCancellationDeadline);
    }

    [Fact]
    public void FromCalculation_rejects_a_null_calculation_argument()
    {
        Assert.Throws<ArgumentNullException>(() => RenewalOpportunityGenerator.FromCalculation(null!));
    }
}
