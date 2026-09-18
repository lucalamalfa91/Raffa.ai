using Raffa.Chat.Application.Planning;
using Raffa.Chat.Domain;

namespace Raffa.Chat.Tests.Planning;

/// <summary>
/// Proves task E13/F06/US01/T01's planner (ask-engine coding objective point 2; `inputs/requirements.md`
/// R-ASK-03; ADR-024 "planner (fixed intents)"): every <see cref="GateLabel.InDomain"/> question
/// maps onto exactly one of the ten fixed <see cref="AskIntent"/> values, in the declared lexicon
/// order. Includes this task's own Definition of Done scenario — "Is my Allianz contract above
/// market?" plans to <see cref="AskIntent.MarketCompare"/> once <see cref="Gate.DomainGate"/> (see
/// <c>Gate.DomainGateTests</c>) has already resolved the named supplier.
///
/// <para>
/// Also proves task E27/F01/US01/T01 (NW-79, ADR-024 w19 cl. 13): the three "wow" screenshot
/// questions (IT + EN) route to their target intent — Q1 to
/// <see cref="AskIntent.PortfolioMarketPosition"/> (never <see cref="AskIntent.QuoteRoute"/>, even
/// when a supplier is already in scope — lock 4), Q2 to <see cref="AskIntent.StructuredFact"/>
/// (never <see cref="AskIntent.Clause"/>), Q3 to <see cref="AskIntent.RenewalStrategy"/> with the
/// named supplier echoed — and none of them falls through to unscoped
/// <see cref="AskIntent.Clause"/> RAG.
/// </para>
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
    public void Screenshot_Q1_italian_portfolio_mal_position_question_plans_to_portfolio_market_position()
    {
        var result = _planner.Plan(
            "quali contratti sono mal posizionati sul mercato? su quali posso lavorare per " +
            "risparmiare un po di soldi sull'anno 2026?",
            namedSupplier: null);

        Assert.Equal(AskIntent.PortfolioMarketPosition, result.Intent);
    }

    [Fact]
    public void Screenshot_Q1_english_paraphrase_plans_to_portfolio_market_position()
    {
        var result = _planner.Plan(
            "Which contracts are poorly positioned in the market? Which ones can I work on to " +
            "save money in 2026?",
            namedSupplier: null);

        Assert.Equal(AskIntent.PortfolioMarketPosition, result.Intent);
    }

    [Fact]
    public void Screenshot_Q1_still_plans_to_portfolio_market_position_when_asked_from_a_scoped_contract()
    {
        // Lock 4: "quali contratti" is a portfolio question even from an open contract 360 — the
        // gate may have already resolved a named supplier for that contract, but it must not steer
        // this intent to MarketCompare/QuoteRoute.
        var result = _planner.Plan(
            "quali contratti sono mal posizionati sul mercato? su quali posso lavorare per " +
            "risparmiare un po di soldi sull'anno 2026?",
            namedSupplier: "AsterCloud");

        Assert.Equal(AskIntent.PortfolioMarketPosition, result.Intent);
        Assert.Equal("AsterCloud", result.NamedSupplier);
    }

    [Fact]
    public void A_savings_question_that_also_names_the_market_reaches_portfolio_strategy_not_quote_route()
    {
        // Root cause NW-79 records: BenchmarkPattern used to be checked before SavingsPattern, so
        // a sentence naming "mercato" alongside "risparmiare" was stolen into unscoped QuoteRoute
        // before risparm* ever ran (this sentence has no "quali contratti"/"mal posizionati", so
        // PortfolioMarketPositionPattern does not fire — this is SavingsPattern's own fix, item b).
        var result = _planner.Plan("Rispetto al mercato, dove posso risparmiare?", namedSupplier: null);

        Assert.Equal(AskIntent.PortfolioStrategy, result.Intent);
    }

    [Fact]
    public void Screenshot_Q2_english_notice_question_plans_to_structured_fact_not_clause()
    {
        var result = _planner.Plan("When must we give notice to this supplier?", namedSupplier: null);

        Assert.Equal(AskIntent.StructuredFact, result.Intent);
    }

    [Fact]
    public void Screenshot_Q2_italian_preavviso_disdetta_question_plans_to_structured_fact_not_clause()
    {
        var result = _planner.Plan(
            "Quando dobbiamo dare il preavviso di disdetta a questo fornitore?", namedSupplier: null);

        Assert.Equal(AskIntent.StructuredFact, result.Intent);
    }

    [Fact]
    public void Screenshot_Q3_italian_astercloud_negotiation_question_plans_to_renewal_strategy()
    {
        var result = _planner.Plan(
            "sul contratto di AsterCloud GmbH quali sono i maggiori punti su cui posso " +
            "contrattare nel prossimo rinnovo?",
            "AsterCloud GmbH");

        Assert.Equal(AskIntent.RenewalStrategy, result.Intent);
        Assert.Equal("AsterCloud GmbH", result.NamedSupplier);
    }

    [Fact]
    public void Screenshot_Q3_english_paraphrase_plans_to_renewal_strategy()
    {
        var result = _planner.Plan("What can I negotiate on the AsterCloud renewal?", "AsterCloud");

        Assert.Equal(AskIntent.RenewalStrategy, result.Intent);
        Assert.Equal("AsterCloud", result.NamedSupplier);
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
