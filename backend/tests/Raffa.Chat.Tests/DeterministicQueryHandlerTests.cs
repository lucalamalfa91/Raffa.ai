using System.Reflection;
using Raffa.Chat.Application;
using Raffa.Chat.Domain;
using Raffa.Chat.Tests.TestSupport;
using Raffa.SharedKernel;

namespace Raffa.Chat.Tests;

/// <summary>
/// Proves task E02/F04/US01/T02's execution step: <see cref="DeterministicQueryHandler"/> answers
/// a <see cref="DeterministicQuery"/> by filtering/aggregating an in-memory
/// <see cref="ContractFact"/> snapshot — parent story us-01-query-router AC-2 ("Structured
/// questions hit deterministic queries/filters (no LLM)") — with no database, no HTTP call and no
/// LLM call anywhere in the path.
/// </summary>
public sealed class DeterministicQueryHandlerTests
{
    private static readonly DateOnly AsOf = new(2026, 1, 1);
    private static readonly FixedClock Clock = new(new DateTimeOffset(AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

    private readonly DeterministicQueryHandler _handler = new(Clock);

    [Fact]
    public void Answers_which_contracts_renew_in_the_next_120_days()
    {
        // Matches: inside the window, at the lower bound (today), and at the upper bound
        // (exactly 120 days out) — both edges are inclusive.
        var withinWindow = NewFact(autoRenewal: true, endDate: AsOf.AddDays(10));
        var atLowerBound = NewFact(autoRenewal: true, endDate: AsOf);
        var atUpperBound = NewFact(autoRenewal: true, endDate: AsOf.AddDays(120));

        // Non-matches: one day past the window, not auto-renewing, no end date, already lapsed.
        var pastUpperBound = NewFact(autoRenewal: true, endDate: AsOf.AddDays(121));
        var notAutoRenewing = NewFact(autoRenewal: false, endDate: AsOf.AddDays(10));
        var noEndDate = NewFact(autoRenewal: true, endDate: null);
        var alreadyLapsed = NewFact(autoRenewal: true, endDate: AsOf.AddDays(-5));

        var contracts = new[]
        {
            withinWindow, atLowerBound, atUpperBound, pastUpperBound, notAutoRenewing, noEndDate, alreadyLapsed,
        };

        var result = _handler.Handle(
            new DeterministicQuery.RenewalWindow("Which contracts renew in the next 120 days?", 120),
            contracts);

        Assert.Equal(DeterministicQueryKind.RenewalWindow, result.Kind);
        Assert.Equal(
            [withinWindow.ContractId, atLowerBound.ContractId, atUpperBound.ContractId],
            result.MatchedContractIds);
        Assert.Null(result.AggregateAnnualSpend);
        Assert.Contains("3 contract(s)", result.Explanation);
    }

    [Fact]
    public void Answers_our_annual_spend_across_all_suppliers()
    {
        var withSpend1 = NewFact(annualSpend: 100_000m);
        var withSpend2 = NewFact(annualSpend: 250_000m);
        var unvalidatedSpend = NewFact(annualSpend: null);

        var contracts = new[] { withSpend1, withSpend2, unvalidatedSpend };

        var result = _handler.Handle(
            new DeterministicQuery.AnnualSpend("What is our annual spend?", SupplierId: null),
            contracts);

        Assert.Equal(DeterministicQueryKind.AnnualSpend, result.Kind);
        Assert.Equal(350_000m, result.AggregateAnnualSpend);
        Assert.Equal([withSpend1.ContractId, withSpend2.ContractId], result.MatchedContractIds);
        // No supplier was named at all (RequestedSupplierName null, same as SupplierId here) —
        // a genuinely company-wide question, not an unresolved one.
        Assert.False(result.SupplierScopeUnresolved);
    }

    [Fact]
    public void Scopes_annual_spend_to_one_supplier_when_a_supplier_id_is_given()
    {
        var supplierA = EntityId.New();
        var supplierB = EntityId.New();

        var supplierAContract = NewFact(annualSpend: 100_000m, supplierId: supplierA);
        var supplierBContract = NewFact(annualSpend: 999_000m, supplierId: supplierB);

        var result = _handler.Handle(
            new DeterministicQuery.AnnualSpend("What is our annual spend with supplier A?", supplierA),
            [supplierAContract, supplierBContract]);

        Assert.Equal(100_000m, result.AggregateAnnualSpend);
        Assert.Equal([supplierAContract.ContractId], result.MatchedContractIds);
        // A resolved SupplierId was supplied (however it got resolved) — scoping actually
        // happened, so this is not the unresolved case.
        Assert.False(result.SupplierScopeUnresolved);
    }

    [Fact]
    public void Flags_supplier_scope_unresolved_when_the_question_names_a_supplier_the_planner_could_not_resolve()
    {
        var result = _handler.Handle(
            new DeterministicQuery.AnnualSpend(
                "What is our Microsoft annual spend?", SupplierId: null, RequestedSupplierName: "Microsoft"),
            [NewFact(annualSpend: 100_000m)]);

        Assert.Equal(DeterministicQueryKind.AnnualSpend, result.Kind);
        // The aggregate is still computed — every contract in the snapshot, not just
        // "Microsoft"'s — but the flag tells the caller not to present it as if it were.
        Assert.Equal(100_000m, result.AggregateAnnualSpend);
        Assert.True(result.SupplierScopeUnresolved);
        Assert.Contains("Microsoft", result.Explanation);
    }

    [Fact]
    public void Zero_matching_contracts_sums_to_zero_not_null()
    {
        var result = _handler.Handle(
            new DeterministicQuery.AnnualSpend("What is our annual spend?", SupplierId: EntityId.New()),
            [NewFact(annualSpend: 100_000m)]);

        Assert.Equal(0m, result.AggregateAnnualSpend);
        Assert.Empty(result.MatchedContractIds);
    }

    [Fact]
    public void Reports_an_unsupported_query_without_fabricating_an_answer()
    {
        var query = new DeterministicQuery.Unsupported(
            "What is the total contract value for our SAP agreement?",
            "no deterministic handler exists yet for this phrasing.");

        var result = _handler.Handle(query, []);

        Assert.Equal(DeterministicQueryKind.Unsupported, result.Kind);
        Assert.Empty(result.MatchedContractIds);
        Assert.Null(result.AggregateAnnualSpend);
        Assert.Equal(query.Reason, result.Explanation);
    }

    [Fact]
    public void Rejects_a_null_query()
    {
        Assert.Throws<ArgumentNullException>(() => _handler.Handle(null!, []));
    }

    [Fact]
    public void Rejects_a_null_contract_list()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _handler.Handle(new DeterministicQuery.AnnualSpend("q", null), null!));
    }

    // Same structural proof AskRaffaQueryRouterTests and DeterministicQueryPlannerTests already
    // use: the handler cannot call the AI Gateway because it holds no reference to it anywhere.
    [Fact]
    public void Handler_has_no_dependency_on_the_AI_Gateway()
    {
        var type = typeof(DeterministicQueryHandler);

        var constructorParamsFromGateway = type.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Where(p => p.ParameterType.Namespace == "Raffa.AiGateway")
            .ToList();

        var fieldsFromGateway = type
            .GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => f.FieldType.Namespace == "Raffa.AiGateway")
            .ToList();

        Assert.Empty(constructorParamsFromGateway);
        Assert.Empty(fieldsFromGateway);
    }

    private static ContractFact NewFact(
        bool autoRenewal = false,
        DateOnly? endDate = null,
        decimal? annualSpend = null,
        EntityId? supplierId = null) =>
        new(EntityId.New(), supplierId, annualSpend, endDate, autoRenewal);
}
