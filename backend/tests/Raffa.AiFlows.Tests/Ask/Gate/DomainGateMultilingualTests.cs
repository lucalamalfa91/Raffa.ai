using Raffa.AiFlows.Ask.Gate;
using Raffa.AiFlows.Ask.Routing;
using Raffa.Chat.Domain;

namespace Raffa.AiFlows.Tests.Ask.Gate;

/// <summary>
/// LANG-03: the gate lexicons in French, Spanish and German, next to the Italian/English ones in
/// <see cref="DomainGateTests"/>. Each lexicon has about five positive and three negative phrases per
/// language; the negatives are the in-domain sentences a looser pattern would misroute.
/// </summary>
public sealed class DomainGateMultilingualTests
{
    private readonly DomainGate _gate = new();

    private static readonly KnownSupplierName[] NoKnownSuppliers = [];
    private static readonly KnownSupplierName[] KnownAllianz = [new("Allianz", "allianz")];

    private GateLabel Label(string question, KnownSupplierName[]? known = null) =>
        _gate.Classify(question, known ?? NoKnownSuppliers).Label;

    [Theory]
    [InlineData("bonjour")]
    [InlineData("Bonsoir à tous")]
    [InlineData("salut")]
    [InlineData("hola")]
    [InlineData("Buenos días")]
    [InlineData("buenas tardes")]
    [InlineData("hallo")]
    [InlineData("Guten Morgen")]
    [InlineData("Guten Tag")]
    public void Greetings_in_french_spanish_and_german_are_greeting(string question) =>
        Assert.Equal(GateLabel.Greeting, Label(question));

    [Theory]
    [InlineData("Quand expire le contrat avec Allianz ?")]
    [InlineData("¿Cuándo vence el contrato con Allianz?")]
    [InlineData("Wann endet der Vertrag mit Allianz?")]
    public void An_in_domain_question_is_not_a_greeting(string question) =>
        Assert.Equal(GateLabel.InDomain, Label(question, KnownAllianz));

    [Theory]
    [InlineData("Donne-moi une recette de cuisine italienne")]
    [InlineData("Quelle est la météo demain ?")]
    [InlineData("Raconte-moi une blague")]
    [InlineData("Je voudrais cuisiner des pâtes")]
    [InlineData("Une photo de ma grand-mère")]
    [InlineData("Dame una receta de paella")]
    [InlineData("Dime el pronóstico del tiempo para mañana")]
    [InlineData("Cuéntame un chiste")]
    [InlineData("¿Quién ganó en el fútbol ayer?")]
    [InlineData("Una foto de mi abuela")]
    [InlineData("Rezept für Spaghetti")]
    [InlineData("Wie wird das Wetter morgen?")]
    [InlineData("Erzähl mir einen Witz")]
    [InlineData("Wer gewinnt am Samstag im Fußball?")]
    [InlineData("Ein Foto von meiner Oma")]
    public void Leisure_and_small_talk_in_french_spanish_and_german_is_off_domain(string question) =>
        Assert.Equal(GateLabel.OffDomain, Label(question));

    [Theory]
    [InlineData("Quelle est la recette annuelle du fournisseur Allianz ?")]
    [InlineData("¿Cuál es el tiempo de entrega pactado con Allianz?")]
    [InlineData("Wie hoch ist der Preis pro Lizenz bei Allianz?")]
    public void A_procurement_question_that_shares_a_word_with_the_leisure_lexicon_stays_in_domain(string question) =>
        Assert.Equal(GateLabel.InDomain, Label(question, KnownAllianz));

    [Theory]
    [InlineData("Puis-je poursuivre en justice mon fournisseur ?")]
    [InlineData("J'ai besoin d'un avocat pour ce contrat")]
    [InlineData("Cette clause est-elle juridiquement valable ?")]
    [InlineData("Quel conseil juridique pour la résiliation ?")]
    [InlineData("Peut-on assigner en justice le fournisseur ?")]
    [InlineData("¿Puedo demandar a mi proveedor?")]
    [InlineData("Necesito un abogado para este contrato")]
    [InlineData("¿Es legalmente válida esta cláusula?")]
    [InlineData("Quiero asesoramiento legal sobre el contrato")]
    [InlineData("¿Acabará en los tribunales?")]
    [InlineData("Kann ich den Lieferanten verklagen?")]
    [InlineData("Ich brauche einen Anwalt für den Vertrag")]
    [InlineData("Ist diese Klausel rechtlich bindend?")]
    [InlineData("Brauche ich Rechtsberatung zur Kündigung?")]
    [InlineData("Können wir vor Gericht gehen?")]
    public void Legal_advice_requests_in_french_spanish_and_german_are_legal(string question) =>
        Assert.Equal(GateLabel.Legal, Label(question));

    [Theory]
    [InlineData("Quelles sont les clauses de responsabilité du contrat Allianz ?")]
    [InlineData("¿Qué cláusulas de responsabilidad tiene el contrato con Allianz?")]
    [InlineData("Welche Haftungsgrenze gilt im Vertrag mit Allianz?")]
    public void A_clause_question_is_not_legal_advice(string question) =>
        Assert.Equal(GateLabel.InDomain, Label(question, KnownAllianz));

    [Theory]
    [InlineData("Que peux-tu faire ?")]
    [InlineData("Que savez-vous faire ?")]
    [InlineData("Comment faire pour importer un fichier ?")]
    [InlineData("Aide-moi")]
    [InlineData("J'ai besoin d'aide")]
    [InlineData("¿Qué puedes hacer?")]
    [InlineData("¿Cómo puedo subir un archivo?")]
    [InlineData("Ayúdame")]
    [InlineData("Necesito ayuda")]
    [InlineData("¿Cómo hago para ver mis documentos?")]
    [InlineData("Was kannst du?")]
    [InlineData("Was können Sie für mich tun?")]
    [InlineData("Wie kann ich eine Datei hochladen?")]
    [InlineData("Hilf mir bitte")]
    [InlineData("Ich brauche Hilfe")]
    public void How_to_and_help_requests_in_french_spanish_and_german_are_capability(string question) =>
        Assert.Equal(GateLabel.Capability, Label(question));

    [Theory]
    [InlineData("Comment puis-je économiser sur le renouvellement ?")]
    [InlineData("¿Cómo puedo ahorrar en la renovación?")]
    [InlineData("Wie kann ich Kosten senken?")]
    [InlineData("À l'aide du contrat Allianz, quand expire-t-il ?")]
    [InlineData("Con la ayuda del contrato de Allianz, ¿cuándo vence?")]
    [InlineData("Mit Hilfe des Vertrags: Wann endet er bei Allianz?")]
    public void A_how_to_opening_with_a_savings_signal_or_a_borrowed_help_word_stays_in_domain(string question) =>
        Assert.Equal(GateLabel.InDomain, Label(question, KnownAllianz));

    [Theory]
    [InlineData("¿Cuándo vence el contrato?")]
    [InlineData("¿Puedo ver los documentos pendientes?")]
    [InlineData("¡Cuántos contratos tenemos!")]
    public void Spanish_inverted_punctuation_does_not_turn_the_first_word_into_a_supplier(string question) =>
        Assert.Equal(GateLabel.InDomain, Label(question));

    [Theory]
    [InlineData("Wann endet die Kündigungsfrist?")]
    [InlineData("Welche Verträge laufen aus?")]
    [InlineData("Können Sie mir die Laufzeit nennen?")]
    [InlineData("Wo sind die Einsparungen am größten?")]
    public void German_capitalised_nouns_and_the_formal_you_are_not_supplier_names(string question) =>
        Assert.Equal(GateLabel.InDomain, Label(question));

    [Fact]
    public void A_german_question_naming_an_unknown_supplier_still_needs_a_document()
    {
        var result = _gate.Classify("Wann endet der Vertrag mit Databricks?", NoKnownSuppliers);

        Assert.Equal(GateLabel.NeedsDocument, result.Label);
        Assert.Equal("Databricks", result.NamedSupplier);
    }
}
