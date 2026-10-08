using System.Reflection;
using Raffa.AiGateway;
using Raffa.Chat.Application.Gaps;
using Raffa.Chat.Application.Planning;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Domain;

namespace Raffa.Chat.Tests.Gaps;

/// <summary>
/// INV-01 (decision D3): the investigator's deterministic trigger. T1 reads the planner's basis,
/// T2 reads the reply outcome, T3 reads the question against the versioned lexicon; none of them
/// can call a model. The labelled examples below are the unit-level half of the INV-04 set
/// (<c>Raffa.AiEval</c> runs the whole 150-phrase set and reports precision and recall).
/// </summary>
public sealed class InvestigatorTriggerTests
{
    private static readonly InvestigatorTrigger Trigger = new();

    private static IntentPlanResult Plan(IntentPlanBasis basis) =>
        new(AskIntent.StructuredFact, "test", null, null, basis, []);

    private static ReplyOutcome Outcome(ReplyKind kind, bool guard = false, bool fallback = false) => new(kind, guard, fallback);

    // ----- T1 -----

    [Fact]
    public void T1_fires_only_when_the_planner_recognised_no_intent()
    {
        var fallback = Trigger.Evaluate(Plan(IntentPlanBasis.Fallback), Outcome(ReplyKind.Answer), "xyz");
        Assert.True(fallback.T1);
        Assert.False(fallback.T2);
        Assert.False(fallback.T3);
        Assert.True(fallback.ShouldRun);
        Assert.Equal(InvestigatorTrigger.ReasonNoIntent, fallback.Reason);

        foreach (var basis in new[] { IntentPlanBasis.Lexicon, IntentPlanBasis.LegacyRouter, IntentPlanBasis.FollowUp, IntentPlanBasis.Forced })
        {
            var verdict = Trigger.Evaluate(Plan(basis), Outcome(ReplyKind.Answer), "quando scade il contratto con Amazon?");
            Assert.False(verdict.T1, basis.ToString());
            Assert.False(verdict.ShouldRun, basis.ToString());
        }
    }

    [Fact]
    public void T1_is_not_evaluated_before_the_planner_ran()
    {
        var verdict = Trigger.Evaluate(plan: null, replyOutcome: null, "quando scade il contratto con Amazon?");

        Assert.False(verdict.T1);
        Assert.False(verdict.ShouldRun);
        Assert.Equal(InvestigatorTrigger.ReasonNone, verdict.Reason);
    }

    // ----- T2 -----

    [Theory]
    [InlineData(ReplyKind.Abstain, false, false, InvestigatorTrigger.ReasonAbstain)]
    [InlineData(ReplyKind.Abstain, true, false, InvestigatorTrigger.ReasonGuardDowngrade)]
    [InlineData(ReplyKind.Abstain, true, true, InvestigatorTrigger.ReasonGuardDowngrade)]
    [InlineData(ReplyKind.Answer, true, true, InvestigatorTrigger.ReasonFallbackAnswer)]
    [InlineData(ReplyKind.Answer, false, true, InvestigatorTrigger.ReasonFallbackAnswer)]
    public void T2_fires_when_Raffa_could_not_answer(ReplyKind kind, bool guard, bool fallback, string reason)
    {
        var verdict = Trigger.Evaluate(Plan(IntentPlanBasis.Lexicon), Outcome(kind, guard, fallback), "qual è la spesa con Amazon?");

        Assert.True(verdict.T2);
        Assert.False(verdict.T1);
        Assert.True(verdict.ShouldRun);
        Assert.Equal(reason, verdict.Reason);
    }

    [Theory]
    [InlineData(ReplyKind.Answer, false, false)]
    [InlineData(ReplyKind.Answer, true, false)] // the guard retried and the model's second answer stood
    [InlineData(ReplyKind.Redirect, false, false)]
    [InlineData(ReplyKind.Interview, false, false)]
    [InlineData(ReplyKind.Draft, true, false)]
    [InlineData(ReplyKind.Refusal, false, false)]
    public void T2_stays_quiet_when_Raffa_answered_or_routed(ReplyKind kind, bool guard, bool fallback)
    {
        var verdict = Trigger.Evaluate(Plan(IntentPlanBasis.Lexicon), Outcome(kind, guard, fallback), "qual è la spesa con Amazon?");

        Assert.False(verdict.T2);
        Assert.False(verdict.ShouldRun);
    }

    [Fact]
    public void The_reasons_of_every_trigger_that_fired_are_listed_and_the_verdict_runs()
    {
        var verdict = Trigger.Evaluate(
            Plan(IntentPlanBasis.Fallback), Outcome(ReplyKind.Abstain), "Puoi generare un report mensile per il CFO?");

        Assert.True(verdict.T1);
        Assert.True(verdict.T2);
        Assert.True(verdict.T3);
        Assert.Equal("it", verdict.T3Language);
        Assert.Equal(
            $"{InvestigatorTrigger.ReasonNoIntent}+{InvestigatorTrigger.ReasonAbstain}+{InvestigatorTrigger.ReasonOperationalRequest}",
            verdict.Reason);
    }

    // ----- T3: labelled examples in the five languages -----

    public static TheoryData<string, string> OperationalRequests => new()
    {
        { "it", "Puoi generare un report mensile sui risparmi?" },
        { "it", "Genera una presentazione per il comitato" },
        { "it", "Per favore pianifica ogni lunedì un controllo del portafoglio" },
        { "it", "Potresti automatizzare l'invio del riepilogo ai responsabili?" },
        { "it", "È possibile condividere questa analisi con il controllo di gestione?" },
        { "it", "Confronta i contratti cloud e fammi una tabella" },
        { "it", "Riesci a scaricare i PDF dei contratti?" },
        { "it", "Programma un aggiornamento settimanale dei dati" },
        { "en", "Can you create a dashboard with the spend by supplier?" },
        { "en", "Generate a slide deck for the board" },
        { "en", "Please schedule a weekly refresh of the portfolio" },
        { "en", "Is it possible to automate the monthly summary?" },
        { "en", "Could you download all the contract PDFs?" },
        { "en", "Compare my cloud contracts and give me a summary table" },
        { "en", "Would you update the renewal dates automatically?" },
        { "fr", "Peux-tu préparer un rapport mensuel pour la direction ?" },
        { "fr", "Génère une présentation pour le conseil" },
        { "fr", "Est-il possible d'automatiser le récapitulatif mensuel ?" },
        { "fr", "Planifie chaque lundi une actualisation du portefeuille" },
        { "es", "¿Puedes preparar un informe mensual para la dirección?" },
        { "es", "Genera una presentación para el consejo" },
        { "es", "¿Es posible automatizar el resumen mensual?" },
        { "es", "Descarga todos los PDF de los contratos" },
        { "de", "Kannst du einen Bericht über die Einsparungen erstellen?" },
        { "de", "Erstelle eine Präsentation für den Vorstand" },
        { "de", "Ist es möglich, die Zusammenfassung zu automatisieren?" },
        { "de", "Lade alle PDF-Dateien in einen Ordner herunter" },
    };

    public static TheoryData<string, string> NotOperationalRequests => new()
    {
        // ordinary questions Raffa answers
        { "it", "Quando scade il contratto con Amazon?" },
        { "it", "Puoi dirmi quanto abbiamo speso con Salesforce nel 2025?" },
        { "it", "Riesci a spiegarmi la clausola di rinnovo?" },
        { "en", "When does the Amazon contract expire?" },
        { "en", "Can you tell me how much we spent in 2025?" },
        { "fr", "Quand expire le contrat avec Amazon ?" },
        { "es", "¿Cuándo vence el contrato con Amazon?" },
        { "de", "Wann läuft der Vertrag mit Amazon aus?" },
        // F4-D01 hard negatives: the verb or the deliverable belongs to somebody else
        { "it", "Il fornitore deve inviare la fattura entro 30 giorni?" },
        { "it", "Entro quando devo inviare la disdetta?" },
        { "it", "Il fornitore può aggiornare unilateralmente i prezzi?" },
        { "it", "Qual è il testo della clausola sul report mensile?" },
        { "it", "I contatti del venditore per generare un ticket sono nel contratto?" },
        { "en", "Does the provider have to send the invoice within 30 days?" },
        { "en", "By when do I have to send the termination notice?" },
        { "en", "What does the clause say about the monthly report?" },
        { "en", "The price list updates every year, right?" },
        { "en", "What does the schedule of payments say?" },
        { "fr", "Le fournisseur doit-il envoyer la facture sous 30 jours ?" },
        { "fr", "Que dit la clause sur le rapport mensuel ?" },
        { "es", "¿Debe el proveedor enviar la factura en 30 días?" },
        { "es", "¿Qué dice la cláusula sobre el informe mensual?" },
        { "de", "Muss der Lieferant die Rechnung innerhalb von 30 Tagen senden?" },
        { "de", "Was steht in der Klausel zum Servicebericht?" },
    };

    [Theory]
    [MemberData(nameof(OperationalRequests))]
    public void T3_fires_on_an_operational_request_in_every_language(string language, string question)
    {
        var verdict = Trigger.Evaluate(plan: null, replyOutcome: null, question);

        Assert.True(verdict.T3, question);
        Assert.True(verdict.ShouldRun, question);
        Assert.Equal(language, verdict.T3Language);
        Assert.Equal(InvestigatorTrigger.ReasonOperationalRequest, verdict.Reason);
    }

    [Theory]
    [MemberData(nameof(NotOperationalRequests))]
    public void T3_stays_quiet_on_an_ordinary_or_third_party_question(string language, string question)
    {
        var verdict = Trigger.Evaluate(plan: null, replyOutcome: null, question);

        Assert.False(verdict.T3, $"[{language}] {question} ({verdict.Reason})");
        Assert.False(verdict.ShouldRun);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_question_fires_nothing(string question)
    {
        var verdict = Trigger.Evaluate(plan: null, replyOutcome: null, question);

        Assert.False(verdict.ShouldRun);
        Assert.Equal(InvestigatorTrigger.ReasonNone, verdict.Reason);
    }

    [Fact]
    public void The_user_addressing_Raffa_after_naming_a_contract_is_still_a_request()
    {
        var verdict = Trigger.Evaluate(plan: null, replyOutcome: null, "Sul contratto Amazon puoi esportare i dati in un file?");

        Assert.True(verdict.T3);
    }

    // ----- no model call, versioned data -----

    [Fact]
    public void The_trigger_and_its_lexicon_hold_no_AI_gateway()
    {
        foreach (var type in new[] { typeof(InvestigatorTrigger), typeof(InvestigatorTriggerLexicon), typeof(TriggerVerdict), typeof(ReplyOutcome) })
        {
            var members = type
                .GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(f => f.FieldType)
                .Concat(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SelectMany(c => c.GetParameters()).Select(p => p.ParameterType))
                .Concat(type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).SelectMany(m => m.GetParameters()).Select(p => p.ParameterType));

            Assert.DoesNotContain(members, t => t == typeof(IAiGateway) || t.Namespace?.StartsWith("Raffa.AiGateway", StringComparison.Ordinal) == true);
        }
    }

    [Fact]
    public void The_embedded_lexicon_is_versioned_and_covers_the_five_languages()
    {
        var lexicon = InvestigatorTriggerLexicon.Default;

        Assert.Matches(@"^\d+\.\d+\.\d+$", lexicon.Version);
        Assert.Equal(["it", "en", "fr", "es", "de"], lexicon.Languages.Select(l => l.Language).ToList());
        Assert.Equal("baseline", lexicon.Languages.Single(l => l.Language == "it").Status);
        Assert.Equal("baseline", lexicon.Languages.Single(l => l.Language == "en").Status);
        Assert.Equal(Trigger.LexiconVersion, lexicon.Version);
    }

    [Fact]
    public void A_candidate_lexicon_document_can_be_parsed_and_changes_the_verdict()
    {
        const string Json = """
            {
              "version": "9.9.9",
              "languages": {
                "xx": {
                  "politeness": [],
                  "operationalVerbs": ["zorb"],
                  "requestFormulas": ["canzorb"],
                  "deliverables": ["gizmo"]
                }
              }
            }
            """;

        var trigger = new InvestigatorTrigger(InvestigatorTriggerLexicon.Parse(Json));

        Assert.Equal("9.9.9", trigger.LexiconVersion);
        Assert.Equal("xx", trigger.Evaluate(null, null, "canzorb the gizmo please zorb it").T3Language);
        Assert.False(trigger.Evaluate(null, null, "Can you generate a report?").T3);
    }
}
