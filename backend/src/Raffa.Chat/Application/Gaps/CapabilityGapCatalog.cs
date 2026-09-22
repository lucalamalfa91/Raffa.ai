using System.Text.RegularExpressions;

namespace Raffa.Chat.Application.Gaps;

/// <summary>
/// The fixed catalog of operations Ask Raffa cannot perform yet (ADR-030 D1) — five entries,
/// matched deterministically (regex only, it/en) by <c>Gate.DomainGate.Classify</c> right after the
/// legal lexicon and before the capability/how-to lexicon, so "can you help me send an email to
/// the supplier?" is recognised as a request for an operation, never as the feature tour
/// ("help") and never as a savings turn ("leverage"). First match wins, in catalog order:
/// <see cref="SendSupplierKey"/> is checked before <see cref="EmailDraftKey"/> so a question that
/// both writes and sends is recorded as the <em>send</em> gap (what is actually missing), while
/// both resolve to the same drafted-email alternative.
///
/// <para>Deliberately conservative: every pattern needs an operation verb and its object (or an
/// unmistakable noun such as "promemoria"), a supplier name alone is never enough, and the
/// send gap is vetoed by a timing question ("when must we send the notice?" is a notice-deadline
/// fact, R-ASK-03/NW-91). A missed gap costs the user one honest abstain; a false positive would
/// hijack a real question, so the lexicon errs on the side of missing.</para>
/// </summary>
public static class CapabilityGapCatalog
{
    public const string SendSupplierKey = "send-supplier";
    public const string EmailDraftKey = "email-draft";
    public const string ReminderKey = "reminder";
    public const string ExportFileKey = "export-file";
    public const string PurchaseOrderKey = "purchase-order";

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    // An operation verb in either language, conjugated the way a request is phrased ("send",
    // "invia", "mandami", "spediscila", "contattalo", "email the supplier").
    private const string SendVerbs =
        @"\b(send|sends|sending|notify|notifies|contact|contacts|forward|forwards|" +
        @"invi(a|are|o|amo|ala|alo|agli|ale|ami|amela|amelo)|" +
        @"mand(a|are|i|iamo|ala|alo|agli|ale|ami|amela|amelo)|" +
        @"spedi(sci|re|sco|amo|scila|scilo|scigli)|" +
        @"notific(a|are|hi|hiamo|alo|ala|agli)|" +
        @"contatt(a|are|i|iamo|alo|ala|ali|ami)|" +
        @"inoltr(a|are|i|iamo|ala|alo|agli)|" +
        @"e-?mail\s+(the|our|my|them|him|her|it\s+to|to))\b";

    private const string SendObjects =
        @"\b(e-?mails?|mail|letter|lettera|messag(e|es|gio|gi)|comunicazion[ei]|pec|" +
        @"supplier|suppliers|fornitor[ei]|vendor|vendors|to\s+them|a\s+loro|al\s+fornitore|ai\s+fornitori)\b";

    // A timing question about the notice is a structured fact, never a send request.
    private static readonly Regex TimingQuestion = new(
        @"\b(when|by\s+when|quando|entro\s+quando|entro\s+il|entro\s+che|deadline|scadenz\w*|termine\s+per)\b",
        Options);

    private const string WriteVerbs =
        @"\b(write|writes|writing|draft|drafts|drafting|create|creates|creating|prepare|prepares|preparing|" +
        @"compose|composes|put\s+together|" +
        @"scriv\w*|prepar(a|are|ami|iamo|ala|alo|amela|amelo|ami)|" +
        @"cre(a|are|ami|iamo|ala|alo|amela|amelo)|redig\w*|compon(i|ere|iamo|imi)|" +
        @"formul(a|are|ami|iamo|ala|alo)|bozza|stesura|testo)\b";

    private const string WriteObjects =
        @"\b(e-?mails?|mail|letter|lettera|messag(e|es|gio|gi)|comunicazion[ei]|pec|" +
        @"proposal\s+letter|offerta\s+scritta|counter-?proposal|controproposta|" +
        @"reply\s+to\s+the\s+supplier|risposta\s+(al|per\s+il)\s+fornitore)\b";

    /// <summary>Two lookaheads over the whole question: an operation verb anywhere and its object
    /// anywhere, so word order and intervening words ("help me create an email based on...") do
    /// not matter.</summary>
    private static Regex VerbAndObject(string verbs, string objects) =>
        new($"(?s)^(?=.*{verbs})(?=.*{objects})", Options);

    public static IReadOnlyList<CapabilityGap> All { get; } =
    [
        new(
            SendSupplierKey,
            "Send a message to the supplier from Raffa.ai",
            "Inviare un messaggio al fornitore da Raffa.ai",
            "send an email to the supplier",
            "inviare un'email al fornitore",
            "I can help you write the renewal email",
            "posso aiutarti a scrivere la mail per il rinnovo",
            "Send the negotiation email to the supplier directly from Raffa.ai",
            "Inviare l'email di rinegoziazione al fornitore direttamente da Raffa.ai",
            GapAlternative.DraftEmail,
            VerbAndObject(SendVerbs, SendObjects),
            TimingQuestion),
        new(
            EmailDraftKey,
            "Draft and send negotiation emails",
            "Scrivere e inviare email di negoziazione",
            "create or send emails",
            "creare o inviare email",
            "I can help you write the renewal email",
            "posso aiutarti a scrivere la mail per il rinnovo",
            "Create the negotiation email as a ready-to-send message in my mailbox",
            "Creare l'email di negoziazione come messaggio pronto da inviare dalla mia casella",
            GapAlternative.DraftEmail,
            VerbAndObject(WriteVerbs, WriteObjects)),
        new(
            ReminderKey,
            "Reminders and calendar entries",
            "Promemoria e calendario",
            "set reminders or calendar entries",
            "impostare promemoria o eventi in calendario",
            "Renewals already tracks every notice deadline for you",
            "in Renewals trovi già ogni scadenza di preavviso",
            "Remind me before a notice deadline by email or calendar",
            "Ricordarmi una scadenza di preavviso via email o calendario",
            GapAlternative.Renewals,
            new Regex(
                @"\b(reminders?|remind\s+(me|us)|alert\s+(me|us)|set\s+an?\s+alert|" +
                @"add\s+[^.?!]{0,40}?\bto\s+(my\s+|the\s+|our\s+)?calendar|calendar\s+(invite|entry|event|reminder)|" +
                @"promemoria|ricordamel[oa]|ricordami|ricordaci|avvisami|avvisaci|avvertimi|" +
                @"mettimi\s+un|segna(mi|lo|la)?\s+in\s+agenda|in\s+agenda|(sul|nel|in)\s+calendario|invito\s+(in\s+)?calendario)\b",
                Options)),
        new(
            ExportFileKey,
            "Export to Excel or Word",
            "Esportare in Excel o Word",
            "export files",
            "esportare file",
            "Portfolio shows the same data in a table you can filter",
            "in Portfolio trovi gli stessi dati in una tabella filtrabile",
            "Export the portfolio or an answer to Excel/Word",
            "Esportare il portafoglio o una risposta in Excel/Word",
            GapAlternative.Portfolio,
            new Regex(
                @"\b(export\w*|esport\w*|excel|xlsx|csv|spreadsheet|foglio\s+(di\s+calcolo|excel)|" +
                @"(to|into|as|in)\s+(a\s+)?(word|docx)\b|word\s+document|documento\s+word|" +
                @"(genera|generate|create|crea|produce|produci)\w*\s+(a\s+|un\s+|il\s+|the\s+)?(report|pdf)\b)",
                Options)),
        new(
            PurchaseOrderKey,
            "Purchase orders and ERP",
            "Ordini d'acquisto ed ERP",
            "raise purchase orders or talk to your ERP",
            "emettere ordini d'acquisto o parlare con il vostro ERP",
            "Contract 360 has the facts you need to raise it yourself",
            "in Contract 360 trovi i dati che ti servono per emetterlo",
            "Raise a purchase order from a contract or sync with our ERP",
            "Emettere un ordine d'acquisto da un contratto o sincronizzare con il nostro ERP",
            GapAlternative.ContractDetail,
            // "PO"/"ODA"/"RDA" are matched case-sensitively: Italian "un po'" must never fire this.
            new Regex(
                @"\b(purchase\s+orders?|requisitions?|\berp\b|ordin[ei]\s+d[’']acquisto|richiest[ae]\s+d[’']acquisto|" +
                @"raise\s+an?\s+(?-i:PO)\b|(?-i:\bPO\b)|(?-i:\bODA\b)|(?-i:\bRDA\b)|" +
                @"create\s+an?\s+order|crea\w*\s+(un\s+)?ordine|emett\w*\s+(un\s+)?ordine)",
                Options)),
    ];

    /// <summary>The first catalog entry whose lexicon matches <paramref name="question"/>, or
    /// <see langword="null"/> when the question asks for nothing Raffa cannot do. Pure.</summary>
    public static CapabilityGap? Match(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        var trimmed = question.Trim();
        return All.FirstOrDefault(gap => gap.Matches(trimmed));
    }

    public static CapabilityGap? Find(string key) =>
        All.FirstOrDefault(gap => string.Equals(gap.Key, key, StringComparison.Ordinal));
}
