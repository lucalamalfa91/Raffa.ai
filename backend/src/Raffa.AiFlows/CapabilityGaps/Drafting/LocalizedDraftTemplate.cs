using System.Globalization;
using System.Text;
using Raffa.AiFlows.Shared.Guards;
using Raffa.AiFlows.Shared.Pack;

namespace Raffa.AiFlows.CapabilityGaps.Drafting;

/// <summary>
/// The deterministic fallback draft for the three communication types
/// (<see cref="DraftType"/>) in the five supported languages (plan LANG-07 / F4-D10), behind
/// <see cref="DraftingOptions.LocalizedTemplates"/> (off by default: <see cref="NegotiationEmailTemplate"/>
/// writes a renegotiation mail in Italian or English whatever the type). Same rules as that template: no
/// model call, every clause conditional on its fact being in the pack, every figure written the way
/// <see cref="NumericGuard"/> reads it back, digit-free wording otherwise, and a copyable draft with no
/// effect (decision D4: nothing is sent). The Italian and English renegotiation text is exactly
/// <see cref="NegotiationEmailTemplate"/>'s (a test holds it).
///
/// <para>
/// The French, Spanish and German wording, and the cancellation text of every language, is drafting
/// copy, not legal advice: the formal notice period and the form of a notice depend on the
/// jurisdiction and on the contract's own notices clause, so these texts need a native and a legal
/// review per jurisdiction (IT, FR, ES, DE) before they are relied on in production (LANG-07).
/// </para>
/// </summary>
public static class LocalizedDraftTemplate
{
    /// <summary>The draft for <paramref name="type"/> in <paramref name="language"/> (an unsupported or
    /// blank language is English, the language every deterministic reply already falls back to).</summary>
    public static DraftOutcome Build(
        string language, string supplierName, IReadOnlyList<PackItem> pack, DraftPlan? plan, DraftType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(supplierName);
        ArgumentNullException.ThrowIfNull(pack);

        plan ??= DraftPlan.FromPack(pack);
        var text = TextFor(NumericLocale.SupportedLanguage(language) ?? "en");
        var used = new List<string>();

        var (endDate, endDateKey) = NegotiationEmailTemplate.FindDate(pack, "endDate", "renewalDate");
        var (deadline, deadlineKey) = NegotiationEmailTemplate.FindDate(pack, "cancellationDeadline", "noticeDeadline");
        var (spend, spendKey) = NegotiationEmailTemplate.FindSpend(pack);

        NegotiationEmailTemplate.Track(used, endDateKey);
        NegotiationEmailTemplate.Track(used, deadlineKey);
        NegotiationEmailTemplate.Track(used, spendKey);

        var body = new StringBuilder();
        body.AppendLine(Fill(text.Greeting, supplierName));
        body.AppendLine();

        string subject;
        switch (type)
        {
            case DraftType.CancellationNonRenewal:
                subject = Fill(text.CancelSubject, supplierName);
                body.AppendLine(Opening(text.CancelOpening, text, endDate, deadline, spend: null));
                body.AppendLine();
                body.AppendLine(text.CancelRequest);
                body.AppendLine();
                body.AppendLine(text.CancelClosing);
                break;

            case DraftType.CounterProposal:
                subject = Fill(text.CounterSubject, supplierName);
                body.AppendLine(Opening(text.CounterOpening, text, endDate, deadline: null, spend));
                body.AppendLine();
                body.AppendLine(text.CounterIntro);
                AppendAsks(body, text, plan, pack, used);
                body.AppendLine();
                body.AppendLine(text.CounterClosing);
                break;

            default:
                subject = Fill(text.RenegotiationSubject, supplierName);
                body.AppendLine(Opening(text.RenegotiationOpening, text, endDate, deadline, spend));
                body.AppendLine();
                body.AppendLine(text.RenegotiationIntro);
                AppendAsks(body, text, plan, pack, used);
                body.AppendLine();
                body.AppendLine(text.RenegotiationClosing);
                break;
        }

        body.AppendLine();
        body.AppendLine(text.Regards);
        body.AppendLine(text.NamePlaceholder);
        body.Append(text.CompanyPlaceholder);

        if (used.Count == 0 && pack.Count > 0)
        {
            used.Add(pack[0].CitationKey);
        }

        return new DraftOutcome(subject, body.ToString(), used, DraftSource.Template, 0, null, [], type);
    }

    private static string Opening(string lead, DraftTemplateText text, string? endDate, string? deadline, string? spend)
    {
        var opening = new StringBuilder(lead);
        if (endDate is not null)
        {
            opening.Append(Fill(text.EndsOn, endDate));
        }

        if (deadline is not null)
        {
            opening.Append(Fill(text.NoticeDeadline, deadline));
        }

        opening.Append('.');
        if (spend is not null)
        {
            opening.Append(Fill(text.AnnualValue, spend));
        }

        return opening.ToString();
    }

    /// <summary>The plan's asks (at most three, and the trade as a last bullet), or the two generic ones
    /// when the plan has none.</summary>
    private static void AppendAsks(StringBuilder body, DraftTemplateText text, DraftPlan plan, IReadOnlyList<PackItem> pack, List<string> used)
    {
        var asks = plan.Asks.Where(a => !string.IsNullOrWhiteSpace(a.Sentence)).Take(3).ToList();
        if (!string.IsNullOrWhiteSpace(plan.Trade))
        {
            asks.Add(new DraftAsk("playbook", plan.Trade, NegotiationEmailTemplate.PlaybookKeys(pack)));
        }

        if (asks.Count == 0)
        {
            body.AppendLine("- " + text.DefaultAskPrice);
            body.AppendLine("- " + text.DefaultAskCap);
            return;
        }

        foreach (var ask in asks)
        {
            body.AppendLine("- " + ask.Sentence.Trim());
            foreach (var key in ask.CitationKeys)
            {
                NegotiationEmailTemplate.Track(used, key);
            }
        }
    }

    private static string Fill(string template, string value) => string.Format(CultureInfo.InvariantCulture, template, value);

    private static DraftTemplateText TextFor(string language) => language switch
    {
        "it" => DraftTemplateText.Italian,
        "fr" => DraftTemplateText.French,
        "es" => DraftTemplateText.Spanish,
        "de" => DraftTemplateText.German,
        _ => DraftTemplateText.English,
    };
}

/// <summary>The wording of one language. <c>{0}</c> is the supplier's name in a greeting or subject, a
/// date in <see cref="EndsOn"/> / <see cref="NoticeDeadline"/>, and an amount in <see cref="AnnualValue"/>.</summary>
internal sealed record DraftTemplateText
{
    public required string Greeting { get; init; }

    public required string EndsOn { get; init; }

    public required string NoticeDeadline { get; init; }

    public required string AnnualValue { get; init; }

    public required string DefaultAskPrice { get; init; }

    public required string DefaultAskCap { get; init; }

    public required string Regards { get; init; }

    public required string NamePlaceholder { get; init; }

    public required string CompanyPlaceholder { get; init; }

    public required string RenegotiationSubject { get; init; }

    public required string RenegotiationOpening { get; init; }

    public required string RenegotiationIntro { get; init; }

    public required string RenegotiationClosing { get; init; }

    public required string CancelSubject { get; init; }

    public required string CancelOpening { get; init; }

    public required string CancelRequest { get; init; }

    public required string CancelClosing { get; init; }

    public required string CounterSubject { get; init; }

    public required string CounterOpening { get; init; }

    public required string CounterIntro { get; init; }

    public required string CounterClosing { get; init; }

    public static DraftTemplateText Italian { get; } = new()
    {
        Greeting = "Gentile team {0},",
        EndsOn = ", in scadenza il {0}",
        NoticeDeadline = " (termine per la disdetta: {0})",
        AnnualValue = " Il valore annuo attuale è di {0}.",
        DefaultAskPrice = "una revisione del prezzo alla luce di quanto pagano oggi clienti comparabili;",
        DefaultAskCap = "un tetto agli aumenti annui e la flessibilità sui volumi per la durata del contratto.",
        Regards = "Cordiali saluti,",
        NamePlaceholder = "[Nome e cognome]",
        CompanyPlaceholder = "[Azienda]",
        RenegotiationSubject = "Rinnovo {0} – richiesta di revisione delle condizioni",
        RenegotiationOpening = "vi scrivo in merito al rinnovo del nostro contratto",
        RenegotiationIntro = "Prima di confermare il rinnovo vorremmo rivedere insieme le condizioni economiche:",
        RenegotiationClosing = "Restiamo disponibili a un confronto nei prossimi giorni e vi chiediamo di inviarci una proposta aggiornata prima della scadenza indicata, così da poter decidere il rinnovo con serenità.",
        CancelSubject = "Non rinnovo del contratto con {0}",
        CancelOpening = "con la presente vi comunichiamo che non intendiamo rinnovare il nostro contratto",
        CancelRequest = "Vi chiediamo di confermarci per iscritto la ricezione di questa comunicazione e la data di cessazione del servizio, e di indicarci le modalità per l'esportazione e la successiva cancellazione dei nostri dati.",
        CancelClosing = "Vi chiediamo inoltre di segnalarci se questa comunicazione deve essere inviata anche attraverso un canale diverso da questo, secondo quanto previsto dal contratto.",
        CounterSubject = "Riscontro alla vostra proposta – {0}",
        CounterOpening = "vi ringraziamo per la proposta ricevuta in merito al rinnovo del nostro contratto",
        CounterIntro = "Dopo le nostre valutazioni interne e il confronto con i valori di mercato, formuliamo la seguente controproposta:",
        CounterClosing = "Restiamo a disposizione per discuterne e ci auguriamo di poter raggiungere presto un accordo che soddisfi entrambe le parti.",
    };

    public static DraftTemplateText English { get; } = new()
    {
        Greeting = "Dear {0} team,",
        EndsOn = ", which ends on {0}",
        NoticeDeadline = " (notice deadline: {0})",
        AnnualValue = " The current annual value is {0}.",
        DefaultAskPrice = "a price revision in line with what comparable customers pay today;",
        DefaultAskCap = "a cap on annual increases and volume flexibility for the whole term.",
        Regards = "Kind regards,",
        NamePlaceholder = "[Name and surname]",
        CompanyPlaceholder = "[Company]",
        RenegotiationSubject = "{0} renewal – request to revise the commercial terms",
        RenegotiationOpening = "I am writing about the renewal of our contract",
        RenegotiationIntro = "Before we confirm the renewal we would like to review the commercial terms together:",
        RenegotiationClosing = "We remain available to discuss this in the coming days and ask you to send us a revised proposal before the deadline above, so that we can decide on the renewal in good time.",
        CancelSubject = "{0} – notice of non-renewal",
        CancelOpening = "we are writing to inform you that we do not intend to renew our contract",
        CancelRequest = "Please confirm in writing that you have received this notice and the date on which the service will end, and let us know how our data will be exported and then deleted.",
        CancelClosing = "Please also tell us whether this notice must be sent through a channel other than this one, as the contract provides.",
        CounterSubject = "Response to your proposal – {0}",
        CounterOpening = "thank you for your proposal on the renewal of our contract",
        CounterIntro = "Following our internal review and a comparison with market values, we put forward the following counter-proposal:",
        CounterClosing = "We remain available to discuss it and hope to reach an agreement that works for both sides soon.",
    };

    public static DraftTemplateText French { get; } = new()
    {
        Greeting = "À l'attention de l'équipe {0},",
        EndsOn = ", qui arrive à échéance le {0}",
        NoticeDeadline = " (date limite de préavis : {0})",
        AnnualValue = " La valeur annuelle actuelle est de {0}.",
        DefaultAskPrice = "une révision du prix au regard de ce que paient aujourd'hui des clients comparables ;",
        DefaultAskCap = "un plafonnement des hausses annuelles et de la souplesse sur les volumes pendant toute la durée du contrat.",
        Regards = "Cordialement,",
        NamePlaceholder = "[Prénom et nom]",
        CompanyPlaceholder = "[Société]",
        RenegotiationSubject = "Renouvellement {0} – demande de révision des conditions",
        RenegotiationOpening = "je vous écris au sujet du renouvellement de notre contrat",
        RenegotiationIntro = "Avant de confirmer le renouvellement, nous souhaiterions revoir ensemble les conditions commerciales :",
        RenegotiationClosing = "Nous restons disponibles pour en discuter dans les prochains jours et vous prions de nous adresser une proposition révisée avant l'échéance indiquée ci-dessus, afin que nous puissions décider du renouvellement dans de bonnes conditions.",
        CancelSubject = "Non-renouvellement du contrat avec {0}",
        CancelOpening = "par la présente, nous vous informons que nous ne souhaitons pas renouveler notre contrat",
        CancelRequest = "Nous vous remercions de bien vouloir nous confirmer par écrit la réception de ce message et la date de fin du service, ainsi que les modalités d'export puis de suppression de nos données.",
        CancelClosing = "Merci également de nous indiquer si cette communication doit être adressée par un autre canal que celui-ci, conformément aux dispositions du contrat.",
        CounterSubject = "Réponse à votre proposition – {0}",
        CounterOpening = "nous vous remercions de la proposition reçue concernant le renouvellement de notre contrat",
        CounterIntro = "Après analyse interne et comparaison avec les valeurs du marché, nous formulons la contre-proposition suivante :",
        CounterClosing = "Nous restons disponibles pour en discuter et espérons parvenir rapidement à un accord satisfaisant pour les deux parties.",
    };

    public static DraftTemplateText Spanish { get; } = new()
    {
        Greeting = "A la atención del equipo de {0},",
        EndsOn = ", que vence el {0}",
        NoticeDeadline = " (plazo de preaviso: {0})",
        AnnualValue = " El valor anual actual es de {0}.",
        DefaultAskPrice = "una revisión del precio a la luz de lo que pagan hoy clientes comparables;",
        DefaultAskCap = "un límite a los aumentos anuales y flexibilidad en los volúmenes durante toda la vigencia del contrato.",
        Regards = "Atentamente,",
        NamePlaceholder = "[Nombre y apellidos]",
        CompanyPlaceholder = "[Empresa]",
        RenegotiationSubject = "Renovación {0}: solicitud de revisión de las condiciones",
        RenegotiationOpening = "les escribo en relación con la renovación de nuestro contrato",
        RenegotiationIntro = "Antes de confirmar la renovación, nos gustaría revisar conjuntamente las condiciones económicas:",
        RenegotiationClosing = "Quedamos a su disposición para hablarlo en los próximos días y les rogamos que nos envíen una propuesta actualizada antes del plazo indicado, para poder decidir la renovación con tiempo.",
        CancelSubject = "No renovación del contrato con {0}",
        CancelOpening = "por la presente les comunicamos que no deseamos renovar nuestro contrato",
        CancelRequest = "Les rogamos que nos confirmen por escrito la recepción de esta comunicación y la fecha de finalización del servicio, así como el procedimiento para la exportación y posterior eliminación de nuestros datos.",
        CancelClosing = "Les agradeceríamos también que nos indiquen si esta comunicación debe enviarse por otro canal distinto de este, conforme a lo previsto en el contrato.",
        CounterSubject = "Respuesta a su propuesta – {0}",
        CounterOpening = "les agradecemos la propuesta recibida sobre la renovación de nuestro contrato",
        CounterIntro = "Tras nuestro análisis interno y la comparación con los valores de mercado, formulamos la siguiente contrapropuesta:",
        CounterClosing = "Quedamos a su disposición para comentarlo y confiamos en alcanzar pronto un acuerdo satisfactorio para ambas partes.",
    };

    public static DraftTemplateText German { get; } = new()
    {
        Greeting = "Sehr geehrtes {0}-Team,",
        EndsOn = " (Vertragsende: {0})",
        NoticeDeadline = " (Kündigungsfrist bis: {0})",
        AnnualValue = " Der aktuelle Jahreswert beträgt {0}.",
        DefaultAskPrice = "eine Preisüberprüfung im Lichte dessen, was vergleichbare Kunden heute zahlen;",
        DefaultAskCap = "eine Begrenzung der jährlichen Erhöhungen und Flexibilität bei den Mengen für die gesamte Vertragslaufzeit.",
        Regards = "Mit freundlichen Grüßen",
        NamePlaceholder = "[Vor- und Nachname]",
        CompanyPlaceholder = "[Unternehmen]",
        RenegotiationSubject = "Verlängerung {0} – Bitte um Überprüfung der Konditionen",
        RenegotiationOpening = "ich schreibe Ihnen zur Verlängerung unseres Vertrags",
        RenegotiationIntro = "Bevor wir die Verlängerung bestätigen, möchten wir die wirtschaftlichen Konditionen gemeinsam überprüfen:",
        RenegotiationClosing = "Wir stehen Ihnen in den kommenden Tagen gerne für ein Gespräch zur Verfügung und bitten Sie, uns vor der oben genannten Frist ein überarbeitetes Angebot zu senden, damit wir rechtzeitig über die Verlängerung entscheiden können.",
        CancelSubject = "Nichtverlängerung des Vertrags mit {0}",
        CancelOpening = "hiermit teilen wir Ihnen mit, dass wir unseren Vertrag nicht verlängern möchten",
        CancelRequest = "Bitte bestätigen Sie uns den Eingang dieser Mitteilung und das Datum der Beendigung des Dienstes schriftlich und teilen Sie uns mit, wie unsere Daten exportiert und anschließend gelöscht werden.",
        CancelClosing = "Bitte teilen Sie uns außerdem mit, ob diese Mitteilung gemäß Vertrag zusätzlich über einen anderen Kanal zu übermitteln ist.",
        CounterSubject = "Antwort auf Ihr Angebot – {0}",
        CounterOpening = "wir danken Ihnen für das Angebot zur Verlängerung unseres Vertrags",
        CounterIntro = "Nach interner Prüfung und einem Abgleich mit den Marktwerten unterbreiten wir Ihnen folgendes Gegenangebot:",
        CounterClosing = "Wir stehen Ihnen für ein Gespräch gerne zur Verfügung und hoffen auf eine baldige Einigung, die beiden Seiten gerecht wird.",
    };
}
