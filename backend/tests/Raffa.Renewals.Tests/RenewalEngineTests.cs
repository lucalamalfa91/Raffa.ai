using System.Reflection;
using Raffa.Renewals.Application;
using Raffa.Renewals.Domain;
using Raffa.Renewals.Tests.TestSupport;
using Raffa.SharedKernel;

namespace Raffa.Renewals.Tests;

/// <summary>
/// Proves task E03/F01/US01/T01's execution step: <see cref="RenewalEngine"/> computes a renewal
/// date and cancellation deadline from an in-memory <see cref="ContractRenewalTerms"/> snapshot —
/// parent story us-01-deterministic-dates AC-1 ("Renewal date + cancellation deadline derived from
/// contract terms — code, not LLM"), AC-2 ("Days-until/cancellation-deadline computed per active
/// contract") and AC-3 ("Missing dates return 'cannot determine' rather than a fabricated value")
/// — with no database, no HTTP call and no LLM call anywhere in the path.
/// </summary>
public sealed class RenewalEngineTests
{
    private static readonly DateOnly AsOf = new(2026, 1, 1);
    private static readonly FixedClock Clock = new(new DateTimeOffset(AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

    private readonly RenewalEngine _engine = new(Clock);

    [Fact]
    public void Determined_computes_renewal_date_and_cancellation_deadline_from_end_date_and_notice_days()
    {
        // Mirrors product spec §9.3's own renewal-insight-card example (134 days to renewal, 90
        // days' notice).
        var endDate = AsOf.AddDays(134);
        var terms = new ContractRenewalTerms(EntityId.New(), endDate, AutoRenewal: true, CancellationNoticeDays: 90);

        var result = _engine.Calculate(terms);

        Assert.Equal(terms.ContractId, result.ContractId);
        Assert.Equal(RenewalCalculationStatus.Determined, result.Status);
        Assert.Equal(endDate, result.RenewalDate);
        Assert.Equal(134, result.DaysUntilRenewal);
        Assert.Equal(endDate.AddDays(-90), result.CancellationDeadline);
        Assert.Equal(44, result.DaysUntilCancellationDeadline);
        Assert.Contains("Appendix C rule 6", result.Explanation);
    }

    [Fact]
    public void No_renewal_when_the_contract_does_not_auto_renew()
    {
        // AutoRenewal=false must win even though EndDate/CancellationNoticeDays both look usable —
        // a determined "no renewal", not a data gap.
        var terms = new ContractRenewalTerms(EntityId.New(), AsOf.AddDays(30), AutoRenewal: false, CancellationNoticeDays: 90);

        var result = _engine.Calculate(terms);

        Assert.Equal(RenewalCalculationStatus.NoRenewal, result.Status);
        Assert.Null(result.RenewalDate);
        Assert.Null(result.CancellationDeadline);
        Assert.Null(result.DaysUntilRenewal);
        Assert.Null(result.DaysUntilCancellationDeadline);
    }

    [Fact]
    public void Cannot_determine_anything_when_end_date_is_missing_even_if_auto_renewal_is_true()
    {
        var terms = new ContractRenewalTerms(EntityId.New(), EndDate: null, AutoRenewal: true, CancellationNoticeDays: 90);

        var result = _engine.Calculate(terms);

        Assert.Equal(RenewalCalculationStatus.CannotDetermine, result.Status);
        Assert.Null(result.RenewalDate);
        Assert.Null(result.CancellationDeadline);
        Assert.Null(result.DaysUntilRenewal);
        Assert.Null(result.DaysUntilCancellationDeadline);
        Assert.Contains("Appendix C rule 10", result.Explanation);
    }

    [Fact]
    public void Renewal_date_is_determined_but_cancellation_deadline_abstains_when_notice_days_is_unknown()
    {
        var endDate = AsOf.AddDays(50);
        var terms = new ContractRenewalTerms(EntityId.New(), endDate, AutoRenewal: true, CancellationNoticeDays: null);

        var result = _engine.Calculate(terms);

        // Partial determination: the renewal date needed only EndDate + AutoRenewal, both known —
        // it must not be downgraded to CannotDetermine just because a *different* value
        // (CancellationDeadline) is separately unknown.
        Assert.Equal(RenewalCalculationStatus.Determined, result.Status);
        Assert.Equal(endDate, result.RenewalDate);
        Assert.Equal(50, result.DaysUntilRenewal);
        Assert.Null(result.CancellationDeadline);
        Assert.Null(result.DaysUntilCancellationDeadline);
        Assert.Contains("CancellationNoticeDays is unknown", result.Explanation);
    }

    [Fact]
    public void Cancellation_deadline_abstains_rather_than_compute_from_a_negative_notice_period()
    {
        var endDate = AsOf.AddDays(50);
        var terms = new ContractRenewalTerms(EntityId.New(), endDate, AutoRenewal: true, CancellationNoticeDays: -5);

        var result = _engine.Calculate(terms);

        Assert.Equal(RenewalCalculationStatus.Determined, result.Status);
        Assert.Equal(endDate, result.RenewalDate);
        Assert.Null(result.CancellationDeadline);
        Assert.Null(result.DaysUntilCancellationDeadline);
        Assert.Contains("negative", result.Explanation);
    }

    [Fact]
    public void Days_until_is_negative_and_unclamped_for_an_already_lapsed_end_date()
    {
        var endDate = AsOf.AddDays(-10);
        var terms = new ContractRenewalTerms(EntityId.New(), endDate, AutoRenewal: true, CancellationNoticeDays: 5);

        var result = _engine.Calculate(terms);

        Assert.Equal(RenewalCalculationStatus.Determined, result.Status);
        Assert.Equal(-10, result.DaysUntilRenewal);
        Assert.Equal(endDate.AddDays(-5), result.CancellationDeadline);
        Assert.Equal(-15, result.DaysUntilCancellationDeadline);
    }

    [Fact]
    public void Days_until_is_zero_when_the_date_is_today()
    {
        var terms = new ContractRenewalTerms(EntityId.New(), AsOf, AutoRenewal: true, CancellationNoticeDays: 0);

        var result = _engine.Calculate(terms);

        Assert.Equal(0, result.DaysUntilRenewal);
        Assert.Equal(AsOf, result.CancellationDeadline);
        Assert.Equal(0, result.DaysUntilCancellationDeadline);
    }

    [Fact]
    public void Same_inputs_and_the_same_clock_produce_the_same_result_every_time()
    {
        var terms = new ContractRenewalTerms(EntityId.New(), AsOf.AddDays(200), AutoRenewal: true, CancellationNoticeDays: 60);

        var first = _engine.Calculate(terms);
        var second = _engine.Calculate(terms);

        // RenewalCalculationResult is a record: this is value equality across every field,
        // including Explanation — the whole point of "deterministic" (Appendix C rule 6).
        Assert.Equal(first, second);
    }

    [Fact]
    public void Rejects_a_null_terms_argument()
    {
        Assert.Throws<ArgumentNullException>(() => _engine.Calculate(null!));
    }

    [Fact]
    public void Rejects_a_null_contract_list_in_the_batch_form()
    {
        Assert.Throws<ArgumentNullException>(() => _engine.CalculateMany(null!));
    }

    [Fact]
    public void CalculateMany_computes_one_result_per_contract_preserving_order_and_correlation()
    {
        var determined = new ContractRenewalTerms(EntityId.New(), AsOf.AddDays(10), AutoRenewal: true, CancellationNoticeDays: 5);
        var noRenewal = new ContractRenewalTerms(EntityId.New(), AsOf.AddDays(10), AutoRenewal: false, CancellationNoticeDays: null);
        var cannotDetermine = new ContractRenewalTerms(EntityId.New(), EndDate: null, AutoRenewal: true, CancellationNoticeDays: null);

        var results = _engine.CalculateMany([determined, noRenewal, cannotDetermine]);

        Assert.Equal(3, results.Count);

        Assert.Equal(determined.ContractId, results[0].ContractId);
        Assert.Equal(RenewalCalculationStatus.Determined, results[0].Status);

        Assert.Equal(noRenewal.ContractId, results[1].ContractId);
        Assert.Equal(RenewalCalculationStatus.NoRenewal, results[1].Status);

        Assert.Equal(cannotDetermine.ContractId, results[2].ContractId);
        Assert.Equal(RenewalCalculationStatus.CannotDetermine, results[2].Status);
    }

    // Same structural proof Raffa.Chat.Tests.DeterministicQueryHandlerTests uses for the AI
    // Gateway: Appendix C rule 3 ("never call a benchmark provider directly from renewal, savings
    // or quote business logic") must hold for this specific class, not just at the project-
    // reference level (Raffa.Renewals.csproj is *allowed* to reference Raffa.Benchmark, so
    // only a class-level check like this one catches an accidental constructor dependency).
    [Fact]
    public void Engine_has_no_dependency_on_the_Benchmark_Service()
    {
        var type = typeof(RenewalEngine);

        var constructorParamsFromBenchmark = type.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Where(p => p.ParameterType.Namespace == "Raffa.Benchmark")
            .ToList();

        var fieldsFromBenchmark = type
            .GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => f.FieldType.Namespace == "Raffa.Benchmark")
            .ToList();

        Assert.Empty(constructorParamsFromBenchmark);
        Assert.Empty(fieldsFromBenchmark);
    }
}
