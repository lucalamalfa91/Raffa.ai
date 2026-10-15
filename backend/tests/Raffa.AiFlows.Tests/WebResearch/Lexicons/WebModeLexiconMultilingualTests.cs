using Raffa.AiFlows.WebResearch.Lexicons;

namespace Raffa.AiFlows.Tests.WebResearch.Lexicons;

/// <summary>
/// LANG-03: the web-mode leisure filter in French, Spanish and German, next to the Italian/English
/// ones in <see cref="WebModeLexiconTests"/>.
/// </summary>
public sealed class WebModeLexiconMultilingualTests
{
    [Theory]
    [InlineData("donne-moi une recette de cuisine italienne")]
    [InlineData("quelle est la météo ce week-end ?")]
    [InlineData("raconte-moi une blague")]
    [InlineData("qui a gagné le match hier soir ?")]
    [InlineData("quels films à regarder ce soir")]
    [InlineData("que voir à Lisbonne en deux jours")]
    [InlineData("dame una receta de paella")]
    [InlineData("pronóstico del tiempo para mañana")]
    [InlineData("cuéntame un chiste")]
    [InlineData("¿quién ganó el partido ayer?")]
    [InlineData("películas para ver esta noche")]
    [InlineData("qué ver en Lisboa")]
    [InlineData("Rezept für Gulasch")]
    [InlineData("Wie wird das Wetter morgen?")]
    [InlineData("Erzähl mir einen Witz")]
    [InlineData("Wer hat das Spiel gewonnen?")]
    [InlineData("Filme zum Anschauen heute Abend")]
    [InlineData("Sehenswürdigkeiten in Wien")]
    public void A_plainly_personal_or_leisure_question_is_off_context(string question) =>
        Assert.True(WebModeLexicon.IsOffContext(question));

    [Theory]
    [InlineData("dernières nouvelles sur les hausses de prix de Salesforce")]
    [InlineData("quelles sont les bonnes pratiques du marché pour renouveler un contrat SaaS ?")]
    [InlineData("les recettes fiscales de la France influencent-elles les tarifs de Orange ?")]
    [InlineData("últimas noticias sobre la subida de precios de Salesforce")]
    [InlineData("¿cuáles son las tarifas de mercado para licencias SaaS?")]
    [InlineData("el tiempo de entrega de los proveedores de nube")]
    [InlineData("Aktuelle Nachrichten zu Preiserhöhungen bei Salesforce")]
    [InlineData("Was ist der übliche Rabatt bei SaaS-Verträgen?")]
    [InlineData("Wie lange ist die Lieferzeit bei Cloud-Anbietern?")]
    public void A_work_question_is_never_off_context(string question) =>
        Assert.False(WebModeLexicon.IsOffContext(question));

    [Theory]
    [InlineData("impact de la météo sur la logistique des fournisseurs")]
    [InlineData("impacto del pronóstico del tiempo en la logística de los proveedores")]
    [InlineData("Einfluss des Wetterberichts auf die Logistik der Lieferanten")]
    public void A_leisure_word_next_to_a_work_word_stays_a_work_question(string question) =>
        Assert.False(WebModeLexicon.IsOffContext(question));
}
