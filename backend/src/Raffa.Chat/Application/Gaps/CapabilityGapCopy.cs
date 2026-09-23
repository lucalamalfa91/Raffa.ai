using Raffa.Chat.Application.Language;
using Raffa.Chat.Application.Reply;

namespace Raffa.Chat.Application.Gaps;

/// <summary>
/// Every deterministic sentence a capability-gap turn shows, in Italian and English (ADR-030
/// D1/D4/D5) — written here, never in the client, so the preface, the feedback card and the
/// confirmation follow the question's language the way the persona's own answers do (law 5).
/// Pure string composition; no number, no date, no guid ever enters these sentences, so nothing
/// here can trip the numeric guard or R-ASK-08.
/// </summary>
public static class CapabilityGapCopy
{
    /// <summary>"Al momento non posso {operazione} da Raffa.ai, però {alternativa}." — the
    /// honest opening every gap reply starts with.</summary>
    public static string Preface(CapabilityGap gap, string language)
    {
        ArgumentNullException.ThrowIfNull(gap);
        return QuestionLanguage.IsItalian(language)
            ? $"Al momento non posso {gap.OperationIt} da Raffa.ai, però {gap.AlternativeIt}."
            : $"I can't {gap.OperationEn} from Raffa.ai yet, but {gap.AlternativeEn}.";
    }

    /// <summary>
    /// The clause after "but" for a gap the investigator discovered (ADR-031): server-authored
    /// from the nearest existing screen it named, never model text — a
    /// <see cref="Capabilities.CapabilityCatalog"/> key, or <see langword="null"/>/<c>ask</c> when
    /// the nearest thing Raffa offers is an answer in the chat itself.
    /// </summary>
    public static string NearestAlternative(string? capabilityKey, string language)
    {
        var italian = QuestionLanguage.IsItalian(language);
        return capabilityKey switch
        {
            Capabilities.CapabilityCatalog.PortfolioKey => italian
                ? "in Portfolio trovi già ogni contratto validato con spesa, scadenze e rischio"
                : "Portfolio already shows every validated contract with its spend, dates and risk",
            Capabilities.CapabilityCatalog.RenewalsKey => italian
                ? "in Renewals trovi già ogni scadenza di preavviso e l'azione da fare"
                : "Renewals already lists every notice deadline and the action to take",
            Capabilities.CapabilityCatalog.SavingsKey => italian
                ? "in Savings trovi già i risparmi individuati e le opportunità da cui nascono"
                : "Savings already shows the savings identified and the opportunities behind them",
            Capabilities.CapabilityCatalog.ContractDetailKey => italian
                ? "in Contract 360 trovi già tutti i dati di un contratto"
                : "Contract 360 already holds every fact of a contract",
            Capabilities.CapabilityCatalog.QuoteCheckKey => italian
                ? "con Quote check puoi già confrontare un nuovo preventivo con il mercato"
                : "Quote check already benchmarks a new quote against the market",
            Capabilities.CapabilityCatalog.DocumentsKey
                or Capabilities.CapabilityCatalog.DocumentsAttentionKey
                or Capabilities.CapabilityCatalog.DocumentsReviewKey => italian
                ? "in Documents trovi già ogni file caricato e la sua revisione"
                : "Documents already holds every file you uploaded and its review",
            Capabilities.CapabilityCatalog.WorkspaceMembersKey => italian
                ? "in Workspace & members gestisci già chi lavora in Raffa"
                : "Workspace & members already manages who works in Raffa",
            _ => italian
                ? "posso rispondere qui alle tue domande sui contratti validati"
                : "I can answer your questions on the validated contracts right here",
        };
    }

    /// <summary>The first sentence of a capability follow-up (ADR-031): the answer came first,
    /// this message comes after it, so it says what it is — the result of checking what Raffa can
    /// do for the request.</summary>
    public static string CheckedOpening(string language) =>
        QuestionLanguage.IsItalian(language)
            ? "Ho verificato cosa sa fare Raffa.ai per la tua richiesta."
            : "I checked what Raffa.ai can do for your request.";

    /// <summary>The second sentence of a discovered-gap turn (ADR-031): the feature does not
    /// exist yet, the user can propose it, and a person approves it before anything is built.</summary>
    public static string DiscoveredLeadIn(string language) =>
        QuestionLanguage.IsItalian(language)
            ? "Non esiste ancora una funzionalità per farlo: se vuoi, puoi proporla al team Raffa.ai " +
              "rispondendo a tre domande. La proposta diventa una segnalazione su GitHub e verrà sviluppata " +
              "solo dopo l'approvazione di una persona del team."
            : "There is no feature for this yet: if you like, you can propose it to the Raffa.ai team by " +
              "answering three questions. The proposal becomes a GitHub issue and is built only once a " +
              "person on the team approves it.";

    /// <summary>The second sentence of a draft turn, before the email card.</summary>
    public static string DraftLeadIn(string language, string supplierName) =>
        QuestionLanguage.IsItalian(language)
            ? $"Ecco una bozza scritta dai dati del contratto {supplierName}: controllala, adattala e copiala nella tua casella di posta."
            : $"Here is a draft written from the {supplierName} contract's own facts: check it, adapt it and copy it into your mailbox.";

    /// <summary>The draft-alternative reply when no contract could be resolved: the preface plus
    /// "which contract?", naming the unknown supplier the question typed when there was one, or
    /// inviting an upload when the workspace has nothing validated at all.</summary>
    public static string AskWhichContract(CapabilityGap gap, string language, string? unknownSupplier, bool portfolioIsEmpty)
    {
        var preface = Preface(gap, language);

        if (portfolioIsEmpty)
        {
            return QuestionLanguage.IsItalian(language)
                ? preface + " Non c'è ancora nessun contratto validato: caricane uno in Documents e scriverò la mail dai suoi dati."
                : preface + " Nothing is validated yet: upload a contract in Documents and I'll write the email from its own facts.";
        }

        if (unknownSupplier is not null)
        {
            return QuestionLanguage.IsItalian(language)
                ? preface + $" Non ho un contratto validato di {unknownSupplier}: caricalo in Documents, oppure scegli uno dei fornitori qui sotto."
                : preface + $" I have no validated {unknownSupplier} contract: upload it in Documents, or pick one of the suppliers below.";
        }

        return QuestionLanguage.IsItalian(language)
            ? preface + " Dimmi per quale contratto la vuoi: scegli uno dei fornitori qui sotto, oppure apri Portfolio."
            : preface + " Tell me which contract it is for: pick one of the suppliers below, or open Portfolio.";
    }

    /// <summary>The follow-up chip that re-enters the email gap for one validated supplier —
    /// phrased so <see cref="CapabilityGapCatalog"/> matches it again and the gate resolves the
    /// name (proved by <c>DomainGateTests</c>).</summary>
    public static string DraftFollowUp(string language, string supplierName) =>
        QuestionLanguage.IsItalian(language)
            ? $"Scrivi la mail per il rinnovo {supplierName}"
            : $"Write the renewal email for {supplierName}";

    /// <summary>The two follow-ups after a draft, both landing on intents that already work.</summary>
    public static IReadOnlyList<string> AfterDraftFollowUps(string language, string supplierName) =>
        QuestionLanguage.IsItalian(language)
            ? [$"Quali leve ho sul rinnovo {supplierName}?", $"Quando devo dare disdetta a {supplierName}?"]
            : [$"What levers do I have on the {supplierName} renewal?", $"When must we give notice to {supplierName}?"];

    /// <summary>The in-chat feedback card, fully localised (ADR-030 D5). The first question is
    /// prefilled with the gap's own one-line description so "yes" then "send" is a complete,
    /// meaningful report even when nothing is typed. A discovered gap (ADR-031) asks to
    /// <em>propose</em> the feature by name; every card says, before the free text, that the
    /// answers are public and that a person approves the request before it is built.</summary>
    public static FeedbackOffer FeedbackOfferFor(CapabilityGap gap, string language)
    {
        ArgumentNullException.ThrowIfNull(gap);
        var discovered = gap.Origin == GapOrigin.Investigator;

        if (QuestionLanguage.IsItalian(language))
        {
            return new FeedbackOffer(
                discovered
                    ? $"Vuoi proporre «{gap.TitleIt}» come nuova funzionalità di Raffa.ai?"
                    : "Vuoi segnalarlo al team Raffa.ai perché lo implementi?",
                "Sì", "No", "Avanti", "Indietro", "Invia", "Invio in corso…", "Grazie!",
                "Non sono riuscito a inviare la segnalazione. Riprova.",
                "Le risposte saranno pubbliche su GitHub: non inserire nomi di fornitori, importi o dati dei contratti. " +
                "Una persona del team Raffa.ai valuterà la richiesta e dovrà approvarla prima che venga sviluppata.",
                [
                    new FeedbackQuestion(FeedbackQuestions.WhatKey, FeedbackQuestions.TextKind,
                        "Cosa dovrebbe fare Raffa esattamente?", gap.DescriptionIt, null),
                    new FeedbackQuestion(FeedbackQuestions.FrequencyKey, FeedbackQuestions.ChoiceKind,
                        "Quanto spesso ti servirebbe?", null,
                        [
                            new FeedbackChoice(FeedbackQuestions.FrequencyEveryRenewal, "ad ogni rinnovo"),
                            new FeedbackChoice(FeedbackQuestions.FrequencyWeekly, "ogni settimana"),
                            new FeedbackChoice(FeedbackQuestions.FrequencySometimes, "ogni tanto"),
                        ]),
                    new FeedbackQuestion(FeedbackQuestions.ImportanceKey, FeedbackQuestions.ChoiceKind,
                        "Quanto è importante per il tuo lavoro?", null,
                        [
                            new FeedbackChoice(FeedbackQuestions.ImportanceBlocking, "bloccante"),
                            new FeedbackChoice(FeedbackQuestions.ImportanceVeryUseful, "molto utile"),
                            new FeedbackChoice(FeedbackQuestions.ImportanceNiceToHave, "comodo"),
                        ]),
                ]);
        }

        return new FeedbackOffer(
            discovered
                ? $"Want to propose “{gap.TitleEn}” as a new Raffa.ai feature?"
                : "Want to report this to the Raffa.ai team so they can build it?",
            "Yes", "No", "Next", "Back", "Send", "Sending…", "Thanks!",
            "I couldn't send the report. Please try again.",
            "Your answers will be public on GitHub: do not include supplier names, amounts or contract data. " +
            "A person on the Raffa.ai team reviews the request and must approve it before it is built.",
            [
                new FeedbackQuestion(FeedbackQuestions.WhatKey, FeedbackQuestions.TextKind,
                    "What exactly should Raffa do?", gap.DescriptionEn, null),
                new FeedbackQuestion(FeedbackQuestions.FrequencyKey, FeedbackQuestions.ChoiceKind,
                    "How often would you need it?", null,
                    [
                        new FeedbackChoice(FeedbackQuestions.FrequencyEveryRenewal, "at every renewal"),
                        new FeedbackChoice(FeedbackQuestions.FrequencyWeekly, "every week"),
                        new FeedbackChoice(FeedbackQuestions.FrequencySometimes, "now and then"),
                    ]),
                new FeedbackQuestion(FeedbackQuestions.ImportanceKey, FeedbackQuestions.ChoiceKind,
                    "How important is it for your work?", null,
                    [
                        new FeedbackChoice(FeedbackQuestions.ImportanceBlocking, "blocking"),
                        new FeedbackChoice(FeedbackQuestions.ImportanceVeryUseful, "very useful"),
                        new FeedbackChoice(FeedbackQuestions.ImportanceNiceToHave, "nice to have"),
                    ]),
            ]);
    }
}

/// <summary>The fixed vocabulary of the three interview questions — the keys the card submits and
/// the server validates (<c>Feedback.FeedbackService</c>), so a client cannot invent a fourth
/// question or a fifth choice.</summary>
public static class FeedbackQuestions
{
    public const string TextKind = "text";
    public const string ChoiceKind = "choice";

    public const string WhatKey = "what";
    public const string FrequencyKey = "frequency";
    public const string ImportanceKey = "importance";

    public const string FrequencyEveryRenewal = "every-renewal";
    public const string FrequencyWeekly = "weekly";
    public const string FrequencySometimes = "sometimes";

    public const string ImportanceBlocking = "blocking";
    public const string ImportanceVeryUseful = "very-useful";
    public const string ImportanceNiceToHave = "nice-to-have";

    /// <summary>The free-text answer is capped so a public issue body stays short and a pasted
    /// contract cannot travel through it by accident.</summary>
    public const int WhatMaxLength = 500;

    public static IReadOnlySet<string> FrequencyKeys { get; } =
        new HashSet<string>(StringComparer.Ordinal) { FrequencyEveryRenewal, FrequencyWeekly, FrequencySometimes };

    public static IReadOnlySet<string> ImportanceKeys { get; } =
        new HashSet<string>(StringComparer.Ordinal) { ImportanceBlocking, ImportanceVeryUseful, ImportanceNiceToHave };

    /// <summary>English labels for the issue body (the devs' language), keyed by choice key.</summary>
    public static string LabelFor(string choiceKey) => choiceKey switch
    {
        FrequencyEveryRenewal => "at every renewal",
        FrequencyWeekly => "every week",
        FrequencySometimes => "now and then",
        ImportanceBlocking => "blocking",
        ImportanceVeryUseful => "very useful",
        ImportanceNiceToHave => "nice to have",
        _ => choiceKey,
    };
}
