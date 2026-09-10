using Raffa.Chat.Application.Planning;
using Raffa.Chat.Domain;

namespace Raffa.Chat.Tests.Planning;

/// <summary>
/// Proves task E13/F06/US01/T01's planner (ask-engine coding objective point 2; `inputs/requirements.md`
/// R-ASK-03; ADR-024 "planner (fixed intents)"): every <see cref="GateLabel.InDomain"/> question
/// maps onto exactly one of the nine fixed <see cref="AskIntent"/> values, in the declared lexicon
/// order. Includes this task's own Definition of Done scenario — "Is my Allianz contract above
/// market?" plans to <see cref="AskIntent.MarketCompare"/> once <see cref="Gate.DomainGate"/> (see
/// <c>Gate.DomainGateTests</c>) has already resolved the named supplier.
/// </summary>
public sealed class IntentPlannerTests
{
    private readonly IntentPlanner _planner = new();

    [Fact]
    public void Allianz_above_market_with_a_known_supplier_plans_to_market_compare()
    {
        var result = _planner.Plan("Is my Allianz contract above market?", "Allianz");

        Assert.Equal(AskIntent.MarketCompare, result.Intent);
        Assert.Equal("Allianz", result.NamedSupplier);
    }

    [Fact]
    public void A_benchmark_question_with_no_named_supplier_routes_to_quote_check_instead_of_narrating()
    {
        var result = _planner.Plan("Is this in line with the market?", namedSupplier: null);

        Assert.Equal(AskIntent.QuoteRoute, result.Intent);
    }

    [Fact]
    public void An_approach_the_renewal_question_plans_to_renewal_strategy()
    {
        var result = _planner.Plan("How should I approach the renewal?", "Salesforce");

        Assert.Equal(AskIntent.RenewalStrategy, result.Intent);
    }

    [Fact]
    public void A_priority_question_scoped_to_a_named_supplier_plans_to_renewal_strategy()
    {
        var result = _planner.Plan("Why is this contract first?", "Salesforce");

        Assert.Equal(AskIntent.RenewalStrategy, result.Intent);
        Assert.Equal("Salesforce", result.NamedSupplier);
    }

    [Fact]
    public void A_priority_question_with_no_named_supplier_plans_to_portfolio_strategy()
    {
        var result = _planner.Plan("Which contracts are most critical?", namedSupplier: null);

        Assert.Equal(AskIntent.PortfolioStrategy, result.Intent);
    }

    [Fact]
    public void A_savings_question_scoped_to_a_named_supplier_plans_to_savings()
    {
        var result = _planner.Plan("Where is the largest saving on this contract?", "AWS");

        Assert.Equal(AskIntent.Savings, result.Intent);
    }

    [Fact]
    public void A_savings_question_with_no_named_supplier_plans_to_portfolio_strategy()
    {
        var result = _planner.Plan("Where are the biggest savings?", namedSupplier: null);

        Assert.Equal(AskIntent.PortfolioStrategy, result.Intent);
    }

    [Fact]
    public void A_document_status_question_plans_to_document_status()
    {
        var result = _planner.Plan("Which documents are not askable yet?", namedSupplier: null);

        Assert.Equal(AskIntent.DocumentStatus, result.Intent);
    }

    [Fact]
    public void A_bare_navigation_request_plans_to_navigate()
    {
        var result = _planner.Plan("Take me to Renewals", namedSupplier: null);

        Assert.Equal(AskIntent.Navigate, result.Intent);
    }

    [Fact]
    public void A_clause_level_question_falls_through_to_the_legacy_semantic_router_and_plans_to_clause()
    {
        var result = _planner.Plan("What is the liability clause?", namedSupplier: null);

        Assert.Equal(AskIntent.Clause, result.Intent);
    }

    [Fact]
    public void A_validated_field_question_falls_through_to_the_legacy_structured_router_and_plans_to_structured_fact()
    {
        var result = _planner.Plan("When does this contract expire?", namedSupplier: null);

        Assert.Equal(AskIntent.StructuredFact, result.Intent);
    }

    [Fact]
    public void Plan_rejects_a_null_question()
    {
        Assert.Throws<ArgumentNullException>(() => _planner.Plan(null!, namedSupplier: null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Plan_rejects_a_blank_question(string question)
    {
        Assert.Throws<ArgumentException>(() => _planner.Plan(question, namedSupplier: null));
    }
}
