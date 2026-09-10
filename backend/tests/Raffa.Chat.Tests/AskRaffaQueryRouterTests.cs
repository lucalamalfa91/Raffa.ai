using System.Reflection;
using Raffa.Chat.Application;
using Raffa.Chat.Domain;

namespace Raffa.Chat.Tests;

/// <summary>
/// Proves the Definition of Done for task E02/F04/US01/T01 (us-01-query-router): "dotnet test
/// proves routing for the four spec §8.3 examples" (parent story DoD), AC-1 (classifies the
/// spec §8.3 examples), AC-2 (structured questions never reach an LLM) and AC-3 (semantic
/// questions route to RAG retrieval).
/// </summary>
public sealed class AskRaffaQueryRouterTests
{
    private readonly AskRaffaQueryRouter _router = new();

    // Product spec §8.3's own example table — the exact four questions the parent story's
    // Definition of Done names.
    [Theory]
    [InlineData("Which contracts renew in the next 120 days?", QueryIntent.Structured)]
    [InlineData("What is our Microsoft annual spend?", QueryIntent.Structured)]
    [InlineData("What liability do we have with AWS?", QueryIntent.Semantic)]
    [InlineData("Which contracts contain unlimited liability?", QueryIntent.Semantic)]
    public void Classifies_the_spec_8_3_example_questions(string question, QueryIntent expected)
    {
        var decision = _router.Route(question);

        Assert.Equal(expected, decision.Intent);
        Assert.Equal(expected == QueryIntent.Structured, decision.RequiresDeterministicQuery);
        Assert.Equal(expected == QueryIntent.Semantic, decision.RequiresRagRetrieval);
    }

    // Proves the router generalizes from real field/clause vocabulary rather than matching the
    // four literal strings above verbatim.
    [Theory]
    [InlineData("How much do we spend annually with Acme Corp?", QueryIntent.Structured)]
    [InlineData("Which contracts expire in the next 30 days?", QueryIntent.Structured)]
    [InlineData("What is the total contract value for our SAP agreement?", QueryIntent.Structured)]
    [InlineData("Does our AWS contract cap liability?", QueryIntent.Semantic)]
    [InlineData("What confidentiality obligations exist in the IBM contract?", QueryIntent.Semantic)]
    [InlineData("What happens if we breach the termination clause with Salesforce?", QueryIntent.Semantic)]
    public void Generalizes_beyond_the_literal_example_strings(string question, QueryIntent expected)
    {
        Assert.Equal(expected, _router.Route(question).Intent);
    }

    [Fact]
    public void Classification_is_case_insensitive()
    {
        var decision = _router.Route("WHICH CONTRACTS RENEW IN THE NEXT 120 DAYS?");

        Assert.Equal(QueryIntent.Structured, decision.Intent);
    }

    [Fact]
    public void Defaults_an_unrecognized_question_to_semantic_retrieval()
    {
        // Appendix C rule 10: "If data quality is insufficient, return uncertainty instead of
        // fabricated precision" — a question the classifier cannot confidently place in the
        // structured branch must not be silently forced through a deterministic filter that does
        // not apply.
        var decision = _router.Route("Tell me about our relationship with Acme Corp.");

        Assert.Equal(QueryIntent.Semantic, decision.Intent);
        Assert.True(decision.RequiresRagRetrieval);
        Assert.False(decision.RequiresDeterministicQuery);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_an_empty_question(string question)
    {
        Assert.Throws<ArgumentException>(() => _router.Route(question));
    }

    [Fact]
    public void Rejects_a_null_question()
    {
        Assert.Throws<ArgumentNullException>(() => _router.Route(null!));
    }

    // AC-2 "Structured questions hit deterministic queries/filters (no LLM)": proves it
    // structurally, not just by absence of a bug today — the router type cannot call the AI
    // Gateway because it holds no reference to it anywhere (constructor parameter or field).
    [Fact]
    public void Router_has_no_dependency_on_the_AI_Gateway()
    {
        var type = typeof(AskRaffaQueryRouter);

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
