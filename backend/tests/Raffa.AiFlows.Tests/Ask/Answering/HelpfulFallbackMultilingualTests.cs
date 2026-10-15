using Raffa.AiFlows.Ask.Answering;
using Raffa.Chat.Application.Capabilities;

namespace Raffa.AiFlows.Tests.Ask.Answering;

/// <summary>
/// LANG-03: the question-area cues of <see cref="HelpfulFallbackAnswer.AreaFor"/> in French, Spanish
/// and German, next to the Italian/English ones in <see cref="HelpfulFallbackAnswerTests"/>.
/// </summary>
public sealed class HelpfulFallbackMultilingualTests
{
    [Theory]
    [InlineData("Quand expire le préavis ?", CapabilityCatalog.RenewalsKey)]
    [InlineData("Comment faire des économies ?", CapabilityCatalog.SavingsKey)]
    [InlineData("Ce prix est-il correct ?", CapabilityCatalog.QuoteCheckKey)]
    [InlineData("Quelle est la clause de pénalité ?", CapabilityCatalog.ContractDetailKey)]
    [InlineData("Où sont mes documents ?", CapabilityCatalog.DocumentsKey)]
    [InlineData("¿Cuándo vence el contrato?", CapabilityCatalog.RenewalsKey)]
    [InlineData("¿Cómo puedo ahorrar?", CapabilityCatalog.SavingsKey)]
    [InlineData("¿Es caro el precio?", CapabilityCatalog.QuoteCheckKey)]
    [InlineData("¿Qué dice la cláusula de responsabilidad?", CapabilityCatalog.ContractDetailKey)]
    [InlineData("Quiero ver mis documentos", CapabilityCatalog.DocumentsKey)]
    [InlineData("Wann läuft die Kündigungsfrist ab?", CapabilityCatalog.RenewalsKey)]
    [InlineData("Wie kann ich einsparen?", CapabilityCatalog.SavingsKey)]
    [InlineData("Ist der Preis marktüblich?", CapabilityCatalog.QuoteCheckKey)]
    [InlineData("Welche Haftung gilt?", CapabilityCatalog.ContractDetailKey)]
    [InlineData("Wo ist das Dokument?", CapabilityCatalog.DocumentsKey)]
    public void The_area_follows_the_french_spanish_or_german_words(string question, string expected) =>
        Assert.Equal(expected, HelpfulFallbackAnswer.AreaFor(question));

    [Theory]
    [InlineData("Quelle heure est-il ?")]
    [InlineData("¿Qué hora es?")]
    [InlineData("Wie spät ist es?")]
    public void An_unrelated_question_has_no_area(string question) =>
        Assert.Null(HelpfulFallbackAnswer.AreaFor(question));

    [Theory]
    [InlineData("Rédige un brouillon pour le fournisseur")]
    [InlineData("Escribe un borrador para el proveedor")]
    [InlineData("Schreibe einen Entwurf für den Lieferanten")]
    public void A_request_to_write_something_gets_the_supplier_draft(string question) =>
        Assert.Equal(
            HelpfulFallbackAnswer.Proposal("write a draft for the supplier", null),
            HelpfulFallbackAnswer.Proposal(question, null));
}
