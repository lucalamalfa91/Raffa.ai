using Raffa.AiFlows.Ask.Interview;
using Raffa.AiFlows.Ask.Planning;
using Raffa.Chat.Application.Interview;

namespace Raffa.AiFlows.Tests.Ask.Interview;

/// <summary>
/// LANG-03: the vague/deictic and domain-noun lexicons and the ambiguous-abstain recogniser in French,
/// Spanish and German, next to the Italian/English ones in <see cref="AmbiguityDetectorTests"/>.
/// </summary>
public sealed class AmbiguityDetectorMultilingualTests
{
    private readonly IntentPlanner _planner = new();
    private readonly InterviewOptions _options = new();

    private AmbiguitySignals Detect(string question) =>
        AmbiguityDetector.Detect(
            question, _planner.Plan(question, null), new InterviewContext(false, 0, false, false, false), _options);

    [Theory]
    [InlineData("Fais-moi une synthèse de tous mes contrats")]
    [InlineData("Donne-moi une vue d'ensemble")]
    [InlineData("Peux-tu résumer ?")]
    [InlineData("En général, où en sommes-nous ?")]
    [InlineData("Ceci, qu'en penses-tu ?")]
    [InlineData("Dame un resumen general")]
    [InlineData("Una visión general de todos mis contratos")]
    [InlineData("En general, ¿cómo vamos?")]
    [InlineData("Esto, ¿qué opinas?")]
    [InlineData("Necesito una panorámica")]
    [InlineData("Gib mir einen Überblick")]
    [InlineData("Eine Zusammenfassung bitte")]
    [InlineData("Alle meine Verträge bitte")]
    [InlineData("Insgesamt, wie sieht es aus?")]
    [InlineData("Dies, was meinst du?")]
    public void A_vague_french_spanish_or_german_request_is_flagged_as_vague(string question) =>
        Assert.True(Detect(question).VagueOrDeictic);

    [Theory]
    [InlineData("Donne-moi une vue d'ensemble des pénalités")]
    [InlineData("Dame un resumen de las cláusulas de responsabilidad")]
    [InlineData("Gib mir einen Überblick über die Haftung")]
    public void A_vague_opening_with_a_domain_noun_is_not_vague(string question) =>
        Assert.False(Detect(question).VagueOrDeictic);

    [Theory]
    [InlineData("Quelle est la clause de responsabilité ?")]
    [InlineData("¿Cuál es el precio por usuario?")]
    [InlineData("Wie hoch ist die Vertragsstrafe?")]
    public void A_domain_noun_in_french_spanish_or_german_keeps_a_question_out_of_the_interview(string question) =>
        Assert.NotEqual(AmbiguityVerdict.Ambiguous, Detect(question).Verdict);

    [Theory]
    [InlineData("La question est ambiguë : elle peut désigner la dépense la plus élevée.")]
    [InlineData("Ce n'est pas clair : plusieurs interprétations sont possibles.")]
    [InlineData("La demande est trop vague pour répondre.")]
    [InlineData("La pregunta es ambigua, no está claro qué quieres decir.")]
    [InlineData("La pregunta es demasiado vaga.")]
    [InlineData("Hay varias interpretaciones posibles.")]
    [InlineData("Die Frage ist mehrdeutig.")]
    [InlineData("Die Anfrage ist unklar.")]
    [InlineData("Das ist nicht eindeutig; es gibt mehrere mögliche Interpretationen.")]
    public void An_ambiguous_abstain_reason_in_french_spanish_or_german_is_recognised(string reason) =>
        Assert.True(AmbiguousAbstainDetector.IsAmbiguous(reason));

    [Theory]
    [InlineData("Aucun contrat validé ne permet de répondre de façon fiable.")]
    [InlineData("Ningún contrato validado permite una respuesta fiable.")]
    [InlineData("Die validierten Verträge enthalten dazu keine Angabe.")]
    public void Any_other_abstain_reason_in_french_spanish_or_german_is_not(string reason) =>
        Assert.False(AmbiguousAbstainDetector.IsAmbiguous(reason));
}
