using System.Text.RegularExpressions;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Interview;

namespace Raffa.Chat.Application.Answering;

/// <summary>
/// The proposal Raffa writes itself when no grounded answer could be composed — the model declined
/// twice, both attempts failed the guards with nothing quotable in the pack, the pack was empty, or
/// the answer service did not respond. The product rule (persona v2.4) is that Ask never ends on
/// "I don't have data I trust enough": the user always gets a concrete way forward for the kind of
/// question they asked, in its language, with the screen that holds the answer as the reply's
/// action and next-step questions as its follow-ups (both chosen by the composition root).
///
/// <para>
/// Fixed copy only — no digit, amount, date or contract fact, so nothing in it can be wrong about
/// the user's data; the supplier name in the notice variants is the portfolio's own display name.
/// Pure and synchronous.
/// </para>
/// </summary>
public static class HelpfulFallbackAnswer
{
    private const RegexOptions CueOptions = RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant;

    // A request to write something for the supplier.
    private static readonly Regex DraftCue = new(@"\b(e-?mail|mail|pec|lettera|letter|bozza|draft)\b", CueOptions);

    private static readonly Regex RenewalCue = new(
        @"\b(rinnov\w*|renew\w*|scadenz\w*|scade\w*|disdett\w*|preavviso|notice|expir\w*)\b", CueOptions);

    private static readonly Regex SavingsCue = new(
        @"\b(risparm\w*|save|saves|saving|savings|scont\w*|discount\w*|ridurre|riduzione|reduce|cut)\b", CueOptions);

    private static readonly Regex MarketCue = new(
        @"\b(mercato|market|benchmark\w*|preventiv\w*|quotes?|prezz\w*|prices?|pricing)\b", CueOptions);

    private static readonly Regex ClauseCue = new(
        @"\b(clausol\w*|clauses?|responsabilit\w*|liabilit\w*|penal\w*|sla|indemnit\w*|recesso|termination)\b", CueOptions);

    private static readonly Regex DocumentsCue = new(@"\b(document\w*|caric\w*|upload\w*|pdf)\b", CueOptions);

    /// <summary>
    /// The kind of question <paramref name="question"/> reads as, from its own words — the area
    /// <see cref="Proposal"/> writes for when the planner's intent names none (a structured-fact or
    /// portfolio-wide turn): renewals, savings, market position, a clause, documents, or
    /// <see langword="null"/> for the general proposal. First match wins, in that order.
    /// </summary>
    public static string? AreaFor(string question)
    {
        ArgumentNullException.ThrowIfNull(question);

        return RenewalCue.IsMatch(question) ? CapabilityCatalog.RenewalsKey
            : SavingsCue.IsMatch(question) ? CapabilityCatalog.SavingsKey
            : MarketCue.IsMatch(question) ? CapabilityCatalog.QuoteCheckKey
            : ClauseCue.IsMatch(question) ? CapabilityCatalog.ContractDetailKey
            : DocumentsCue.IsMatch(question) ? CapabilityCatalog.DocumentsKey
            : null;
    }

    /// <summary>
    /// The way forward for <paramref name="capabilityKey"/>'s kind of question: savings, renewals,
    /// market position (Quote check), a clause (Contract 360), documents, or the general proposal
    /// for anything else. A request to write an email or a letter gets a ready-to-send draft to the
    /// supplier instead, every contract detail a bracketed placeholder. <paramref name="emptyWorkspace"/>
    /// wins over both: with no contract uploaded yet, uploading one is the way forward whatever was
    /// asked.
    /// </summary>
    public static string Proposal(string question, string? capabilityKey, bool emptyWorkspace = false)
    {
        ArgumentNullException.ThrowIfNull(question);

        var italian = IsItalian(question);
        if (!emptyWorkspace && DraftCue.IsMatch(question))
        {
            return SupplierEmailDraft(italian);
        }

        if (emptyWorkspace)
        {
            return italian
                ? "Partiamo dal primo contratto: caricalo in Documents e ti dico scadenze, spesa, clausole chiave e dove risparmiare.\n\n" +
                  "- **Carica un contratto, un ordine o un preventivo** in PDF.\n" +
                  "- **Conferma i campi estratti** quando te lo chiedo: da lì li uso in ogni risposta.\n" +
                  "- **Poi chiedimi** scadenze, rischi, leve di negoziazione o una bozza di email per il fornitore."
                : "Let's start from your first contract: upload it in Documents and I'll tell you its deadlines, spend, key clauses and where to save.\n\n" +
                  "- **Upload a contract, an order form or a quote** as a PDF.\n" +
                  "- **Confirm the extracted fields** when I ask: from then on I use them in every answer.\n" +
                  "- **Then ask me** about deadlines, risks, negotiation levers or a draft email to the supplier.";
        }

        return capabilityKey switch
        {
            CapabilityCatalog.SavingsKey => italian
                ? "Ecco da dove partire per risparmiare:\n\n" +
                  "- **Scegli il contratto giusto**: in Savings trovi le opportunità già calcolate, in Renewals le scadenze che ti lasciano tempo per negoziare.\n" +
                  "- **Prepara le leve**: dimmi il fornitore e ti preparo leve, richieste da fare e una bozza di email.\n" +
                  "- **Rafforza la tua posizione**: un preventivo concorrente o l'ultimo report d'uso in Quote check rendono ogni richiesta più solida.\n\n" +
                  "Dimmi su quale fornitore vuoi lavorare e ti preparo il piano completo."
                : "Here's where to start saving:\n\n" +
                  "- **Pick the right contract**: Savings lists the opportunities already calculated, Renewals the deadlines that leave you time to negotiate.\n" +
                  "- **Prepare the levers**: name the supplier and I'll prepare the levers, the asks and a draft email.\n" +
                  "- **Strengthen your position**: a competing quote or the latest usage report in Quote check makes every ask more solid.\n\n" +
                  "Tell me which supplier to work on and I'll build the full plan.",
            CapabilityCatalog.RenewalsKey => italian
                ? "Ecco come preparare il rinnovo:\n\n" +
                  "- **Fissa la scadenza**: in Renewals vedi entro quando muoverti su ogni contratto, disdetta compresa.\n" +
                  "- **Scrivi al fornitore**: dimmi il fornitore e ti preparo una bozza di email pronta da inviare, con le condizioni da chiedere.\n" +
                  "- **Arriva con un'alternativa**: un preventivo concorrente in Quote check rafforza la tua posizione in trattativa.\n\n" +
                  "Dimmi da quale contratto partire e ti preparo il piano."
                : "Here's how to prepare the renewal:\n\n" +
                  "- **Pin the deadline**: Renewals shows when to move on each contract, notice date included.\n" +
                  "- **Write to the supplier**: name the supplier and I'll draft a ready-to-send email with the terms to ask for.\n" +
                  "- **Bring an alternative**: a competing quote in Quote check strengthens your hand in the negotiation.\n\n" +
                  "Tell me which contract to start from and I'll build the plan.",
            CapabilityCatalog.QuoteCheckKey => italian
                ? "Ecco come verificare il prezzo rispetto al mercato:\n\n" +
                  "- **Carica il preventivo o l'ordine** in Quote check: lo confronto con i benchmark di mercato e con quanto paghi già.\n" +
                  "- **Scegli il fornitore**: chiedimi come si posiziona un contratto specifico e ti dico dove c'è margine.\n" +
                  "- **Usa il confronto in trattativa**: ti preparo le richieste da fare al fornitore partendo dai risultati."
                : "Here's how to check the price against the market:\n\n" +
                  "- **Upload the quote or the order form** in Quote check: I compare it with market benchmarks and with what you already pay.\n" +
                  "- **Pick the supplier**: ask me how one contract is positioned and I'll show you where the margin is.\n" +
                  "- **Use the comparison in the negotiation**: I'll prepare the asks for the supplier from the results.",
            CapabilityCatalog.ContractDetailKey => italian
                ? "Ecco come arrivare a questa clausola:\n\n" +
                  "- **Apri il contratto** da Portfolio: in Contract 360 hai il documento originale e le clausole principali.\n" +
                  "- **Completa i documenti**: se la clausola sta in un allegato o in un ordine, caricalo in Documents e la includo nelle risposte.\n" +
                  "- **Trasformala in una richiesta**: appena la clausola è a sistema, ti dico cosa significa commercialmente e cosa chiedere al fornitore."
                : "Here's how to get to this clause:\n\n" +
                  "- **Open the contract** from Portfolio: Contract 360 has the original document and its main clauses.\n" +
                  "- **Complete the documents**: if the clause sits in an annex or an order form, upload it in Documents and I'll include it in my answers.\n" +
                  "- **Turn it into an ask**: once the clause is on file, I'll tell you what it means commercially and what to ask the supplier.",
            CapabilityCatalog.DocumentsKey or CapabilityCatalog.DocumentsAttentionKey => italian
                ? "Ecco come sbloccare i tuoi documenti:\n\n" +
                  "- **Apri Documents** per vedere cosa è in revisione e quali campi mancano.\n" +
                  "- **Conferma i campi estratti**: appena un contratto è validato lo uso nelle risposte, con date e importi.\n" +
                  "- **Carica ciò che manca**: ordini, rinnovi o allegati completano il quadro."
                : "Here's how to unblock your documents:\n\n" +
                  "- **Open Documents** to see what is in review and which fields are missing.\n" +
                  "- **Confirm the extracted fields**: as soon as a contract is validated I use it in my answers, dates and amounts included.\n" +
                  "- **Upload what is missing**: order forms, renewals or annexes complete the picture.",
            _ => italian
                ? "Ti aiuto volentieri. Per arrivare a una risposta concreta:\n\n" +
                  "- **Nominami il fornitore o il contratto**: lavoro direttamente sui suoi dati.\n" +
                  "- **Dimmi l'obiettivo**: risparmio, rinnovo, confronto con il mercato o una clausola specifica.\n" +
                  "- **Chiedimi una bozza**: email al fornitore, piano di negoziazione o checklist per il rinnovo, pronta da usare."
                : "Happy to help. To get you a concrete answer:\n\n" +
                  "- **Name the supplier or the contract**: I work straight from its data.\n" +
                  "- **Tell me the goal**: savings, a renewal, a market comparison or a specific clause.\n" +
                  "- **Ask me for a draft**: an email to the supplier, a negotiation plan or a renewal checklist, ready to use.",
        };
    }

    /// <summary>The answer service did not respond this turn: say so plainly, as a delay rather than
    /// a dead end, and point at the screens (the reply's actions) that already hold the data.</summary>
    public static string ServiceBusy(string question)
    {
        ArgumentNullException.ThrowIfNull(question);

        return IsItalian(question)
            ? "Sto impiegando più del previsto a completare l'analisi: riprova tra qualche istante e la riprendo da qui. Nel frattempo trovi i dati aggiornati nelle schermate qui sotto."
            : "The analysis is taking longer than expected: try again in a moment and I'll pick it up from here. Meanwhile the screens below have the latest data.";
    }

    /// <summary>A notice question about <paramref name="supplierName"/>'s contract with neither a
    /// validated notice deadline nor a clause that names one: how to pin the date down now.</summary>
    public static string NoticeDateMissing(string question, string supplierName)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentException.ThrowIfNullOrWhiteSpace(supplierName);

        return IsItalian(question)
            ? $"Per {supplierName} la data di disdetta non è ancora tra i dati validati. Ecco come fissarla subito:\n\n" +
              "- **Apri Contract 360** e controlla la clausola di durata e rinnovo nel documento originale.\n" +
              "- **Chiedi conferma al fornitore**: posso prepararti una breve email per farti confermare scadenza e preavviso.\n" +
              "- **Conferma il campo** in Documents: da lì in poi Raffa ti avvisa in tempo."
            : $"The notice date for {supplierName} isn't among the validated data yet. Here's how to pin it down now:\n\n" +
              "- **Open Contract 360** and check the term and renewal clause in the original document.\n" +
              "- **Ask the supplier to confirm**: I can draft a short email asking them to confirm the end date and the notice period.\n" +
              "- **Confirm the field** in Documents: from then on Raffa reminds you in time.";
    }

    /// <summary>A notice question with no contract in scope: pick the contract, or name the
    /// supplier, and the exact date follows.</summary>
    public static string NoticeWithoutContract(string question)
    {
        ArgumentNullException.ThrowIfNull(question);

        return IsItalian(question)
            ? "Per darti la data di disdetta esatta partiamo dal contratto giusto: aprilo da Portfolio e chiedimelo da lì, oppure scrivimi il nome del fornitore (per esempio «quando va inviata la disdetta a [fornitore]?»)."
            : "To give you the exact notice date, let's start from the right contract: open it from Portfolio and ask me from there, or tell me the supplier (for example \"when is the notice due for [supplier]?\").";
    }

    /// <summary>A renewal and terms-review email to the supplier, ready to send once the bracketed
    /// placeholders are filled in — each part its own paragraph, which is how the web renderer
    /// separates lines.</summary>
    private static string SupplierEmailDraft(bool italian) => italian
        ? "Ecco una bozza pronta da inviare: completa i campi tra parentesi quadre.\n\n" +
          "**Oggetto:** Rinnovo e condizioni del contratto [nome del contratto]\n\n" +
          "Gentile [nome del referente],\n\n" +
          "in vista della scadenza del contratto [nome del contratto], prevista per il [data di scadenza], vorremmo avviare per tempo il confronto sulle condizioni di rinnovo.\n\n" +
          "Vi chiediamo di inviarci entro il [data di risposta] una proposta che indichi:\n\n" +
          "- prezzi e sconti per il nuovo periodo, con un tetto agli aumenti annuali;\n" +
          "- durata, preavviso e condizioni di recesso;\n" +
          "- livelli di servizio e relative penali.\n\n" +
          "Stiamo valutando anche alternative sul mercato: una proposta competitiva ci aiuterà a decidere rapidamente.\n\n" +
          "Cordiali saluti,\n\n" +
          "[nome e ruolo]\n\n" +
          "Dimmi il fornitore e completo la bozza con le date e le condizioni del tuo contratto."
        : "Here's a draft ready to send: fill in the fields in square brackets.\n\n" +
          "**Subject:** Renewal and terms of the [contract name] agreement\n\n" +
          "Dear [contact name],\n\n" +
          "ahead of the [contract name] agreement's end on [end date], we would like to start the renewal discussion in good time.\n\n" +
          "Please send us by [reply date] a proposal covering:\n\n" +
          "- prices and discounts for the new term, with a cap on annual increases;\n" +
          "- term, notice period and termination rights;\n" +
          "- service levels and the related credits.\n\n" +
          "We are also reviewing alternatives on the market, so a competitive proposal will help us decide quickly.\n\n" +
          "Kind regards,\n\n" +
          "[name and role]\n\n" +
          "Tell me the supplier and I'll complete the draft with your contract's dates and terms.";

    // Either detector: the fallback answer's own cue list, or the planner's (which also knows the
    // notice vocabulary — "quando", "scade", "disdetta", "preavviso").
    private static bool IsItalian(string question) =>
        GroundedFallbackAnswer.IsItalian(question) || LanguageHint.IsItalian(question);
}
