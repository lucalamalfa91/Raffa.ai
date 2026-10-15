using Raffa.AiFlows.WebResearch.Lexicons;

namespace Raffa.AiFlows.Tests.WebResearch.Lexicons;

/// <summary>
/// LANG-03: the explicit web-request and procurement-topic lexicons in French, Spanish and German,
/// next to the Italian/English ones in <see cref="WebResearchTopicLexiconTests"/>.
/// </summary>
public sealed class WebResearchTopicLexiconMultilingualTests
{
    [Theory]
    [InlineData("cherche sur le web les pratiques du marché")]
    [InlineData("recherche sur internet les tarifs de Salesforce")]
    [InlineData("vérifie en ligne les prix")]
    [InlineData("cherche-le sur google")]
    [InlineData("recherche web des alternatives à Salesforce")]
    [InlineData("busca en la web las prácticas de mercado")]
    [InlineData("búsqueda en internet de tarifas")]
    [InlineData("comprueba en línea el precio")]
    [InlineData("busca en internet alternativas")]
    [InlineData("googlea esto por favor")]
    [InlineData("suche im Internet nach Preisen")]
    [InlineData("recherchiere im Web die Marktpreise")]
    [InlineData("such das online")]
    [InlineData("google das bitte")]
    [InlineData("schau im Internet nach")]
    public void An_explicit_web_request_in_french_spanish_or_german_is_recognised(string question) =>
        Assert.True(WebResearchTopicLexicon.IsExplicitRequest(question));

    [Theory]
    [InlineData("Les tarifs trouvés sur internet sont-ils fiables ?")]
    [InlineData("Quel est le préavis du contrat Allianz ?")]
    [InlineData("Los precios que vi en internet eran más bajos")]
    [InlineData("¿Cuándo vence el contrato?")]
    [InlineData("Im Internet stehen andere Preise als in unserem Vertrag")]
    [InlineData("Wann endet die Kündigungsfrist?")]
    public void An_ordinary_question_is_not_an_explicit_web_request(string question) =>
        Assert.False(WebResearchTopicLexicon.IsExplicitRequest(question));

    [Theory]
    [InlineData("dernières actualités sur le rachat de Salesforce", WebResearchTopicLexicon.SupplierNews)]
    [InlineData("fourchette de prix typique pour une licence SaaS", WebResearchTopicLexicon.BenchmarkRange)]
    [InlineData("tactiques de négociation pour un renouvellement", WebResearchTopicLexicon.NegotiationTactics)]
    [InlineData("bonnes pratiques pour un contrat SaaS", WebResearchTopicLexicon.MarketPractice)]
    [InlineData("combien coûte une licence cloud par utilisateur", WebResearchTopicLexicon.BenchmarkRange)]
    [InlineData("últimas noticias sobre la adquisición de Salesforce", WebResearchTopicLexicon.SupplierNews)]
    [InlineData("rango de precios habitual para una licencia SaaS", WebResearchTopicLexicon.BenchmarkRange)]
    [InlineData("estrategias de negociación para una renovación", WebResearchTopicLexicon.NegotiationTactics)]
    [InlineData("buenas prácticas de mercado para contratos de nube", WebResearchTopicLexicon.MarketPractice)]
    [InlineData("cuánto cuesta una suscripción cloud", WebResearchTopicLexicon.BenchmarkRange)]
    [InlineData("aktuelle Nachrichten zur Übernahme von Salesforce", WebResearchTopicLexicon.SupplierNews)]
    [InlineData("Preisspanne für eine SaaS-Lizenz", WebResearchTopicLexicon.BenchmarkRange)]
    [InlineData("Verhandlungstaktiken für die Verlängerung", WebResearchTopicLexicon.NegotiationTactics)]
    [InlineData("Marktpraxis bei Lizenzen", WebResearchTopicLexicon.MarketPractice)]
    [InlineData("was kostet eine Cloud-Lizenz", WebResearchTopicLexicon.BenchmarkRange)]
    public void A_procurement_question_in_french_spanish_or_german_gets_its_purpose(string question, string purpose) =>
        Assert.Equal(purpose, WebResearchTopicLexicon.Classify(question));

    [Theory]
    [InlineData("quelle est la capitale de la France ?")]
    [InlineData("¿cuál es la capital de Francia?")]
    [InlineData("Wie hoch ist der Eiffelturm?")]
    [InlineData("quel est le négoce principal de la région ?")]
    [InlineData("¿qué negocio abre hoy en mi calle?")]
    public void An_off_topic_question_in_french_spanish_or_german_is_never_offered_the_web(string question) =>
        Assert.Null(WebResearchTopicLexicon.Classify(question));
}
