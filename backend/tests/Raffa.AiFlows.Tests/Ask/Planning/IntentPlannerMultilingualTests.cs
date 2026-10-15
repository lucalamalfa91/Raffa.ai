using Raffa.AiFlows.Ask.Planning;
using Raffa.Chat.Domain;

namespace Raffa.AiFlows.Tests.Ask.Planning;

/// <summary>
/// LANG-03: the planner lexicons in French, Spanish and German, next to the Italian/English ones in
/// <see cref="IntentPlannerTests"/>. About five positive and three negative phrases per language.
/// </summary>
public sealed class IntentPlannerMultilingualTests
{
    private readonly IntentPlanner _planner = new();

    [Theory]
    [InlineData("Quels contrats sont mal positionnés par rapport au marché ?", null, AskIntent.PortfolioMarketPosition)]
    [InlineData("Où puis-je économiser ?", null, AskIntent.PortfolioStrategy)]
    [InlineData("Quels leviers pour réduire les dépenses ?", null, AskIntent.PortfolioStrategy)]
    [InlineData("Comment aborder le renouvellement Salesforce ?", "Salesforce", AskIntent.RenewalStrategy)]
    [InlineData("Ce contrat est-il aligné avec le marché ?", "Salesforce", AskIntent.MarketCompare)]
    [InlineData("Quel est le préavis pour Salesforce ?", "Salesforce", AskIntent.StructuredFact)]
    [InlineData("Quels champs manquent ?", null, AskIntent.DocumentStatus)]
    [InlineData("Ouvre les documents", null, AskIntent.Navigate)]
    [InlineData("¿Qué contratos están mal posicionados respecto al mercado?", null, AskIntent.PortfolioMarketPosition)]
    [InlineData("¿Dónde puedo ahorrar?", null, AskIntent.PortfolioStrategy)]
    [InlineData("¿Cómo abordar la renovación de Salesforce?", "Salesforce", AskIntent.RenewalStrategy)]
    [InlineData("¿Está Salesforce alineado con el mercado?", "Salesforce", AskIntent.MarketCompare)]
    [InlineData("¿Cuál es el plazo de preaviso de Salesforce?", "Salesforce", AskIntent.StructuredFact)]
    [InlineData("¿Qué campos faltantes hay?", null, AskIntent.DocumentStatus)]
    [InlineData("Abre los documentos", null, AskIntent.Navigate)]
    [InlineData("Welche Verträge sind schlecht positioniert und zu teuer?", null, AskIntent.PortfolioMarketPosition)]
    [InlineData("Wo kann ich einsparen?", null, AskIntent.PortfolioStrategy)]
    [InlineData("Wie gehe ich die Verlängerung bei Salesforce an?", "Salesforce", AskIntent.RenewalStrategy)]
    [InlineData("Liegt Salesforce über dem Markt?", "Salesforce", AskIntent.MarketCompare)]
    [InlineData("Wie lang ist die Kündigungsfrist bei Salesforce?", "Salesforce", AskIntent.StructuredFact)]
    [InlineData("Welche Felder fehlen noch?", null, AskIntent.DocumentStatus)]
    [InlineData("Öffne die Dokumente", null, AskIntent.Navigate)]
    public void A_french_spanish_or_german_question_plans_to_the_same_intent_as_its_italian_twin(
        string question, string? supplier, AskIntent expected)
    {
        var result = _planner.Plan(question, supplier);

        Assert.Equal(expected, result.Intent);
    }

    [Theory]
    [InlineData("Quels sont les coûts du contrat Allianz ?", AskIntent.Savings)]
    [InlineData("Quels contrats se renouvellent dans les prochains 120 jours ?", AskIntent.RenewalStrategy)]
    [InlineData("Quel est le marché public de ce contrat ?", AskIntent.MarketCompare)]
    [InlineData("¿Cuánto cuestan los costes del contrato con Allianz?", AskIntent.Savings)]
    [InlineData("¿Cuándo vencen los contratos?", AskIntent.RenewalStrategy)]
    [InlineData("¿Qué negocio principal tiene Allianz?", AskIntent.RenewalStrategy)]
    [InlineData("Welche Kosten hat der Vertrag mit Allianz?", AskIntent.Savings)]
    [InlineData("Wann läuft der Vertrag aus?", AskIntent.RenewalStrategy)]
    [InlineData("Wer ist der Ansprechpartner im Vertrag mit Allianz?", AskIntent.MarketCompare)]
    public void A_plain_question_that_shares_a_word_with_a_lexicon_does_not_take_that_intent(
        string question, AskIntent notExpected)
    {
        var result = _planner.Plan(question, "Allianz");

        Assert.NotEqual(notExpected, result.Intent);
    }

    [Theory]
    [InlineData("Quel est le préavis ?")]
    [InlineData("Quel est le délai de préavis ?")]
    [InlineData("Quelle est la date limite de préavis ?")]
    [InlineData("Notification de non-renouvellement")]
    [InlineData("Date limite de résiliation")]
    [InlineData("¿Cuál es el preaviso?")]
    [InlineData("Plazo de preaviso del contrato")]
    [InlineData("Fecha límite de preaviso")]
    [InlineData("Notificación de no renovación")]
    [InlineData("Fecha límite de cancelación")]
    [InlineData("Wie lang ist die Kündigungsfrist?")]
    [InlineData("Kuendigungsfrist des Vertrags")]
    [InlineData("Wann ist der Kündigungstermin?")]
    [InlineData("Nichtverlängerung mitteilen")]
    [InlineData("Nicht-Verlängerung")]
    public void The_notice_lexicon_covers_the_glossary_terms(string question) =>
        Assert.True(IntentPlanner.IsNoticeQuestion(question));

    [Theory]
    [InlineData("Résiliation pour faute")]
    [InlineData("Rescisión por incumplimiento")]
    [InlineData("Kündigung aus wichtigem Grund")]
    [InlineData("Verlängerung des Vertrags")]
    public void Termination_without_a_notice_term_is_not_a_notice_question(string question) =>
        Assert.False(IntentPlanner.IsNoticeQuestion(question));

    [Theory]
    [InlineData("¿Dónde puedo ahorrar?", "no me has respondido")]
    [InlineData("Où puis-je économiser ?", "Tu n'as pas répondu")]
    [InlineData("Wo kann ich einsparen?", "Du hast nicht geantwortet")]
    [InlineData("¿Dónde puedo ahorrar?", "y entonces")]
    [InlineData("Où puis-je économiser ?", "explique-moi")]
    [InlineData("Wo kann ich einsparen?", "erkläre mir das genauer")]
    public void A_french_spanish_or_german_follow_up_inherits_the_previous_turn(string previous, string followUp)
    {
        var result = _planner.Plan(followUp, namedSupplier: null, previousUserQuestion: previous);

        Assert.Equal(IntentPlanBasis.FollowUp, result.Basis);
        Assert.Equal(AskIntent.PortfolioStrategy, result.Intent);
    }

    [Theory]
    [InlineData("comment ?")]
    [InlineData("cómo?")]
    [InlineData("warum?")]
    public void A_bare_interrogative_in_french_spanish_or_german_is_a_follow_up(string followUp)
    {
        var result = _planner.Plan(followUp, null, previousUserQuestion: "Where can we save?");

        Assert.Equal(IntentPlanBasis.FollowUp, result.Basis);
    }

    [Theory]
    [InlineData("Quels contrats arrivent à échéance cette année et pourquoi aucun ne bouge en mars ?")]
    [InlineData("¿Cuántos contratos hay y por qué hay tantos proveedores distintos en el portfolio?")]
    [InlineData("Wie viele Verträge gibt es und warum sind so viele Lieferanten im Portfolio vertreten, bitte nenne mir alle?")]
    public void A_long_question_is_never_a_follow_up(string question)
    {
        var result = _planner.Plan(question, null, previousUserQuestion: "Where can we save?");

        Assert.NotEqual(IntentPlanBasis.FollowUp, result.Basis);
    }
}
