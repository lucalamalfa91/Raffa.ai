using System.Reflection;
using Raffa.Chat.Application;
using Raffa.Chat.Domain;

namespace Raffa.Chat.Tests;

/// <summary>
/// Proves task E02/F04/US01/T02's planning step: a <see cref="QueryRouteDecision"/> already
/// routed <see cref="QueryIntent.Structured"/> by task E02/F04/US01/T01's
/// <see cref="AskRaffaQueryRouter"/> becomes the right typed <see cref="DeterministicQuery"/>.
/// </summary>
public sealed class DeterministicQueryPlannerTests
{
    private readonly AskRaffaQueryRouter _router = new();
    private readonly DeterministicQueryPlanner _planner = new();

    // Product spec §8.3's own structured example — the parent story's "dates" half.
    [Fact]
    public void Plans_the_spec_8_3_renewal_window_example()
    {
        var decision = _router.Route("Which contracts renew in the next 120 days?");

        var plan = _planner.Plan(decision);

        var renewalWindow = Assert.IsType<DeterministicQuery.RenewalWindow>(plan);
        Assert.Equal(120, renewalWindow.Days);
        Assert.Equal(decision.Question, renewalWindow.Question);
    }

    // Product spec §8.3's own structured example — the parent story's "spend" half.
    [Fact]
    public void Plans_the_spec_8_3_annual_spend_example()
    {
        var decision = _router.Route("What is our Microsoft annual spend?");

        var plan = _planner.Plan(decision);

        var annualSpend = Assert.IsType<DeterministicQuery.AnnualSpend>(plan);
        Assert.Null(annualSpend.SupplierId);
        // Named supplier, not resolved to an id: DeterministicQueryHandler must not silently
        // aggregate across every supplier as if this were an unscoped question (Appendix C
        // rule 10) — see DeterministicQueryResult.SupplierScopeUnresolved.
        Assert.Equal("Microsoft", annualSpend.RequestedSupplierName);
        Assert.Equal(decision.Question, annualSpend.Question);
    }

    [Theory]
    [InlineData("Which contracts expire in the next 30 days?", 30)]
    [InlineData("Which contracts renew in the next 3 months?", 90)]
    [InlineData("Which contracts renew in the next 1 year?", 365)]
    [InlineData("Which contracts renew in the next 2 years?", 730)]
    public void Normalizes_the_window_unit_to_days(string question, int expectedDays)
    {
        var decision = _router.Route(question);

        var plan = _planner.Plan(decision);

        var renewalWindow = Assert.IsType<DeterministicQuery.RenewalWindow>(plan);
        Assert.Equal(expectedDays, renewalWindow.Days);
    }

    [Fact]
    public void Plans_a_spend_question_without_the_literal_example_wording()
    {
        var decision = _router.Route("How much do we spend annually with Acme Corp?");

        var plan = _planner.Plan(decision);

        var annualSpend = Assert.IsType<DeterministicQuery.AnnualSpend>(plan);
        Assert.Null(annualSpend.SupplierId);
        // Two-word supplier name, phrased as "with <Name>" rather than "<Name> annual spend" —
        // proves the detection isn't hard-coded to the spec's exact example wording.
        Assert.Equal("Acme Corp", annualSpend.RequestedSupplierName);
    }

    [Fact]
    public void Does_not_flag_a_supplier_name_when_none_is_asked_for()
    {
        var decision = _router.Route("What is our annual spend?");

        var plan = _planner.Plan(decision);

        var annualSpend = Assert.IsType<DeterministicQuery.AnnualSpend>(plan);
        Assert.Null(annualSpend.SupplierId);
        Assert.Null(annualSpend.RequestedSupplierName);
    }

    // "Total contract value" is a structured field (AskRaffaQueryRouter.StructuredKeywords
    // includes it) but is neither a renewal-date-window nor an annual-spend query — task
    // E02/F04/US01/T02's title scopes it to "dates/spend" only. Must not be silently mapped onto
    // the wrong field (Appendix C rule 10).
    [Fact]
    public void Reports_a_structured_question_outside_dates_and_spend_as_unsupported()
    {
        var decision = _router.Route("What is the total contract value for our SAP agreement?");

        var plan = _planner.Plan(decision);

        var unsupported = Assert.IsType<DeterministicQuery.Unsupported>(plan);
        Assert.Equal(decision.Question, unsupported.Question);
        Assert.False(string.IsNullOrWhiteSpace(unsupported.Reason));
    }

    [Fact]
    public void Rejects_a_semantic_decision()
    {
        var decision = _router.Route("What liability do we have with AWS?");
        Assert.Equal(QueryIntent.Semantic, decision.Intent);

        var exception = Assert.Throws<ArgumentException>(() => _planner.Plan(decision));
        Assert.Equal("decision", exception.ParamName);
    }

    [Fact]
    public void Rejects_a_null_decision()
    {
        Assert.Throws<ArgumentNullException>(() => _planner.Plan(null!));
    }

    // AC-2 "Structured questions hit deterministic queries/filters (no LLM)": proves it
    // structurally, the same technique AskRaffaQueryRouterTests already uses for the router
    // itself — the planner type cannot call the AI Gateway because it holds no reference to it
    // anywhere (constructor parameter or field).
    [Fact]
    public void Planner_has_no_dependency_on_the_AI_Gateway()
    {
        var type = typeof(DeterministicQueryPlanner);

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
}
