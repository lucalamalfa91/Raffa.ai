using System.Text.RegularExpressions;
using Raffa.SharedKernel.Text;
using static Raffa.Renewals.Application.Cases.Tx;

namespace Raffa.Renewals.Application.Cases;

public static partial class RenewalCaseEngine
{
    private const string Urgent = "urgent";
    private const string Alert = "alert";
    private const string Muted = "muted";
    private const string Strong = "strong";

    private static readonly CaseText[] Stg =
    [
        B("Start", "Avvio"), B("Plan", "Piano"), B("Negotiation", "Negoziazione"), B("Agreement", "Accordo"),
        B("Contract & signature", "Contratto e firma"), B("Acquisition", "Acquisizione"),
    ];

    private static readonly CaseText[] AStg =
    [
        B("Features", "Funzioni"), B("Candidates", "Candidati"), B("Request offers", "Richiesta offerte"), B("Collect offers", "Raccolta offerte"),
        B("Compare & choose", "Confronto e scelta"), B("Exit the old contract", "Uscita dal vecchio contratto"), B("New contract", "Nuovo contratto"), B("Done", "Fatto"),
    ];

    private static readonly CaseText[] AEx =
    [
        B("Untick what you do not need, add what is missing, then confirm.", "Togli ciò che non serve, aggiungi ciò che manca, poi conferma."),
        B("Name the vendors you want to ask — at least one.", "Indica i fornitori da interpellare — almeno uno."),
        B("Approve the request emails, copy, send from your mailbox, confirm.", "Approva le email di richiesta, copia, invia dalla tua casella, conferma."),
        B("Paste or attach each vendor’s offer when it arrives.", "Incolla o allega l’offerta di ciascun fornitore quando arriva."),
        B("Read the comparison with your current contract, then choose.", "Leggi il confronto con il tuo contratto attuale, poi scegli."),
        B("Send the non-renewal notice to the old supplier and record the outcome.", "Invia la disdetta al vecchio fornitore e registra l’esito."),
        B("Add the signed contract of the new vendor.", "Aggiungi il contratto firmato del nuovo fornitore."),
        B("Replacement complete.", "Sostituzione completata."),
    ];

    private static readonly CaseText[] Ex =
    [
        B("Example: “Keep everything, but reduce CPQ to 40 seats.”", "Esempio: «Mantieni tutto, ma riduci CPQ a 40 licenze.»"),
        B("Read the plan: confirm it, or press Edit to change an answer.", "Leggi il piano: confermalo, oppure premi Modifica per cambiare una risposta."),
        B("Paste the supplier email as it is — the whole thread is fine. E.g. “Hi Luca, for a 24-month renewal we can offer…”", "Incolla l’email del fornitore così com’è — va bene anche tutta la catena. Es. «Hi Luca, for a 24-month renewal we can offer…»"),
        B("Confirm only what the supplier wrote. Silence is not acceptance.", "Conferma solo ciò che il fornitore ha scritto. Il silenzio non è accettazione."),
        B("Add the draft the supplier sent, then the signed PDF (a scan is fine). The file name alone is not proof.", "Aggiungi la bozza ricevuta dal fornitore, poi il PDF firmato (va bene una scansione). Il nome del file da solo non è una prova."),
        B("Decide the fields below 80% — the same review as in Documents.", "Decidi i campi sotto l’80% — la stessa revisione di Documenti."),
    ];

    /// <summary>Email kinds in running text (<c>KX</c>).</summary>
    private static CaseText Kx(string kind) => kind switch
    {
        "first" => B("first email", "prima email"),
        "followup" => B("follow-up", "sollecito"),
        "counter" => B("counter-proposal", "controproposta"),
        "docreq" => B("contract request", "richiesta contratto"),
        "fix" => B("correction email", "email correttiva"),
        "notice" => B("non-renewal notice", "disdetta"),
        _ => B("request for proposal", "richiesta di offerta"),
    };

    /// <summary>Email kinds as a kicker (<c>KL</c>).</summary>
    private static CaseText Kl(string kind) => kind switch
    {
        "first" => B("First email", "Prima email"),
        "followup" => B("Follow-up", "Sollecito"),
        "counter" => B("Counter-proposal", "Controproposta"),
        "docreq" => B("Contract request", "Richiesta contratto"),
        "fix" => B("Correction email", "Email correttiva"),
        "notice" => B("Non-renewal notice", "Disdetta"),
        _ => B("Request for proposal", "Richiesta di offerta"),
    };

    private static CaseText TermStateLabel(string state) => state switch
    {
        "proposed" => B("Proposed", "Proposto"),
        "agreed" => B("Agreed", "Concordato"),
        "rejected" => B("Rejected", "Rifiutato"),
        "unclear" => B("Unclear", "Poco chiaro"),
        _ => B("Open", "Aperto"),
    };

    private static CaseText EmailStateShort(string state) => state switch
    {
        EmailStates.Draft => B("Draft", "Bozza"),
        EmailStates.ToApprove => B("To approve", "Da approvare"),
        EmailStates.Approved => B("Approved", "Approvata"),
        EmailStates.Copied => B("Copied", "Copiata"),
        EmailStates.Sent => B("Send confirmed", "Invio confermato"),
        _ => B("Superseded", "Superata"),
    };

    private static CaseText EmailStateLower(string state) => state switch
    {
        EmailStates.Draft => B("draft", "bozza"),
        EmailStates.ToApprove => B("to approve", "da approvare"),
        EmailStates.Approved => B("approved", "approvata"),
        EmailStates.Copied => B("copied", "copiata"),
        EmailStates.Sent => B("send confirmed", "invio confermato"),
        _ => B("superseded", "superata"),
    };

    private static CaseText? Gloss(string key) => key switch
    {
        "price" => B("What you pay per unit or per year — the base of every other figure.", "Quanto paghi per unità o all’anno — la base di ogni altra cifra."),
        "term" => B("How long you are committed. Longer usually buys a lower price but less flexibility at the next renewal.", "Per quanto tempo sei vincolato. Più lungo di solito vale un prezzo più basso ma meno flessibilità al prossimo rinnovo."),
        "uplift" => B("The automatic price increase applied at each renewal if nothing is negotiated.", "L’aumento di prezzo automatico a ogni rinnovo se non si negozia nulla."),
        "payment" => B("When you pay. Annual in advance ties up cash; quarterly or Net 60 is easier on cash flow.", "Quando paghi. Annuale anticipato immobilizza cassa; trimestrale o Net 60 pesa meno."),
        "liab" => B("Limitation of liability: the most the supplier has to pay you if something goes wrong, in months of fees. The lower it is, the less protected you are — especially for data breaches.", "Limite di responsabilità: il massimo che il fornitore deve pagarti se qualcosa va storto, in mesi di canone. Più è basso, meno sei protetto — soprattutto per le violazioni dei dati."),
        "renew" => B("Auto-renewal: the contract renews by itself unless you give notice before the deadline.", "Rinnovo automatico: il contratto si rinnova da solo se non dai disdetta entro la scadenza."),
        _ => null,
    };

    /// <summary>Phase → (short status, status, next action, who acts) — the prototype's <c>PH</c>.</summary>
    private static (string Short, CaseText Status, CaseText Next, string Who) Ph(string phase) => phase switch
    {
        CasePhases.Interview => ("Interview", B("Interview", "Intervista"), B("Answer the questions", "Rispondi alle domande"), "you"),
        CasePhases.Plan => ("Plan ready", B("Plan to confirm", "Piano da confermare"), B("Confirm or edit the plan", "Conferma o modifica il piano"), "owner"),
        CasePhases.WaitingRem => ("Email to approve", B("Sent — choose a check date", "Inviata — scegli la data di controllo"), B("Confirm the check date", "Conferma la data di controllo"), "owner"),
        CasePhases.Waiting => ("Waiting for supplier", B("Waiting for the supplier", "In attesa del fornitore"), B("Supplier reply", "Risposta del fornitore"), "supplier"),
        CasePhases.WaitingDue => ("Reply to register", B("Check date reached", "Data di controllo raggiunta"), B("Paste the reply or prepare a follow-up", "Incolla la risposta o prepara un sollecito"), "owner"),
        CasePhases.Review => ("Reply to review", B("Reply analysed", "Risposta analizzata"), B("Decide", "Decidi"), "owner"),
        CasePhases.Agree => ("Terms to confirm", B("Terms proposed", "Condizioni proposte"), B("Confirm as agreed", "Conferma come concordate"), "owner"),
        CasePhases.DocWait => ("Waiting for contract draft", B("Terms agreed — waiting for the draft", "Condizioni concordate — attesa bozza"), B("Draft contract from supplier", "Bozza di contratto dal fornitore"), "supplier"),
        CasePhases.DocCheck => ("Checking draft", B("Draft under check", "Bozza in verifica"), B("Resolve differences", "Risolvi le differenze"), "owner"),
        CasePhases.SignWait => ("Waiting for signature", B("Ready to sign — signing is outside Raffa", "Pronta alla firma — la firma è fuori da Raffa"), B("Sign, then add the signed file", "Firma, poi aggiungi il file firmato"), "owner"),
        CasePhases.SignDeclared => ("Signature declared", B("Signature declared — document to acquire", "Firma dichiarata — documento da acquisire"), B("Add the signed file", "Aggiungi il file firmato"), "owner"),
        CasePhases.SignConfirm => ("Signed · to confirm", B("Signed file received", "File firmato ricevuto"), B("Confirm it is the final signed version", "Conferma che è la versione firmata definitiva"), "owner"),
        CasePhases.Pipeline => ("Signed · in review", B("Negotiation completed · new contract in review", "Negoziazione completata · nuovo contratto in revisione"), B("Resolve flagged fields", "Risolvi i campi segnalati"), "owner"),
        CasePhases.Done => ("Renewed", B("Completed — contract signed", "Completata — contratto firmato"), B("Nothing open", "Niente di aperto"), "—"),
        CasePhases.NoticeOut => ("Not renewed", B("Not renewed / notice", "Non rinnovato / disdetta"), B("Exit tasks", "Attività di uscita"), "owner"),
        _ => ("Email to approve", B("Email to approve and send", "Email da approvare e inviare"), B("Approve, copy, send and confirm", "Approva, copia, invia e conferma"), "owner"),
    };

    /// <summary>The prototype's <c>shortStatus</c> (English code-names, as in the prototype).</summary>
    public static string ShortStatus(RenewalCase c)
    {
        if (c.Outcome is { Kind: "terminated" })
        {
            return "Not renewed";
        }

        if (c.Notice is { State: "sent" })
        {
            return "Notice sent · outcome to verify";
        }

        if (c.Alt is { State: "selected" } && c.Outcome is null)
        {
            return "Replacement in progress";
        }

        return Ph(c.Phase).Short;
    }

    private static string Apw(RenewalCase c) => c.Delegates.Count > 0 ? c.Delegates.GetAt(0).Key : c.Approver;

    /* ---------- outlook ---------- */

    private static readonly (Regex Re, CaseText Typical, CaseText Expect)[] Out =
    [
        (new Regex("price increase|uplift|price protection", RegexOptions.IgnoreCase), B("Increase tied to CPI with a cap of 3–5%, or fixed for the term", "Aumento legato all’indice prezzi con tetto 3–5%, oppure bloccato per la durata"), B("Likely — suppliers usually concede a cap or a freeze in exchange for a longer term.", "Probabile — di solito il fornitore concede un tetto o il blocco in cambio di una durata più lunga.")),
        (new Regex("auto-renew", RegexOptions.IgnoreCase), B("Renewal by written agreement, or at least 60–90 days notice before term end", "Rinnovo con accordo scritto, oppure preavviso di almeno 60–90 giorni prima della scadenza"), B("Likely — easy to win if you open the talks before the notice date.", "Probabile — facile da ottenere se apri la trattativa prima della data di disdetta.")),
        (new Regex("liab", RegexOptions.IgnoreCase), B("Cap at 12–24 months of fees, with a separate higher cap for data-breach claims", "Tetto a 12–24 mesi di canone, con un tetto più alto separato per violazioni dei dati"), B("Possible — large vendors rarely move on standard paper; a data-breach carve-out is the realistic ask.", "Possibile — i grandi fornitori cedono di rado sul contratto standard; la richiesta realistica è l’esclusione per violazione dati.")),
        (new Regex("terminat", RegexOptions.IgnoreCase), B("Exit at convenience is uncommon on committed terms; it usually comes with a fee", "Recesso libero raro sui contratti vincolati; di solito ha un costo"), B("Difficult — expect a trade: exit option against a higher unit price.", "Difficile — aspettati uno scambio: opzione di uscita contro un prezzo unitario più alto.")),
        (new Regex("payment", RegexOptions.IgnoreCase), B("Annual in advance is common; quarterly or Net 45–60 is often accepted", "Pagamento annuale anticipato diffuso; trimestrale o Net 45–60 spesso accettati"), B("Possible — often granted, sometimes with a small price premium.", "Possibile — spesso concesso, a volte con un piccolo sovrapprezzo.")),
        (new Regex("credit", RegexOptions.IgnoreCase), B("Credits capped at 10–25% of monthly fees; an exit for chronic failure is common in enterprise terms", "Crediti con tetto 10–25% del canone mensile; uscita per disservizi cronici comune nei contratti enterprise"), B("Possible — worth asking if uptime matters to you.", "Possibile — vale la pena chiederlo se la continuità del servizio è critica.")),
        (new Regex("commit|envelope|carry|roll|unused", RegexOptions.IgnoreCase), B("Carry-forward of 10–20% of unused volume is a common concession at renewal", "Riporto del 10–20% del volume non usato: concessione comune al rinnovo"), B("Possible — vendors prefer to grant it when you renew or grow the commit.", "Possibile — il fornitore lo concede più volentieri se rinnovi o aumenti l’impegno.")),
        (new Regex("reassign", RegexOptions.IgnoreCase), B("Moving licences between users within 30 days is common", "Riassegnare le licenze tra utenti entro 30 giorni è comune"), B("Likely — low cost for the vendor.", "Probabile — costo basso per il fornitore.")),
        (new Regex("support", RegexOptions.IgnoreCase), B("Support priced as a percentage of spend, with lower tiers above a volume threshold", "Supporto a percentuale sulla spesa, con scaglioni più bassi oltre una soglia"), B("Possible — tiered rates come up as spend grows.", "Possibile — gli scaglioni emergono quando la spesa cresce.")),
    ];

    private static CaseTable OutlookTable(CaseContext ctx)
    {
        var rows = CaseRules.Clauses(ctx).Select(x =>
        {
            var hit = Out.FirstOrDefault(o => o.Re.IsMatch(x.Type));
            var typical = hit.Re is null ? B("Usually negotiable at renewal, especially when tied to term or volume", "Di solito negoziabile al rinnovo, soprattutto se legata a durata o volume") : hit.Typical;
            var expect = hit.Re is null ? B("Depends on your leverage.", "Dipende dalla tua forza negoziale.") : hit.Expect;
            return (IReadOnlyList<CaseCell>)
            [
                new CaseCell(x.Type, 700),
                new CaseCell(x.Norm + " · " + x.Src),
                new CaseCell(typical),
                new CaseCell(B("{expect} Your ask: {ask}", "{expect} Tua richiesta: {ask}", ("expect", expect), ("ask", x.Ask)), 600),
            ];
        }).ToList();
        return new CaseTable(
            "minmax(110px,.8fr) minmax(0,1.3fr) minmax(0,1.3fr) minmax(0,1.5fr)",
            [B("Clause", "Clausola"), B("In your contract", "Nel contratto"), B("Typical practice", "Prassi tipica"), B("What to expect", "Cosa aspettarti")],
            rows);
    }

    /* ---------- view ---------- */

    /// <summary>The case as seen by <see cref="CaseContext.Viewer"/> (<c>RC.view</c> + the case screen composition).</summary>
    public static CaseView View(RenewalCase c, CaseContext ctx)
    {
        var v = ctx.Viewer;
        var cn = ctx.Contract;
        var f = CaseRules.Price(ctx);
        var ap = CaseRules.CanApprove(c, v);
        var wk = CaseRules.CanWork(c, v);
        var cc = CaseRules.CanClose(c, v);
        var rl = CaseRules.Role(c, v);
        var cl = CaseRules.Clauses(ctx);
        var terms = c.Terms;
        var cards = new List<CaseCard>();
        var noWorkWhy = NoWork;

        static CaseButton Bt(CaseText label, string act, CaseActionPayload? p = null, bool primary = false, bool off = false, CaseText? why = null) =>
            new(label, act, p ?? CaseActionPayload.None, primary, off, why ?? CaseText.Empty);

        foreach (var it in c.Feed)
        {
            if (it.Thread is not null && c.Thread is not null && it.Thread != c.Thread)
            {
                continue;
            }

            var d = new CaseCard { Id = it.Id, By = it.By, At = it.At, K = it.K, St = it.St };
            switch (it.K)
            {
                case "ov":
                {
                    var dd0 = DaysTo(ctx, cn.Cancel);
                    d.Kicker = B("Renewal case opened", "Caso di rinnovo aperto");
                    d.Title = cn.Supplier + " · " + cn.Name;
                    d.Stats =
                    [
                        new CaseStat(B("Days to notice", "Giorni alla disdetta"), B("{n} days", "{n} giorni", ("n", dd0)), dd0 <= 30 ? Urgent : null),
                        new CaseStat(B("Notice by", "Disdetta entro"), D(cn.Cancel)),
                        new CaseStat(B("Term ends", "Fine termine"), D(cn.End)),
                        new CaseStat(B("Annual spend", "Spesa annua"), CaseFormat.Chf(cn.Spend)),
                        new CaseStat(B("Uplift at renewal", "Aumento al rinnovo"), cn.Uplift, CaseRules.HasUplift(ctx) ? Alert : null),
                    ];
                    d.Bullets =
                    [
                        B("In your contract: {products}", "Nel tuo contratto: {products}", ("products", ProductsText(ctx))),
                        cn.Auto
                            ? B("Renews automatically for 12 months unless you give notice by {cancel}.", "Si rinnova da solo per 12 mesi se non dai disdetta entro il {cancel}.", ("cancel", D(cn.Cancel)))
                            : B("No auto-renewal: it ends on {end} unless you renegotiate.", "Nessun rinnovo automatico: termina il {end} se non rinegozi.", ("end", D(cn.End))),
                    ];
                    d.Lines = [B("How it works", "Come funziona")];
                    d.Rows =
                    [
                        new CaseRow("1", B("I ask you four short questions.", "Ti faccio quattro domande brevi.")),
                        new CaseRow("2", B("I propose a plan; you confirm it.", "Propongo un piano; tu lo confermi.")),
                        new CaseRow("3", B("I draft the emails, the CPO approves, you send from your own mailbox.", "Preparo le email, il CPO approva, tu invii dalla tua casella.")),
                    ];
                    d.Note = B("Nothing is sent from here.", "Da qui non parte nulla.");
                    d.Dim = c.Q > 0 || c.Phase != CasePhases.Interview;
                    break;
                }

                case "info":
                    d.Kicker = it.Kicker ?? CaseText.Empty;
                    d.Title = it.Title ?? CaseText.Empty;
                    d.Bullets = it.Bullets?.ToList() ?? [];
                    if (it.Note is not null)
                    {
                        d.Note = it.Note;
                    }

                    d.Dim = true;
                    break;
                case "recap":
                    RecapCard(c, ctx, d);
                    break;
                case "say":
                    d.Text = it.Text ?? CaseText.Empty;
                    break;
                case "user":
                    d.Text = it.Text ?? CaseText.Empty;
                    d.Tag = it.Tag ?? CaseText.Empty;
                    break;
                case "q":
                {
                    var qi = it.I ?? 0;
                    var q = QDef(qi, ctx, c);
                    var cur = c.Q == qi && c.Phase == CasePhases.Interview;
                    d.Kicker = B("Question {n} / 4", "Domanda {n} / 4", ("n", qi + 1));
                    d.Title = q.Text;
                    d.Lines = [q.Hint];
                    if (c.Interview is null && BriefHint(c.Brief, qi) is { } fromChat)
                    {
                        d.Lines.Add(fromChat);
                    }

                    if (cur)
                    {
                        d.Chips = q.Opts.Select(o => new CaseChip(
                            o.Label,
                            q.Multi ? "toggle" : "ans",
                            q.Multi ? new CaseActionPayload { K = o.Key } : new CaseActionPayload { Q = qi, Val = o.Key },
                            q.Multi && (c.Goals.Clauses ?? []).Contains(o.Key))).ToList();
                        if (q.Multi)
                        {
                            d.Buttons = [Bt(B("Continue", "Continua"), "q3done", primary: true)];
                            if (!c.Goals.Policy)
                            {
                                d.Buttons.Add(Bt(B("Add company policy (sample)", "Aggiungi policy aziendale (esempio)"), "policy"));
                            }
                        }

                        d.Current = true;
                        d.Chips.Add(new CaseChip(B("Other — write your own answer", "Altro — scrivi la tua risposta"), "ui", new CaseActionPayload { K = "other", V = qi }, c.Ui.Other == qi));
                        if (c.Ui.Other == qi)
                        {
                            var ot = c.Ui.OtherText ?? string.Empty;
                            d.Inputs = [new CaseInput(B("Your answer", "La tua risposta"), ot, "otherText", "text")];
                            d.Buttons.Add(Bt(B("Send my answer", "Invia la mia risposta"), "say", new CaseActionPayload { Text = ot }, primary: true, off: ot.Trim().Length == 0, why: B("Type your answer first", "Scrivi prima la tua risposta")));
                        }
                    }
                    else
                    {
                        d.Dim = true;
                        d.Lines = [B("Answer: {answer}", "Risposta: {answer}", ("answer", AnsLabel(c, ctx, qi)))];
                    }

                    break;
                }

                case "outlook":
                    d.Kicker = B("Before you start", "Prima di iniziare");
                    d.Title = B("What you can expect to renegotiate", "Cosa puoi aspettarti di rinegoziare");
                    d.Lines = [B("Read from your contract, clause by clause, against common practice for this kind of agreement. This is general practice, not a market benchmark — the supplier may differ.", "Letto dal tuo contratto, clausola per clausola, rispetto alla prassi comune per questo tipo di accordo. È prassi generale, non un benchmark di mercato: il fornitore può discostarsi.")];
                    d.Table = OutlookTable(ctx);
                    d.Pin = c.Phase is CasePhases.Interview or CasePhases.Plan;
                    break;
                case "policy":
                    d.Kicker = B("Company policy added", "Policy aziendale aggiunta");
                    d.Title = "Procurement Policy v3.2";
                    d.Rows = [new CaseRow(B("Scope", "Ambito"), B("Software contracts above CHF 100k", "Contratti software oltre CHF 100k"))];
                    d.Rows.AddRange(CaseRules.Policy.Select(pr => new CaseRow(pr.Bind ? B("Binding", "Vincolante") : B("Guidance", "Indicazione"), pr.Text)));
                    d.Note = B("No policy on file would not mean compliance has been checked. Raffa shows policy rules separately from its own proposals.", "L’assenza di una policy non equivale a una verifica di conformità. Raffa tiene le regole di policy separate dalle proprie proposte.");
                    break;
                case "plan":
                    PlanCard(c, ctx, it, d, wk);
                    break;
                case "email":
                    EmailCard(c, ctx, it, d, ap, wk);
                    break;
                case "rem":
                {
                    var r = c.Rems[it.Rid ?? 0];
                    d.Kicker = B("Check date", "Data di controllo");
                    if (r.State == "proposed")
                    {
                        d.Title = B("When should I remind you to check for a reply?", "Quando ti ricordo di controllare la risposta?");
                        d.Lines = [B("I will remind you on {date}. Your contractual notice date stays {cancel} — the check date never replaces it.", "Ti ricordo il {date}. La scadenza contrattuale di disdetta resta il {cancel} — la data di controllo non la sostituisce.", ("date", D(c.Ui.RemDate ?? r.Date)), ("cancel", D(cn.Cancel)))];
                        d.Chips = new[] { 1, 3, 5, 7 }.Select(n => new CaseChip(
                            n == 1 ? B("In {n} day", "Tra {n} giorno", ("n", n)) : B("In {n} days", "Tra {n} giorni", ("n", n)),
                            "remPick", new CaseActionPayload { N = n }, !c.Ui.RemCustom && (c.Ui.RemN ?? 0) == n)).ToList();
                        d.Chips.Add(new CaseChip(B("Custom…", "Personalizzato…"), "ui", new CaseActionPayload { K = "remCustom", V = true }, c.Ui.RemCustom));
                        if (c.Ui.RemCustom)
                        {
                            d.Inputs = [new CaseInput(B("Days from today", "Giorni da oggi"), c.Ui.RemNum ?? string.Empty, "remNum", "text")];
                        }

                        d.Buttons = [Bt(B("Confirm date", "Conferma data"), "remOk", new CaseActionPayload { Rid = it.Rid }, primary: true, off: !wk, why: noWorkWhy)];
                        d.Current = true;
                    }
                    else if (r.State == "set")
                    {
                        d.Title = B("Check set for {date}", "Controllo fissato per il {date}", ("date", D(r.Date)));
                        d.Lines = [B("Active even with the chat closed. It posts an app notice linked to this case — not a message to the supplier.", "Attivo anche a chat chiusa. Genera un avviso nell’app collegato a questo caso — non un messaggio al fornitore.")];
                        d.Current = true;
                        d.Tone = "wait";
                    }
                    else if (r.State == "due")
                    {
                        d.Title = B("Did you receive a reply?", "Hai ricevuto una risposta?");
                        d.Lines = [B("No reply has been registered in Raffa.", "Nessuna risposta registrata in Raffa.")];
                        d.Buttons =
                        [
                            Bt(B("Paste reply", "Incolla risposta"), "openReply", primary: true), Bt(B("Prepare follow-up", "Prepara sollecito"), "followup"),
                            Bt(B("Change date", "Cambia data"), "remChange"),
                        ];
                        d.Current = true;
                    }
                    else
                    {
                        d.Title = B("Check {date} — closed", "Controllo {date} — chiuso", ("date", D(r.Date)));
                        d.Dim = true;
                        d.Lines = [B("Cancelled or updated after a new reply or follow-up.", "Annullato o aggiornato dopo una nuova risposta o un sollecito.")];
                    }

                    break;
                }

                case "reply":
                {
                    var rp = c.Reps[it.Rid ?? 0];
                    d.Kicker = B("Supplier reply pasted · round {round}", "Risposta del fornitore incollata · giro {round}", ("round", rp.Round));
                    d.Title = B("Original text kept", "Testo originale conservato");
                    d.Pre = rp.Text;
                    d.Note = B("Pasted by {by} on {at}. The original stays next to Raffa’s interpretation.", "Incollata da {by} il {at}. L’originale resta accanto all’interpretazione di Raffa.", ("by", rp.By), ("at", D(rp.At)));
                    d.Dim = true;
                    break;
                }

                case "ana":
                {
                    var rp2 = c.Reps[it.Rid ?? 0];
                    d.Kicker = B("Analysis · reply {round}", "Analisi · risposta {round}", ("round", rp2.Round));
                    d.Title = B("What changed", "Cosa è cambiato");
                    d.Table = new CaseTable(
                        "minmax(110px,1fr) minmax(160px,1.8fr) minmax(130px,1.2fr) minmax(90px,.7fr)",
                        [B("Term⟦condition⟧", "Condizione"), B("Supplier says", "Il fornitore dice"), B("Your plan", "Il tuo piano"), B("Status", "Stato")],
                        rp2.Rows.Select(x =>
                        {
                            var (label, tone) = x.State switch
                            {
                                "unclear" => (B("Unclear", "Poco chiaro"), Alert),
                                "rejected" => (B("Rejected", "Rifiutato"), Alert),
                                "conflict" => (B("Conflict", "Conflitto"), Alert),
                                _ => (B("Proposed", "Proposto"), (string?)null),
                            };
                            var hasWarn = x.Warn.IsMessage || !string.IsNullOrEmpty(x.Warn.Plain);
                            return (IReadOnlyList<CaseCell>)
                            [
                                new CaseCell(x.Label, 600),
                                new CaseCell(hasWarn ? N("{said} — {warn}", ("said", x.Said), ("warn", x.Warn)) : x.Said, null, hasWarn ? Alert : null),
                                new CaseCell(x.Plan),
                                new CaseCell(label, 600, tone),
                            ];
                        }).ToList());
                    if (ctx.Services.Scripted)
                    {
                        d.Lines =
                        [
                            rp2.Round == 1
                                ? B("Nothing is agreed yet: a supplier proposal is not an agreement. Payment terms are unclear — I will ask for clarification in the counter-proposal.", "Nulla è concordato: una proposta del fornitore non è un accordo. I pagamenti sono poco chiari — chiederò chiarimenti nella controproposta.")
                                : B("These are proposals backed by the supplier’s written reply. They become “agreed” only when you confirm them.", "Sono proposte con evidenza scritta del fornitore. Diventano «concordate» solo quando le confermi."),
                        ];
                        d.Note = B("Prototype: the reading of the reply is scripted for the demo scenario.", "Prototipo: la lettura della risposta è scriptata per lo scenario demo.");
                    }
                    else
                    {
                        // Only what this reading found: no point is said to be unclear unless the reading marked it so.
                        var unclear = rp2.Rows.Where(r => r.State == "unclear").Select(r => r.Label.Render()).ToList();
                        d.Lines =
                        [
                            rp2.Round == 1
                                ? B("Nothing is agreed yet: a supplier proposal is not an agreement.", "Nulla è concordato: una proposta del fornitore non è un accordo.")
                                : B("These are proposals backed by the supplier’s written reply. They become “agreed” only when you confirm them.", "Sono proposte con evidenza scritta del fornitore. Diventano «concordate» solo quando le confermi."),
                        ];
                        if (unclear.Count > 0 && rp2.Round == 1)
                        {
                            d.Lines.Add(B("Unclear in the reply: {points} — I ask for clarification in the counter-proposal.", "Poco chiaro nella risposta: {points} — chiedo chiarimenti nella controproposta.", ("points", string.Join(", ", unclear))));
                        }
                    }

                    break;
                }

                case "dec":
                {
                    var q = c.Qs[it.Did ?? 0];
                    d.Kicker = B("Decision needed", "Decisione richiesta");
                    d.Title = ctx.Services.Scripted || CaseAgreement.OfferedPrice(c, 24) is not { } o24
                        ? B("The discount is tied to 24 months. What is your limit?", "Lo sconto è legato a 24 mesi. Qual è il tuo limite?")
                        : CaseAgreement.OfferedPrice(c, 12) is { } o12
                            ? B("The supplier offers {p24} for 24 months and {p12} for 12 months. What is your limit?", "Il fornitore offre {p24} per 24 mesi e {p12} per 12 mesi. Qual è il tuo limite?", ("p24", CaseRules.Ps(f, o24)), ("p12", CaseRules.Ps(f, o12)))
                            : B("The supplier offers {p24} for 24 months only. What is your limit?", "Il fornitore offre {p24} solo per 24 mesi. Qual è il tuo limite?", ("p24", CaseRules.Ps(f, o24)));
                    if (q.State == "open")
                    {
                        var p24 = CaseRules.Ps(f, CounterPrice(c, ctx, 24));
                        d.Lines = [B("Your plan asked for {term}. A longer commitment means less flexibility at the next renewal; a shorter one means no discount was offered. My only comparison is your current price ({price}) — no external benchmark.", "Il tuo piano chiedeva {term}. Un impegno più lungo riduce la flessibilità al prossimo rinnovo; uno più breve significa nessuno sconto. Il mio unico confronto è il tuo prezzo attuale ({price}) — nessun benchmark esterno.", ("term", AnsLabel(c, ctx, 1)), ("price", CaseRules.Ps(f, f.P0)))];
                        d.Chips =
                        [
                            new CaseChip(B("Accept up to 24 months if unit price ≤ {price}", "Accetto fino a 24 mesi se prezzo unitario ≤ {price}", ("price", p24)), "decide", new CaseActionPayload { Did = it.Did, Opt = "a" }, false, !wk),
                            new CaseChip(B("Stay at 12 months — decline the discount", "Resto a 12 mesi — rifiuto lo sconto"), "decide", new CaseActionPayload { Did = it.Did, Opt = "b" }, false, !wk),
                            new CaseChip(B("24 months with an exit option at month 12", "24 mesi con uscita al mese 12"), "decide", new CaseActionPayload { Did = it.Did, Opt = "c" }, false, !wk),
                        ];
                        d.Current = true;
                        d.Note = wk ? CaseText.Empty : B("Owner or delegate decides.", "Decide il responsabile o un delegato.");
                    }
                    else
                    {
                        d.Dim = true;
                        d.Lines = [B("Decision: {label} · {by}", "Decisione: {label} · {by}", ("label", q.Label), ("by", q.By))];
                    }

                    break;
                }

                case "agree":
                    AgreeCard(c, it, d, wk);
                    break;
                case "doc":
                    DocCard(c, it, d, wk);
                    break;
                case "signwait":
                    d.Kicker = B("Signature", "Firma");
                    d.Title = B("Ready to sign — outside Raffa", "Pronta alla firma — fuori da Raffa");
                    d.Lines = [B("Signing is not done in Raffa and I do not certify signatures. When it is signed, add the signed contract.", "La firma non avviene in Raffa e non certifico le firme. Quando è firmato, aggiungi il contratto firmato.")];
                    if (c.Phase is CasePhases.SignWait or CasePhases.SignDeclared)
                    {
                        d.Current = true;
                        d.Buttons = [Bt(B("Add signed contract (sample)", "Aggiungi contratto firmato (esempio)"), "addDoc", primary: true)];
                        if (c.Phase == CasePhases.SignWait)
                        {
                            d.Buttons.Add(Bt(B("Signature declared — file missing", "Firma dichiarata — file mancante"), "signDeclared"));
                        }
                        else
                        {
                            d.Banner = B("Signature declared — document to acquire. Task open.", "Firma dichiarata — documento da acquisire. Attività aperta.");
                        }
                    }
                    else
                    {
                        d.Dim = true;
                    }

                    break;
                case "sig":
                {
                    var signed = c.Signed ?? new CaseSigned();
                    var chk = ctx.Services.CheckSigned(c, ctx);
                    d.Kicker = B("Signed document", "Documento firmato");
                    d.Title = signed.File;
                    d.Lines =
                    [
                        chk.Differences == 0
                            ? B("Compared with the last verified draft (v{v}): same content on all 8 points. No differences to document.", "Confrontato con l’ultima bozza verificata (v{v}): stesso contenuto su tutti gli 8 punti. Nessuna differenza da documentare.", ("v", c.Docs.Count))
                            : B("Compared with the last verified draft (v{v}): {n} of {points} points differ. Resolve them before confirming.", "Confrontato con l’ultima bozza verificata (v{v}): {n} punti su {points} differiscono. Risolvili prima di confermare.", ("v", c.Docs.Count), ("n", chk.Differences), ("points", chk.Points)),
                    ];
                    if (c.Phase == CasePhases.SignConfirm)
                    {
                        d.Current = true;
                        d.Lines.Add(B("Confirm that this is the final signed version required by your process. A file name containing “signed” is not enough.", "Conferma che questa è la versione firmata definitiva richiesta dal tuo processo. Un nome file con «signed» non basta."));
                        d.Buttons = [Bt(B("Confirm as final signed document", "Conferma come documento firmato definitivo"), "sigOk", primary: true, off: !cc, why: NoClose)];
                        if (!cc)
                        {
                            d.Note = B("Waiting for {who} or a delegate with closing rights.", "In attesa di {who} o di un delegato con diritto di chiusura.", ("who", c.Owner));
                        }
                    }
                    else
                    {
                        d.Dim = true;
                        d.Lines.Add(B("Confirmed by {who}.", "Confermato da {who}.", ("who", signed.ConfirmedBy)));
                    }

                    break;
                }

                case "pipe":
                    PipeCard(c, d);
                    break;
                case "out":
                {
                    var o = c.Outcome ?? new CaseOutcome();
                    d.Kicker = B("Outcome", "Esito");
                    d.Title = B("Renewed — new contract scheduled", "Rinnovato — nuovo contratto programmato");
                    d.Rows =
                    [
                        new CaseRow(B("Starts", "Decorrenza"), o.Start is { } s ? D(s) : string.Empty),
                        new CaseRow(B("Term ends", "Fine termine"), D(o.End)),
                        new CaseRow(B("Next notice deadline", "Prossima scadenza disdetta"), o.Cancel is { } oc ? D(oc) : string.Empty),
                        new CaseRow(B("New price", "Nuovo prezzo"), o.Price ?? CaseText.Empty),
                        new CaseRow(B("Annual change vs previous (same quantities)", "Variazione annua vs precedente (stesse quantità)"), ((o.Delta ?? 0) > 0 ? "−" : "+") + CaseFormat.Chf(Math.Abs(o.Delta ?? 0)), 600),
                        new CaseRow(B("Previous contract", "Contratto precedente"), B("stays active until {end}", "resta attivo fino al {end}", ("end", D(o.PreviousEnd(cn.End))))),
                    ];
                    // The final document the new contract rests on (the demo script keeps the prototype's rows).
                    if (!ctx.Services.Scripted && !string.IsNullOrEmpty(o.File))
                    {
                        d.Rows.Add(new CaseRow(B("Signed document", "Documento firmato"), o.File));
                    }

                    d.Lines = [B("Signature, validation and start date are separate events. The change is computed from the two contracts’ prices — not a market saving. The next cycle is planned from the validated dates.", "Firma, validazione e decorrenza sono eventi distinti. La variazione è calcolata dai prezzi dei due contratti — non è un risparmio di mercato. Il prossimo ciclo è pianificato dalle date validate.")];
                    d.Buttons = [Bt(B("Open Contract 360", "Apri Contract 360"), "nav", new CaseActionPayload { To = "c360" }), Bt(B("See in Renewals", "Vedi in Rinnovi"), "nav", new CaseActionPayload { To = "renewals" })];
                    break;
                }

                case "ncheck":
                {
                    var dd = DaysTo(ctx, cn.Cancel);
                    var clause = cn.Push.FirstOrDefault(x => x.Type.Contains("renew", StringComparison.OrdinalIgnoreCase)) ?? cn.Push.FirstOrDefault();
                    d.Kicker = B("Non-renewal notice", "Disdetta");
                    d.Title = B("What the contract requires", "Cosa richiede il contratto");
                    d.Rows =
                    [
                        new CaseRow(B("Clause", "Clausola"), clause?.Src ?? "—"),
                        new CaseRow(B("Deadline", "Scadenza"), B("{date} · {n} days left", "{date} · {n} giorni", ("date", D(cn.Cancel)), ("n", dd)), 600),
                        new CaseRow(B("Effective end", "Cessazione effettiva"), D(cn.End)),
                        new CaseRow(B("Form", "Forma"), B("Written notice. The contract lists no email address for notices — recipient to be confirmed.", "Comunicazione scritta. Il contratto non indica un indirizzo email per le disdette — destinatario da confermare.")),
                        new CaseRow(B("Tasks", "Attività"), B("1 · Email notice (draft below) · 2 · Registered letter if required — confirm with Legal", "1 · Email di disdetta (bozza sotto) · 2 · Raccomandata se richiesta — conferma con Legal")),
                    ];
                    if (dd < 0)
                    {
                        d.Banner = B("The window appears expired or contradictory. I cannot promise an effective notice — verify with Legal.", "La finestra sembra scaduta o contraddittoria. Non posso promettere una disdetta efficace — verifica con Legal.");
                    }

                    if (c.Notice is { State: "check" })
                    {
                        d.Current = true;
                        d.Buttons = [Bt(B("Prepare the notice email", "Prepara l’email di disdetta"), "nStart", primary: true)];
                    }
                    else
                    {
                        d.Dim = true;
                    }

                    break;
                }

                case "nrec":
                    d.Kicker = B("Notice communicated", "Disdetta comunicata");
                    d.Title = B("Status: notice communicated — outcome to verify", "Stato: disdetta comunicata — esito da verificare");
                    d.Lines = [B("Sending the email is not the same as the contract ending. Attach the supplier’s confirmation, then record the outcome.", "Inviare l’email non equivale alla fine del contratto. Allega la conferma del fornitore, poi registra l’esito.")];
                    if (c.Notice is { State: "sent" } nn)
                    {
                        d.Current = true;
                        if (nn.Evidence)
                        {
                            d.Lines.Add(B("Evidence attached: {file}", "Prova allegata: {file}", ("file", nn.EvFile)));
                        }
                        else
                        {
                            d.Buttons.Add(Bt(B("Add receipt (sample)", "Aggiungi ricevuta (esempio)"), "nReceipt"));
                            d.Inputs = [new CaseInput(B("Proceed without evidence", "Procedi senza prova"), c.Ui.NoEv, "noEv", "check")];
                        }

                        d.Buttons.Add(Bt(B("Record outcome: not renewed", "Registra esito: non rinnovato"), "nOut", primary: true, off: !cc, why: NoClose));
                    }
                    else
                    {
                        d.Dim = true;
                    }

                    break;
                case "nout":
                {
                    var exits = cn.OblSup.Where(x => ExitRe().IsMatch(x.Text)).Select(x => x.Text + " (" + x.When + ")").ToList();
                    d.Kicker = B("Outcome", "Esito");
                    d.Title = B("Not renewed — notice", "Non rinnovato — disdetta");
                    d.Rows =
                    [
                        new CaseRow(B("Effective end", "Cessazione effettiva"), D(cn.End)),
                        new CaseRow(B("Successor", "Successore"), c.Alt?.Chosen is { } ch ? CaseText.Of(ch) : B("none — nothing activated", "nessuno — nulla attivato")),
                        new CaseRow(B("Exit tasks still open", "Attività di uscita aperte"), exits.Count > 0 ? CaseText.Of(string.Join(" · ", exits)) : B("Return of data per the agreement", "Restituzione dei dati come da contratto")),
                    ];
                    d.Buttons = [Bt(B("Open Contract 360", "Apri Contract 360"), "nav", new CaseActionPayload { To = "c360" })];
                    break;
                }

                case "areq":
                {
                    var alt = c.Alt!;
                    d.Kicker = B("Features", "Funzioni");
                    d.Title = B("What must the new supplier do?", "Cosa deve fare il nuovo fornitore?");
                    d.Bullets =
                    [
                        B("Raffa extracted these functions from your contract with {supplier} — product names are left out on purpose.", "Raffa ha estratto queste funzioni dal tuo contratto con {supplier} — i nomi dei prodotti sono lasciati fuori di proposito.", ("supplier", cn.Supplier)),
                        B("Other vendors answer on features, then map them to their own product.", "Gli altri fornitori rispondono sulle funzioni, poi le associano al loro prodotto."),
                        B("Untick what you do not need and add what is missing. Needed from {date}.", "Togli ciò che non serve e aggiungi ciò che manca. Servono dal {date}.", ("date", D(cn.End.AddDays(1)))),
                    ];
                    d.Rows = [new CaseRow(B("Sizing", "Dimensionamento"), SizeText(f))];
                    if (alt.State == "req")
                    {
                        d.Current = true;
                        d.Chips = alt.Reqs.Select((r, i) => new CaseChip(r.Text, "altReqToggle", new CaseActionPayload { I = i }, r.On)).ToList();
                        d.Inputs = [new CaseInput(B("Add a feature", "Aggiungi una funzione"), c.Ui.AltFeat ?? string.Empty, "altFeat", "text")];
                        d.Buttons =
                        [
                            Bt(B("Add feature", "Aggiungi funzione"), "altFeatAdd", off: (c.Ui.AltFeat ?? string.Empty).Trim().Length == 0, why: B("Type a feature first", "Scrivi prima una funzione")),
                            Bt(B("Confirm features", "Conferma funzioni"), "altReqs", primary: true),
                        ];
                    }
                    else
                    {
                        d.Dim = true;
                        d.Bullets = [];
                        d.Rows.AddRange(alt.Reqs.Select(r => new CaseRow("•", r.Text)));
                    }

                    break;
                }

                case "acand":
                {
                    var alt = c.Alt!;
                    d.Kicker = B("Alternatives", "Alternative");
                    d.Title = B("Which vendors should I ask?", "A quali fornitori chiedo?");
                    d.Lines = [alt.Vendors.Count > 0 ? B("Candidates: {list}", "Candidati: {list}", ("list", string.Join(", ", alt.Vendors))) : B("No candidate yet.", "Nessun candidato ancora.")];
                    if (alt.State == "cand")
                    {
                        d.Current = true;
                        d.Inputs = [new CaseInput(B("Vendor name", "Nome fornitore"), c.Ui.AltName ?? string.Empty, "altName", "text")];
                        d.Buttons =
                        [
                            Bt(B("Add vendor", "Aggiungi fornitore"), "altAdd"), Bt(B("Add two examples", "Aggiungi due esempi"), "altEx"),
                            Bt(B("Prepare RFQ emails", "Prepara email RFQ"), "altRfq", primary: true, off: alt.Vendors.Count == 0, why: B("Add at least one vendor", "Aggiungi almeno un fornitore")),
                        ];
                    }
                    else
                    {
                        d.Dim = true;
                    }

                    break;
                }

                case "aoff":
                {
                    var of = c.Alt!.Offers.TryGetValue(it.Vendor ?? string.Empty, out var o0) ? o0 : new CaseAltOffer();
                    d.Kicker = B("Offer · {vendor}", "Offerta · {vendor}", ("vendor", it.Vendor));
                    d.Title = B("Declared by the vendor — specific evidence, not a market benchmark", "Dichiarata dal fornitore — evidenza specifica, non un benchmark di mercato");
                    d.Rows =
                    [
                        new CaseRow(B("Price", "Prezzo"), f.P0 < 1000 ? "CHF " + CaseFormat.Js(of.Price) + " per " + f.Unit : CaseFormat.Chf(of.Total) + " / yr"),
                        new CaseRow(B("Term", "Durata"), B("{term} months", "{term} mesi", ("term", of.Term))),
                        new CaseRow(B("Features covered", "Funzioni coperte"), of.Cover),
                        new CaseRow(B("Migration", "Migrazione"), of.Mig),
                        new CaseRow(B("Missing data", "Dati mancanti"), of.Gap, null, Alert),
                    ];
                    break;
                }

                case "acmp":
                {
                    var ot = OffersTable(c, ctx);
                    d.Kicker = B("Compare", "Confronto");
                    d.Title = B("Evaluate the offers", "Valuta le offerte");
                    if (ot is not null)
                    {
                        d.Table = ot.Table;
                        d.Bullets =
                        [
                            B("Lowest annual cost: {vendor} ({total}) against {spend} today.", "Costo annuo più basso: {vendor} ({total}) contro {spend} oggi.", ("vendor", ot.Best), ("total", CaseFormat.Chf(ot.BestTotal ?? 0)), ("spend", CaseFormat.Chf(cn.Spend))),
                            B("Costs are what each vendor declared for the lines they quoted; where data is missing I say so.", "I costi sono quelli dichiarati da ciascun fornitore per le righe quotate; dove mancano dati lo dico."),
                            B("The same table stays in Renewals → Terms and goals.", "La stessa tabella resta in Rinnovi → Termini e obiettivi."),
                        ];
                    }

                    if (c.Alt is { State: "compare" } && ot is not null)
                    {
                        d.Current = true;
                        d.Buttons = ot.Names.Select(n => Bt(B("Select {vendor}", "Scegli {vendor}", ("vendor", n)), "altSel", new CaseActionPayload { Vendor = n }, primary: true, off: !cc, why: NoClose)).ToList();
                        d.Note = B("Add the other vendor’s offer from its thread to compare more.", "Aggiungi l’offerta dell’altro fornitore dal suo filone per confrontare di più.");
                    }
                    else
                    {
                        d.Dim = true;
                    }

                    break;
                }

                case "asel":
                    SelCard(c, ctx, d);
                    break;
                case "asel2":
                    d.Kicker = B("New vendor", "Nuovo fornitore");
                    d.Title = B("New contract received — enters the standard pipeline once", "Nuovo contratto ricevuto — entra una sola volta nella pipeline standard");
                    d.Lines = [B("Extraction and review of {file} run like any other contract. It stays linked to this case and to the replaced contract.", "Estrazione e revisione di {file} seguono il flusso di ogni altro contratto. Resta collegato a questo caso e al contratto sostituito.", ("file", c.Alt?.NewFile))];
                    d.Dim = true;
                    break;
            }

            if (c.Alt is not null)
            {
                int? ak = it.K switch { "areq" => 1, "acand" => 2, "aoff" => 4, "acmp" => 5, "asel" => 6, "asel2" => 7, _ => null };
                if (it.K == "email" && c.Emails[it.Eid ?? 0].Kind == "rfq")
                {
                    ak = 3;
                }

                if (it.K is "ncheck" or "nrec" or "nout" && c.Alt.State == "selected")
                {
                    ak = 6;
                }

                if (ak is { } step)
                {
                    d.Kicker = B("Replace the supplier · step {n} / 8 · {kicker}", "Sostituzione fornitore · passo {n} / 8 · {kicker}", ("n", step), ("kicker", d.Kicker));
                }
            }

            d.Plain = it.K is "say" or "user";
            cards.Add(d);
        }

        return Assemble(c, ctx, cards, f, rl, wk, ap, cc);
    }

    private static void RecapCard(RenewalCase c, CaseContext ctx, CaseCard d)
    {
        var cn = ctx.Contract;
        var g0 = c.Goals;
        var rcDone = c.Ui.RecapDone;
        d.Kicker = B("New chat · Back to your plan", "Nuova chat · Torna al tuo piano");
        d.Title = B("Do you still want to send the email to {supplier}?", "Vuoi comunque mandare l’email a {supplier}?", ("supplier", cn.Supplier));
        var vendors = string.Join(", ", c.Alt?.Vendors ?? []);
        d.Lines = [B("The request for proposal went out to {vendors}. Your renewal with {supplier} is exactly where you left it.", "La richiesta di offerta è partita per {vendors}. Il tuo rinnovo con {supplier} è esattamente dove l’avevi lasciato.", ("vendors", vendors), ("supplier", cn.Supplier))];
        CaseText Pv(int i)
        {
            var ok = i switch { 0 => g0.Prod is not null, 1 => g0.Term is not null, 2 => g0.Target is not null, _ => g0.Clauses is not null };
            return ok ? AnsLabel(c, ctx, i) : CaseText.Of("—");
        }

        var planK = c.PlanVer != 0
            ? (c.PlanOk
                ? B("Your plan v{ver} · confirmed", "Il tuo piano v{ver} · confermato", ("ver", c.PlanVer))
                : B("Your plan v{ver} · to confirm", "Il tuo piano v{ver} · da confermare", ("ver", c.PlanVer)))
            : B("Your plan", "Il tuo piano");
        d.Rows =
        [
            new CaseRow(B("Where you are", "A che punto sei"), B("Replacement, step 4 of 8 — waiting for the offers. Notice deadline {cancel} · {n} days left.", "Sostituzione, passo 4 di 8 — in attesa delle offerte. Disdetta entro il {cancel} · mancano {n} giorni.", ("cancel", D(cn.Cancel)), ("n", DaysTo(ctx, cn.Cancel))), 700),
            new CaseRow(planK, c.PlanVer != 0 ? B("Drafted", "Preparato") : B("Not drafted yet — the interview is still open", "Non ancora preparato — l’intervista è ancora aperta")),
            new CaseRow(B("Products", "Prodotti"), Pv(0)),
            new CaseRow(B("Term", "Durata"), Pv(1)),
            new CaseRow(B("Opening request", "Richiesta iniziale"), Pv(2)),
            new CaseRow(B("Clauses", "Clausole"), Pv(3)),
        ];
        if (rcDone is null)
        {
            d.Current = true;
            d.Chips =
            [
                new CaseChip(B("Yes — prepare the email to {supplier}", "Sì — prepara l’email a {supplier}", ("supplier", cn.Supplier)), "recapYes", CaseActionPayload.None, false),
                new CaseChip(B("Not yet — wait for the offers first", "Non ancora — aspetto prima le offerte"), "recapWait", CaseActionPayload.None, false),
                new CaseChip(B("No — I will only replace the supplier", "No — sostituisco solo il fornitore"), "recapNo", CaseActionPayload.None, false),
            ];
        }
        else
        {
            d.Dim = true;
            var ans = rcDone switch
            {
                "yes" => B("prepare the email", "prepara l’email"),
                "wait" => B("wait for the offers", "aspetto le offerte"),
                _ => B("only replace the supplier", "solo sostituzione"),
            };
            d.Lines.Add(B("Your answer: {answer}", "La tua risposta: {answer}", ("answer", ans)));
        }
    }

    private static void PlanCard(RenewalCase c, CaseContext ctx, CaseFeedItem it, CaseCard d, bool wk)
    {
        var cn = ctx.Contract;
        var g = c.Goals;
        var cur2 = it.Ver == c.PlanVer && !c.PlanOk && c.Phase == CasePhases.Plan;
        d.Kicker = B("Plan v{ver}", "Piano v{ver}", ("ver", it.Ver));
        d.Title = B("Renewal plan — {supplier}", "Piano di rinnovo — {supplier}", ("supplier", cn.Supplier));
        d.Rows =
        [
            new CaseRow(B("Situation", "Situazione"), B("Ends {end} · notice by {cancel} · {spend} / yr · uplift {uplift}", "Scade il {end} · disdetta entro il {cancel} · {spend} / anno · aumento {uplift}", ("end", D(cn.End)), ("cancel", D(cn.Cancel)), ("spend", CaseFormat.Chf(cn.Spend)), ("uplift", cn.Uplift))),
            new CaseRow(B("Products", "Prodotti"), AnsLabel(c, ctx, 0)),
            new CaseRow(B("Term", "Durata"), AnsLabel(c, ctx, 1)),
            new CaseRow(B("Opening request", "Richiesta iniziale"), AnsLabel(c, ctx, 2)),
        ];
        var sel = g.Clauses ?? [];
        foreach (var x in CaseRules.Clauses(ctx).Where(x => sel.Contains(x.K)))
        {
            var pol = g.Policy ? CaseRules.PolicyFor(x.Lk) : null;
            var basic = x.Ask + " · " + x.Src;
            CaseText val = pol is { } pp
                ? (pp.Bind ? B("{text} · policy (binding)", "{text} · policy (vincolante)", ("text", basic)) : B("{text} · policy (guidance)", "{text} · policy (indicazione)", ("text", basic)))
                : basic;
            d.Rows.Add(new CaseRow(x.Type, val, 600));
        }

        d.Rows.Add(new CaseRow(B("Hypotheses", "Ipotesi"), B("The supplier may trade price for a longer term — I will ask for your limit when it happens.", "Il fornitore potrebbe scambiare prezzo e durata — ti chiederò il limite quando succede.")));
        d.Rows.Add(new CaseRow(B("Unknown", "Non noto"), B("Usage data (not in the contract) · external market prices (not available, not used)", "Dati d’uso (non nel contratto) · prezzi di mercato esterni (non disponibili, non usati)")));
        if (cur2)
        {
            d.Current = true;
            d.Buttons =
            [
                new CaseButton(B("Confirm plan", "Conferma piano"), "confirmPlan", CaseActionPayload.None, true, !wk, NoWork),
                new CaseButton(B("Edit", "Modifica"), "editPlan", CaseActionPayload.None, false, false, CaseText.Empty),
            ];
        }
        else
        {
            d.Dim = it.Ver != c.PlanVer;
        }

        if (it.Ver == c.PlanVer)
        {
            if (c.Notice is null)
            {
                d.Buttons.Add(new CaseButton(B("Prepare non-renewal notice", "Prepara disdetta"), "startNotice", CaseActionPayload.None, false, false, CaseText.Empty));
            }

            if (c.Alt is null)
            {
                d.Buttons.Add(new CaseButton(B("Evaluate alternatives", "Valuta alternative"), "startAlt", CaseActionPayload.None, false, false, CaseText.Empty));
            }
        }
    }

    private static void EmailCard(RenewalCase c, CaseContext ctx, CaseFeedItem it, CaseCard d, bool ap, bool wk)
    {
        var e = c.Emails[it.Eid ?? 0];
        var es = e.State;
        var sup = es == EmailStates.Superseded;
        // An unsent draft is the step to work on only while the negotiation runs: once the signed
        // contract is in (pipeline) or the case has an outcome, a leftover request is history.
        var cur3 = !sup && es != EmailStates.Sent && c.Outcome is null && c.Phase != CasePhases.Pipeline;
        var apw0 = Apw(c);
        CaseText sl = es switch
        {
            EmailStates.Draft => B("Draft — not yet submitted for approval", "Bozza — non ancora in approvazione"),
            EmailStates.ToApprove => B("To approve — waiting for {who}", "Da approvare — in attesa di {who}", ("who", apw0)),
            EmailStates.Approved => B("Approved v{ver} by {who} — to copy and send", "Approvata v{ver} da {who} — da copiare e inviare", ("ver", e.ApprovedVer), ("who", e.ApprovedBy)),
            EmailStates.Copied => B("Copied — send from your mailbox, then confirm", "Copiata — invia dalla tua casella, poi conferma"),
            EmailStates.Sent => B("Send confirmed by {who} on {at}", "Invio confermato da {who} il {at}", ("who", e.Sent?.By), ("at", e.Sent?.At)),
            _ => B("Superseded — a newer reply arrived", "Superata — è arrivata una nuova risposta"),
        };
        d.Kicker = N("{kind} · v{ver}", ("kind", Kl(e.Kind)), ("ver", e.Ver));
        d.Title = e.Subject;
        d.Dim = sup;
        d.Current = cur3;
        string[] ord = [EmailStates.Draft, EmailStates.ToApprove, EmailStates.Approved, EmailStates.Copied, EmailStates.Sent];
        var pos = Array.IndexOf(ord, es);
        CaseText[] stepLabels = [B("Draft", "Bozza"), B("To approve", "Da approvare"), B("Approved", "Approvata"), B("Copied", "Copiata"), B("Send confirmed", "Invio confermato")];
        d.Steps = stepLabels.Select((lb, i) => new CaseCardStep(lb, !sup && pos >= i, !sup && pos == i)).ToList();
        d.Email = new CaseCardEmail
        {
            Eid = e.Id,
            To = e.To,
            ToMissing = string.IsNullOrEmpty(e.To) && cur3,
            ToKey = "to:" + CaseFormat.Js(e.Id),
            StateKey = es,
            State = sl,
            Subject = e.Subject,
            Body = e.Body,
            Attach = e.Attach,
            Editing = c.Ui.Edit == e.Id && cur3,
            EditBody = c.Ui.EditBody ?? string.Empty,
            ShowBody = !sup,
            SentNote = e.Sent is { Edited: true }
                ? B("The text actually sent was edited after approval; it was confirmed afterwards and may differ from the approved version.", "Il testo effettivamente inviato è stato modificato dopo l’approvazione; è stato confermato a posteriori e può differire dalla versione approvata.")
                : CaseText.Empty,
            AttachNote = e.Attach.Count > 0
                ? B("Attach in your mailbox: {files} — the Copy command does not include attachments.", "Allega nella tua casella: {files} — il comando Copia non include gli allegati.", ("files", string.Join(", ", e.Attach)))
                : CaseText.Empty,
        };
        var cf2 = cur3 && c.Ui.Conflict is { } cfx && cfx.Eid == e.Id ? c.Ui.Conflict : null;
        if (cf2 is not null)
        {
            d.Banner = B("{who} updated this draft to v{ver} at {at}{invalidated}. {next}", "{who} ha aggiornato questa bozza alla v{ver} alle {at}{invalidated}. {next}",
                ("who", cf2.By), ("ver", cf2.Ver), ("at", cf2.At),
                ("invalidated", cf2.PrevState != EmailStates.Draft ? B(". The approval of the previous version is no longer valid", ". L’approvazione della versione precedente non è più valida") : CaseText.Empty),
                ("next", cf2.Yours is not null ? B("Your unsaved text was kept and can be recovered.", "Il tuo testo non salvato è stato conservato e può essere recuperato.") : B("Review the current version before it goes for approval.", "Rivedi la versione corrente prima di mandarla in approvazione.")));
        }

        if (!cur3)
        {
            return;
        }

        var eid = new CaseActionPayload { Eid = e.Id };
        if (string.IsNullOrEmpty(e.To) && e.Kind != "notice" && ctx.Services.ExampleAddress(e, ctx) is not null)
        {
            d.Buttons.Add(new CaseButton(B("Use example address (demo)", "Usa indirizzo di esempio (demo)"), "toEx", eid));
        }

        if (c.Ui.Edit == e.Id)
        {
            d.Buttons.Add(new CaseButton(B("Save changes", "Salva modifiche"), "saveEdit", eid, true));
            d.Buttons.Add(new CaseButton(B("Cancel", "Annulla"), "cancelEdit", CaseActionPayload.None));
        }
        else if (c.Ui.SentOpen == e.Id)
        {
            d.Inputs =
            [
                new CaseInput(B("Recipient", "Destinatario"), e.To, "to:" + CaseFormat.Js(e.Id), "text"),
                new CaseInput(B("Date and time sent", "Data e ora di invio"), c.Ui.SentDate ?? string.Empty, "sentDate", "text"),
                new CaseInput(B("I edited the text in my mailbox", "Ho modificato il testo nella mia casella"), c.Ui.SentEdited, "sentEdited", "check"),
            ];
            if (c.Ui.SentEdited)
            {
                d.Inputs.Add(new CaseInput(B("Paste the text actually sent", "Incolla il testo effettivamente inviato"), c.Ui.SentText ?? string.Empty, "sentText", "area"));
            }

            d.Buttons =
            [
                new CaseButton(B("Confirm sent", "Conferma invio"), "sentOk", eid, true, !wk, NoWork),
                new CaseButton(B("Cancel", "Annulla"), "sentCancel", CaseActionPayload.None),
            ];
            d.Note = B("Raffa records your declaration of sending; it does not verify delivery or reading.", "Raffa registra la tua dichiarazione di invio; non verifica consegna o lettura.");
        }
        else
        {
            if (cf2 is not null)
            {
                d.Buttons.Add(new CaseButton(cf2.Yours is not null ? B("Recover my text", "Recupera il mio testo") : B("Review the changes", "Rivedi le modifiche"), "conflictRecover", CaseActionPayload.None, true));
                d.Buttons.Add(new CaseButton(B("OK, continue", "Ok, continua"), "conflictDismiss", CaseActionPayload.None));
            }
            else if (es == EmailStates.Draft)
            {
                d.Buttons.Add(new CaseButton(B("Send for approval", "Invia in approvazione"), "requestAppr", eid, true, !wk || string.IsNullOrEmpty(e.To), !wk ? NoWork : NoRecipient));
                if (ap && !string.IsNullOrEmpty(e.To))
                {
                    d.Buttons.Add(new CaseButton(B("Approve this version", "Approva questa versione"), "approve", eid));
                }
            }
            else if (es == EmailStates.ToApprove)
            {
                d.Buttons.Add(new CaseButton(B("Approve this version", "Approva questa versione"), "approve", eid, true, !ap, NoApprove));
                if (!ap)
                {
                    d.Note = B("Waiting for {who} (approver or explicit delegate). You can still edit; any edit returns it to draft.", "In attesa di {who} (approvatore o delegato esplicito). Puoi ancora modificare; ogni modifica la riporta in bozza.", ("who", apw0));
                }
            }

            if (es is EmailStates.Approved or EmailStates.Copied)
            {
                d.Buttons.Add(new CaseButton(es == EmailStates.Copied ? B("Copy again", "Copia di nuovo") : B("Copy email", "Copia email"), "copy", eid, es == EmailStates.Approved));
                d.Buttons.Add(new CaseButton(B("I sent this version", "Ho inviato questa versione"), "sentOpen", eid, es == EmailStates.Copied));
            }

            if (cf2 is null)
            {
                d.Buttons.Add(new CaseButton(B("Edit", "Modifica"), "edit", eid));
            }

            if (es is EmailStates.Approved or EmailStates.Copied)
            {
                d.Note = B("Approved by {who}. Any edit invalidates this approval. Copy does not mean sent.", "Approvata da {who}. Ogni modifica invalida questa approvazione. Copiare non significa inviare.", ("who", e.ApprovedBy));
            }
        }
    }

    private static void AgreeCard(RenewalCase c, CaseFeedItem it, CaseCard d, bool wk)
    {
        var q3 = c.Qs[it.Did ?? 0];
        var liabConf = LiabilityConflict(c);
        d.Kicker = B("Confirm terms", "Conferma condizioni");
        d.Title = B("Mark these as agreed?", "Segnare queste come concordate?");
        CaseText Unit(string k, string? val)
        {
            if (string.IsNullOrEmpty(val))
            {
                return "—";
            }

            return k switch
            {
                "term" when IsPlainNumber(val) => B("{val} months", "{val} mesi", ("val", val)),
                "liab" when IsPlainNumber(val) => B("{val} months of fees", "{val} mesi di canone", ("val", val)),
                _ => val,
            };
        }

        d.Rows = c.Terms.Where(kv => kv.Value.State is "proposed" or "agreed").Select(kv =>
        {
            var t = kv.Value;
            var v = t.State == "agreed"
                ? B("{val} · agreed", "{val} · concordato", ("val", Unit(kv.Key, t.Val)))
                : B("{val} · proposed in {ev}", "{val} · proposto in {ev}", ("val", Unit(kv.Key, t.Val)), ("ev", t.Ev));
            return new CaseRow(t.Label, v, 600);
        }).ToList();
        var miss = c.Terms.Where(kv => kv.Value.State == "open").Select(kv => kv.Value.Label).ToList();
        if (miss.Count > 0)
        {
            d.Rows.Add(new CaseRow(B("Not covered by this reply", "Non coperto da questa risposta"), B("{list} — still open, not part of this confirmation.", "{list} — ancora aperto, non incluso in questa conferma.", ("list", CaseText.Join(", ", miss))), null, Muted));
        }

        if (q3.State == "agree")
        {
            d.Current = true;
            d.Lines = [B("“Agreed” needs the written exchange as evidence and your confirmation. Silence is not acceptance.", "«Concordato» richiede lo scambio scritto come evidenza e la tua conferma. Il silenzio non è accettazione.")];
            if (liabConf)
            {
                d.Banner = B("Liability {liab} months conflicts with Procurement Policy v3.2 (binding, min. 24). A deviation needs authorization from the competent role — collected outside Raffa in this release.", "Responsabilità {liab} mesi in conflitto con la Procurement Policy v3.2 (vincolante, min. 24). La deroga richiede l’autorizzazione del ruolo competente — raccolta fuori da Raffa in questo rilascio.", ("liab", CaseAgreement.ProposedLiability(c)));
                d.Inputs = [new CaseInput(B("Exception authorized by (name / role)", "Deroga autorizzata da (nome / ruolo)"), c.Ui.Auth ?? string.Empty, "auth", "text")];
            }

            d.Buttons = [new CaseButton(B("Confirm as agreed", "Conferma come concordate"), "agreeOk", new CaseActionPayload { Did = it.Did }, true, !wk, NoWork)];
        }
        else
        {
            d.Dim = true;
            d.Lines = [B("Confirmed as agreed by {who}", "Confermate come concordate da {who}", ("who", q3.By))];
        }
    }

    private static void DocCard(RenewalCase c, CaseFeedItem it, CaseCard d, bool wk)
    {
        var ver = it.V ?? 1;
        var dc = c.Docs[ver - 1];
        var latest = ver == c.Docs.Count;
        var diffs = dc.Rows.Count(x => x.Res == "diff");
        var unr = dc.Rows.Count(x => x.Res == "unread");
        var okN = dc.Rows.Count(x => x.Res is "ok" or "manual");
        d.Kicker = B("Contract draft v{v}", "Bozza di contratto v{v}", ("v", ver));
        d.Title = dc.File;
        d.Banner = B("Draft — not an active contract. It does not count as spend and does not trigger a new renewal.", "Bozza — non è un contratto attivo. Non conta come spesa e non genera un nuovo rinnovo.");
        d.Table = new CaseTable(
            "minmax(110px,1fr) minmax(130px,1.3fr) minmax(150px,1.6fr) minmax(80px,.6fr) minmax(70px,.5fr)",
            [B("Point", "Punto"), B("Agreed", "Concordato"), B("In the draft", "Nella bozza"), B("Result", "Esito"), B("Source", "Fonte")],
            dc.Rows.Select(x =>
            {
                var (label, tone) = x.Res switch
                {
                    "diff" => (B("Differs", "Differisce"), Alert),
                    "unread" => (B("Not read", "Non letto"), Alert),
                    "manual" => (B("Manual", "Manuale"), (string?)null),
                    _ => (B("Matches", "Coincide"), (string?)null),
                };
                return (IReadOnlyList<CaseCell>)
                [
                    new CaseCell(x.Label, 600), new CaseCell(x.Agreed), new CaseCell(x.Draft, null, x.Res is "diff" or "unread" ? Alert : null),
                    new CaseCell(label, 600, tone), new CaseCell(x.Src, null, Muted),
                ];
            }).ToList());
        d.Lines = [B("{diffs} difference{s1} · {unr} point{s2} not read · {ok} match", "Differenze: {diffs} · punti non letti: {unr} · coincidono: {ok}", ("diffs", diffs), ("s1", diffs == 1 ? string.Empty : "s"), ("unr", unr), ("s2", unr == 1 ? string.Empty : "s"), ("ok", okN))];
        if (latest && c.Phase == CasePhases.DocCheck)
        {
            d.Current = true;
            if (diffs > 0)
            {
                d.Buttons.Add(new CaseButton(B("Prepare correction email", "Prepara email correttiva"), "fixEmail", CaseActionPayload.None, true));
            }

            if (unr > 0)
            {
                d.Buttons.Add(new CaseButton(B("Mark annex verified manually", "Segna allegato verificato a mano"), "markAnnex", new CaseActionPayload { Version = ver }, false, !wk, NoWork));
            }

            d.Note = B("A revised draft restarts the check. When a new version arrives, add it with “Add contract”.", "Una bozza rivista riavvia la verifica. Quando arriva una nuova versione, aggiungila con «Aggiungi contratto».");
        }
        else if (!latest)
        {
            d.Dim = true;
        }
        else
        {
            d.Lines.Add(B("Every point was read and matches the agreed position.", "Ogni punto è stato letto e coincide con la posizione concordata."));
        }
    }

    private static void PipeCard(RenewalCase c, CaseCard d)
    {
        var pp = c.Pipe ?? new CasePipe();
        d.Kicker = B("Negotiation completed · new contract in review", "Negoziazione completata · nuovo contratto in revisione");
        if (pp.Extracted)
        {
            CaseText[] sn = [B("Upload", "Caricamento"), B("Processing", "Elaborazione"), B("Extraction", "Estrazione"), B("Review", "Revisione"), B("Validation", "Validazione")];
            d.Title = B("Standard pipeline — same one, inside the chat", "Pipeline standard — la stessa, dentro la chat");
            d.Steps = sn.Select((x, i) => new CaseCardStep(x, pp.Stage >= i, pp.Stage == i)).ToList();
        }
        else
        {
            // No extraction ran on the signed file: no processing stage is shown as done.
            d.Title = B("Check the signed contract before it becomes the new contract", "Verifica il contratto firmato prima che diventi il nuovo contratto");
            d.Steps =
            [
                new CaseCardStep(B("Upload", "Caricamento"), true, false),
                new CaseCardStep(B("Review", "Revisione"), pp.Stage >= 3, pp.Stage == 3),
                new CaseCardStep(B("Validation", "Validazione"), pp.Stage >= 4, pp.Stage == 4),
            ];
        }

        if (pp.Stage >= 3)
        {
            var open = pp.Weak.Count(x => x.State == "open");
            d.Lines = pp.Extracted
                ? [B("41 facts extracted · 39 above threshold · 2 below 80% for you to decide. An extraction error never reopens the signed negotiation.", "41 fatti estratti · 39 sopra soglia · 2 sotto l’80% da decidere. Un errore di estrazione non riapre la negoziazione firmata.")]
                : [B("Raffa did not read the signed document automatically. Check these {n} values against it: once validated they become the contract’s new terms. A difference never reopens the signed negotiation — correct the value.", "Raffa non ha letto automaticamente il documento firmato. Verifica questi {n} valori sul documento: una volta validati diventano le nuove condizioni del contratto. Una differenza non riapre la negoziazione firmata — correggi il valore.", ("n", pp.Weak.Count))];
            d.Fields = pp.Weak.Select((w, i) =>
            {
                var key = "cf" + CaseFormat.Js(i);
                return new CaseField(w.Name, w.Val, w.Conf is { } cf ? CaseFormat.Js(cf) + "%" : string.Empty, w.State, w.State == "open",
                    w.State == "accepted" ? B("Accepted", "Accettato") : w.State == "corrected" ? B("Corrected", "Corretto") : CaseText.Empty,
                    key, c.Ui.Cf.TryGetValue(key, out var iv) ? iv : string.Empty, i, w.Src);
            }).ToList();
            if (pp.Stage == 3)
            {
                d.Current = true;
                var why = pp.Extracted
                    ? B("Decide both fields first", "Decidi prima i due campi")
                    : B("Accept or correct each value first ({n} still open)", "Accetta o correggi prima ogni valore ({n} ancora aperti)", ("n", open));
                d.Buttons = [new CaseButton(B("Mark as validated", "Segna come validato"), "validate", CaseActionPayload.None, true, open > 0 || (!pp.Extracted && pp.Weak.Count == 0), why)];
            }
        }
        else
        {
            d.Lines = [B("Processing the signed contract…", "Elaborazione del contratto firmato…")];
        }
    }

    private static void SelCard(RenewalCase c, CaseContext ctx, CaseCard d)
    {
        var alt = c.Alt!;
        var sup = ctx.Contract.Supplier;
        d.Kicker = B("Replacement", "Sostituzione");
        d.Title = B("{vendor} selected — the current contract is NOT closed", "Scelto {vendor} — il contratto attuale NON è chiuso", ("vendor", alt.Chosen));
        d.Rows =
        [
            new CaseRow(B("Exit track", "Ramo uscita"), alt.ExitDone ? B("Done — not renewed", "Fatto — non rinnovato") : B("Open — send notice to {supplier}", "Aperto — disdetta a {supplier}", ("supplier", sup)), 600, alt.ExitDone ? null : Alert),
            new CaseRow(B("New agreement", "Nuovo accordo"), alt.NewDone ? B("Signed contract added", "Contratto firmato aggiunto") : B("Open — add the signed contract", "Aperto — aggiungi il contratto firmato"), 600, alt.NewDone ? null : Alert),
        ];
        d.Lines = [B("The case closes as “replacement” only when the previous contract’s obligations are resolved or assigned, and the new contract is acquired. No implicit termination.", "Il caso si chiude come «sostituzione» solo quando gli adempimenti del contratto precedente sono risolti o assegnati e il nuovo contratto è acquisito. Nessuna cessazione implicita.")];
        if (!alt.ExitDone || !alt.NewDone)
        {
            d.Current = true;
            if (!alt.ExitDone && c.Notice is null)
            {
                d.Buttons.Add(new CaseButton(B("Prepare non-renewal notice", "Prepara disdetta"), "startNotice", CaseActionPayload.None, true));
            }

            if (!alt.NewDone && alt.ExitDone)
            {
                d.Buttons.Add(new CaseButton(B("Add new vendor contract (sample)", "Aggiungi contratto nuovo fornitore (esempio)"), "altNew", new CaseActionPayload { Vendor = alt.Chosen }, true));
            }

            if (!alt.ExitDone)
            {
                d.Lines.Add(B("Next: step 6 — give notice. The new contract comes right after.", "Prossimo: passo 6 — dai la disdetta. Subito dopo il nuovo contratto."));
            }
        }
    }

    private static CaseOffersTable? OffersTable(RenewalCase c, CaseContext ctx)
    {
        if (c.Alt is null || c.Alt.Offers.Count == 0)
        {
            return null;
        }

        var cn = ctx.Contract;
        var rows = new List<IReadOnlyList<CaseCell>>
        {
            new List<CaseCell>
            {
                new(B("Current · {supplier}", "Attuale · {supplier}", ("supplier", cn.Supplier)), 700), new(B("{amount} / yr", "{amount} / anno", ("amount", CaseFormat.Chf(cn.Spend)))),
                new("—"), new(B("to {end}", "fino al {end}", ("end", D(cn.End)))), new("—"), new("—"), new(B("uplift {uplift}", "aumento {uplift}", ("uplift", cn.Uplift))),
            },
        };
        string? best = null;
        double bestTot = 0;
        foreach (var (n, o) in c.Alt.Offers)
        {
            var dPct = CaseFormat.Round((o.Total / cn.Spend - 1) * 100);
            if (best is null || o.Total < bestTot)
            {
                best = n;
                bestTot = o.Total;
            }

            rows.Add(new List<CaseCell>
            {
                new(n, 700), new(B("{amount} / yr", "{amount} / anno", ("amount", CaseFormat.Chf(o.Total)))), new((dPct > 0 ? "+" : string.Empty) + CaseFormat.Js(dPct) + "%", 700, dPct < 0 ? null : Alert),
                new(B("{term} mo", "{term} mesi", ("term", o.Term))), new(o.Cover), new(o.Mig), new(o.Gap, null, Alert),
            });
        }

        return new CaseOffersTable(
            c.Alt.Offers.Keys.ToList(),
            best,
            best is null ? null : bestTot,
            new CaseTable(
                "minmax(110px,1fr) minmax(110px,.9fr) minmax(70px,.6fr) minmax(70px,.6fr) minmax(90px,.7fr) minmax(80px,.7fr) minmax(130px,1.1fr)",
                [B("Option", "Opzione"), B("Annual cost", "Costo annuo"), B("vs current", "vs attuale"), B("Term", "Durata"), B("Features covered", "Funzioni coperte"), B("Migration", "Migrazione"), B("Missing data", "Dati mancanti")],
                rows));
    }

    private static List<CaseTermCompare> TermsCompare(RenewalCase c, CaseContext ctx)
    {
        var f = CaseRules.Price(ctx);
        var cn = ctx.Contract;
        var cl = CaseRules.Clauses(ctx);
        var keys = new List<string> { "price", "term" };
        if (CaseRules.HasUplift(ctx))
        {
            keys.Add("uplift");
        }

        foreach (var k in c.Goals.Clauses ?? [])
        {
            var x = cl.FirstOrDefault(z => z.K == k);
            if (x is not null && !keys.Contains(x.Lk))
            {
                keys.Add(x.Lk);
            }
        }

        foreach (var k in c.Terms.Keys)
        {
            if (!keys.Contains(k))
            {
                keys.Add(k);
            }
        }

        (CaseText V, string Src, string? Ask) Cur(string k)
        {
            if (k == "price")
            {
                return (CaseRules.Ps(f, f.P0), "price table", null);
            }

            if (k == "term")
            {
                return (D(cn.Start) + " → " + D(cn.End), "§ 2.1", null);
            }

            if (k == "uplift")
            {
                return (cn.Uplift == "0%" ? B("none", "nessuno") : cn.Uplift, "§ 6.2", null);
            }

            var x = cl.FirstOrDefault(z => z.Lk == k);
            return x is not null ? (x.Norm, x.Src, x.Ask) : ("—", string.Empty, null);
        }

        return keys.Select(k =>
        {
            c.Terms.TryGetValue(k, out var t);
            var cu = Cur(k);
            CaseText label = t is not null
                ? t.Label
                : cl.FirstOrDefault(z => z.Lk == k)?.Type is { } ty
                    ? ty
                    : k switch { "price" => B("Unit price", "Prezzo unitario"), "term" => B("Term", "Durata"), "uplift" => B("Uplift", "Aumento annuo"), _ => k };
            CaseText? pv = t?.Val is { Length: > 0 } tv
                ? k switch
                {
                    "term" when IsPlainNumber(tv) => B("{val} months", "{val} mesi", ("val", tv)),
                    "liab" when IsPlainNumber(tv) => B("{val} months of fees", "{val} mesi di canone", ("val", tv)),
                    _ => tv,
                }
                : null;
            CaseText from = cu.Src.Length == 0
                ? CaseText.Empty
                : cu.Src == "price table"
                    ? B("Contract · price table in the contract", "Contratto · tabella prezzi nel contratto")
                    : B("Contract · {src}", "Contratto · {src}", ("src", cu.Src));
            CaseText propFrom = t?.Ev is { Length: > 0 } ev
                ? B("Supplier {ev}", "Fornitore · {ev}", ("ev", ev.StartsWith("reply ", StringComparison.Ordinal) ? B("reply {n}", "risposta {n}", ("n", ev[6..])) : ev))
                : cu.Ask is not null
                    ? B("Your ask: {ask}", "Tua richiesta: {ask}", ("ask", cu.Ask))
                    : B("no reply yet", "nessuna risposta ancora");
            var tone = t is { State: "agreed" } ? Strong : t is { State: "rejected" or "unclear" } ? Alert : Muted;
            return new CaseTermCompare(label, Gloss(k) ?? CaseText.Empty, cu.V, from, pv ?? "—", propFrom, t is not null ? TermStateLabel(t.State) : B("Open", "Aperto"), tone, t is { State: "agreed" } ? 700 : 600);
        }).ToList();
    }

    [GeneratedRegex("return|data|offboard", RegexOptions.IgnoreCase)]
    private static partial Regex ExitRe();

    [GeneratedRegex(@"(?<=[.!?])\s")]
    private static partial Regex SentenceEnd();

    /// <summary>A unit ("months", "months of fees") is added only to a number: a text value such as
    /// "No new liability cap" or "12 months" is shown as written.</summary>
    private static bool IsPlainNumber(string value) =>
        decimal.TryParse(value.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _);
}
