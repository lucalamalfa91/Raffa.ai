/**
 * `view(case, ctx)` -- everything the UI renders for a case: the chat cards (each tagged with its
 * wizard step), the header, the stepper, checklist, timeline, terms summary and team. Pure: it
 * derives the view from the case and its context and never changes either.
 *
 * Buttons that the viewer may not press are still returned, with `off: true` and the reason in `why`,
 * so the UI can show them disabled next to the reason instead of hiding them.
 */

import { checklist } from "./agenda";
import { addDays, daysBetween } from "./dates";
import {
  EMAIL_KIND,
  EXAMPLES,
  NO_APPROVE,
  NO_CLOSE,
  NO_WORK,
  PHASES,
  POLICY,
  STEPS,
  answerLabel,
  canApprove,
  canClose,
  canWork,
  clausesOf,
  figures,
  liabilityConflict,
  outlookTable,
  permissionsOf,
  priceLabel,
  questionDef,
  responsibilityOf,
  shortStatus,
  stage,
} from "./model";
import { chf, pick, tx, type Bilingual, type Lang } from "./text";
import type {
  Card,
  CardButton,
  CardChip,
  CardInput,
  CardRow,
  Case,
  CaseContext,
  CaseHeader,
  CasePerson,
  CaseView,
  Email,
  FeedItem,
  Responsibility,
  TableCell,
  TermView,
  TimelineEntry,
} from "./types";

interface ButtonOptions {
  primary?: boolean;
  off?: boolean;
  why?: string;
}

function button(label: string, act: CardButton["act"], p: CardButton["p"] = {}, o: ButtonOptions = {}): CardButton {
  // The reason only exists for a button that is switched off, so the UI can render `why` unconditionally.
  return { label, act, p, primary: !!o.primary, off: !!o.off, why: o.off ? (o.why ?? "") : "" };
}

function blankCard(it: FeedItem): Card {
  return {
    id: it.id,
    k: it.k,
    st: it.st ?? 0,
    by: it.by,
    at: it.at,
    plain: false,
    text: "",
    kicker: "",
    title: "",
    lines: [],
    rows: [],
    table: null,
    chips: [],
    buttons: [],
    inputs: [],
    email: null,
    fields: [],
    steps: [],
    note: "",
    pre: "",
    tag: "",
    tone: "",
    current: false,
    dim: false,
    banner: "",
  };
}

const RESPONSIBILITY_LABEL: Record<Responsibility, Bilingual> = {
  approver: { en: "Approver", it: "Approvatore" },
  owner: { en: "Owner", it: "Responsabile" },
  delegate: { en: "Delegate", it: "Delegato" },
  collab: { en: "Collaborator", it: "Collaboratore" },
};

export function view(c: Case, ctx: CaseContext): CaseView {
  const lang: Lang = ctx.lang;
  const S = pick(lang);
  const T = (x: Bilingual | string | null | undefined): string => tx(x, lang);
  const v = ctx.viewer;
  const C = ctx.contract;
  const f = figures(ctx);
  const ap = canApprove(c, v);
  const wk = canWork(c, v);
  const cc = canClose(c, v);
  const whyWork = T(NO_WORK);
  const clauses = clausesOf(ctx);
  const terms = c.terms;
  const st = stage(c);

  const kindTitle = (e: Email): string => T(EMAIL_KIND[e.kind].title);
  const kindNoun = (e: Email): string => T(EMAIL_KIND[e.kind].noun);

  /* ---------- cards ---------- */
  const cards: Card[] = [];
  c.feed.forEach((it) => {
    if (it.thread && c.thread && it.thread !== c.thread) return;
    const d = blankCard(it);
    const i = it.i ?? 0;

    switch (it.k) {
      case "say":
        d.text = T(it.text);
        break;
      case "user":
        d.text = T(it.text);
        d.tag = it.tag ? T(it.tag) : "";
        break;

      case "q": {
        const q = questionDef(i, ctx);
        const current = c.q === i && c.phase === "interview";
        d.kicker = `${S("Question ", "Domanda ")}${i + 1} / 4`;
        d.title = q.text;
        d.lines = [q.hint];
        if (current) {
          d.chips = q.opts.map(
            (o): CardChip => ({
              label: o[1],
              act: q.multi ? "toggle" : "ans",
              p: q.multi ? { k: o[0] } : { q: i, val: o[0] },
              on: q.multi ? (c.goals.clauses ?? []).includes(o[0]) : false,
            }),
          );
          if (q.multi) {
            d.buttons = [button(S("Continue", "Continua"), "q3done", {}, { primary: true })];
            if (!c.goals.policy) d.buttons.push(button(S("Add company policy (sample)", "Aggiungi policy aziendale (esempio)"), "policy"));
            d.note = S("Or type another clause below.", "Oppure scrivi un’altra clausola qui sotto.");
          } else {
            d.note = S("Or type your own answer below.", "Oppure scrivi la tua risposta qui sotto.");
          }
          d.current = true;
        } else {
          d.dim = true;
          d.lines = [`${S("Answer: ", "Risposta: ")}${answerLabel(c, ctx, i)}`];
        }
        break;
      }

      case "outlook":
        d.kicker = S("Before you start", "Prima di iniziare");
        d.title = S("What you can expect to renegotiate", "Cosa puoi aspettarti di rinegoziare");
        d.lines = [
          S(
            "Read from your contract, clause by clause, against common practice for this kind of agreement. This is general practice, not a market benchmark — the supplier may differ.",
            "Letto dal tuo contratto, clausola per clausola, rispetto alla prassi comune per questo tipo di accordo. È prassi generale, non un benchmark di mercato: il fornitore può discostarsi.",
          ),
        ];
        d.table = outlookTable(ctx);
        d.pin = c.phase === "interview" || c.phase === "plan";
        break;

      case "policy": {
        d.kicker = S("Company policy added", "Policy aziendale aggiunta");
        d.title = "Procurement Policy v3.2";
        d.rows = [
          { k: S("Scope", "Ambito"), v: S("Software contracts above CHF 100k", "Contratti software oltre CHF 100k") },
          ...Object.values(POLICY).flatMap((rule): CardRow[] => (rule ? [{ k: rule.bind ? S("Binding", "Vincolante") : S("Guidance", "Indicazione"), v: T(rule.text) }] : [])),
        ];
        d.note = S(
          "No policy on file would not mean compliance has been checked. Raffa shows policy rules separately from its own proposals.",
          "L’assenza di una policy non equivale a una verifica di conformità. Raffa tiene le regole di policy separate dalle proprie proposte.",
        );
        break;
      }

      case "plan": {
        const ver = it.ver ?? 0;
        const current = ver === c.planVer && !c.planOk && c.phase === "plan";
        const g = c.goals;
        d.kicker = `${S("Plan v", "Piano v")}${ver}`;
        d.title = S(`Renewal plan — ${C.supplier}`, `Piano di rinnovo — ${C.supplier}`);
        d.rows = [
          { k: S("Situation", "Situazione"), v: S(`Ends ${C.end} · notice by ${C.cancel} · ${chf(C.spend)} / yr · uplift ${C.uplift}`, `Scade il ${C.end} · disdetta entro il ${C.cancel} · ${chf(C.spend)} / anno · aumento ${C.uplift}`) },
          { k: S("Products", "Prodotti"), v: answerLabel(c, ctx, 0) },
          { k: S("Term", "Durata"), v: answerLabel(c, ctx, 1) },
          { k: S("Opening request", "Richiesta iniziale"), v: answerLabel(c, ctx, 2) },
        ];
        clauses
          .filter((x) => (g.clauses ?? []).includes(x.k))
          .forEach((x) => {
            const rule = g.policy ? POLICY[x.lk] : undefined;
            const tag = rule ? ` · ${rule.bind ? S("policy (binding)", "policy (vincolante)") : S("policy (guidance)", "policy (indicazione)")}` : "";
            d.rows.push({ k: x.type, v: `${x.ask} · ${x.src}${tag}`, w: 600 });
          });
        d.rows.push({ k: S("Hypotheses", "Ipotesi"), v: S("The supplier may trade price for a longer term — I will ask for your limit when it happens.", "Il fornitore potrebbe scambiare prezzo e durata — ti chiederò il limite quando succede.") });
        d.rows.push({ k: S("Unknown", "Non noto"), v: S("Usage data (not in the contract) · external market prices (not available, not used)", "Dati d’uso (non nel contratto) · prezzi di mercato esterni (non disponibili, non usati)") });
        if (current) {
          d.current = true;
          d.buttons = [button(S("Confirm plan", "Conferma piano"), "confirmPlan", {}, { primary: true, off: !wk, why: whyWork }), button(S("Edit", "Modifica"), "editPlan")];
        } else {
          d.dim = ver !== c.planVer;
        }
        if (ver === c.planVer) {
          if (!c.notice) d.buttons.push(button(S("Prepare non-renewal notice", "Prepara disdetta"), "startNotice"));
          if (!c.alt) d.buttons.push(button(S("Evaluate alternatives", "Valuta alternative"), "startAlt"));
        }
        break;
      }

      case "email": {
        const e = c.emails[it.eid ?? -1];
        if (!e) return;
        const es = e.state;
        const superseded = es === "superseded";
        const current = !superseded && es !== "sent";
        const stateLabel: Record<Email["state"], string> = {
          draft: S("Draft — to approve", "Bozza — da approvare"),
          approved: S("Approved — to send", "Approvata — da inviare"),
          copied: S("Copied — send to be confirmed", "Copiata — invio da confermare"),
          sent: S(`Sent — confirmed by ${e.sent?.by} on ${e.sent?.at}`, `Inviata — confermata da ${e.sent?.by} il ${e.sent?.at}`),
          superseded: S("Superseded — a newer reply arrived", "Superata — è arrivata una nuova risposta"),
        };
        d.kicker = `${kindTitle(e)} · v${e.ver}`;
        d.title = e.subject;
        d.dim = superseded;
        d.current = current;
        const order: Email["state"][] = ["draft", "approved", "copied", "sent"];
        d.steps = [S("Draft", "Bozza"), S("Approved", "Approvata"), S("Copied", "Copiata"), S("Sent (declared)", "Inviata (dichiarata)")].map((label, n) => ({
          label,
          on: !superseded && order.indexOf(es) >= n,
          now: !superseded && order.indexOf(es) === n,
        }));
        d.email = {
          eid: e.id,
          to: e.to,
          toMissing: !e.to && current,
          toKey: `to:${e.id}`,
          state: stateLabel[es],
          body: e.body,
          attach: e.attach,
          editing: c.ui.edit === e.id && current,
          editBody: c.ui.editBody ?? "",
          showBody: !superseded,
          sentNote: e.sent?.edited
            ? S(
                "The text actually sent was edited after approval; it was confirmed afterwards and may differ from the approved version.",
                "Il testo effettivamente inviato è stato modificato dopo l’approvazione; è stato confermato a posteriori e può differire dalla versione approvata.",
              )
            : "",
          attachNote: e.attach.length
            ? S(`Attach in your mailbox: ${e.attach.join(", ")} — the Copy command does not include attachments.`, `Allega nella tua casella: ${e.attach.join(", ")} — il comando Copia non include gli allegati.`)
            : "",
        };
        if (current) {
          if (!e.to && ctx.services.exampleAddress) d.buttons.push(button(S("Use example address", "Usa indirizzo di esempio"), "toEx", { eid: e.id }));
          if (c.ui.edit === e.id) {
            d.buttons.push(button(S("Save changes", "Salva modifiche"), "saveEdit", { eid: e.id }, { primary: true }), button(S("Cancel", "Annulla"), "cancelEdit"));
          } else if (c.ui.sentOpen === e.id) {
            const inputs: CardInput[] = [
              { label: S("Recipient", "Destinatario"), value: e.to, ui: `to:${e.id}`, type: "text" },
              { label: S("Date and time sent", "Data e ora di invio"), value: c.ui.sentDate ?? "", ui: "sentDate", type: "text" },
              { label: S("I edited the text in my mailbox", "Ho modificato il testo nella mia casella"), value: !!c.ui.sentEdited, ui: "sentEdited", type: "check" },
            ];
            if (c.ui.sentEdited) inputs.push({ label: S("Paste the text actually sent", "Incolla il testo effettivamente inviato"), value: c.ui.sentText ?? "", ui: "sentText", type: "area" });
            d.inputs = inputs;
            d.buttons = [button(S("Confirm sent", "Conferma invio"), "sentOk", { eid: e.id }, { primary: true, off: !wk, why: whyWork }), button(S("Cancel", "Annulla"), "sentCancel")];
            d.note = S("Raffa records your declaration of sending; it does not verify delivery or reading.", "Raffa registra la tua dichiarazione di invio; non verifica consegna o lettura.");
          } else {
            if (es === "draft") {
              d.buttons.push(
                button(S("Approve", "Approva"), "approve", { eid: e.id }, {
                  primary: true,
                  off: !ap || !e.to,
                  why: !ap ? T(NO_APPROVE) : S("Add the recipient first — I never invent addresses.", "Aggiungi prima il destinatario — non invento mai indirizzi."),
                }),
              );
            }
            if (es === "approved" || es === "copied") {
              d.buttons.push(
                button(es === "copied" ? S("Copy again", "Copia di nuovo") : S("Copy email", "Copia email"), "copy", { eid: e.id }, { primary: es === "approved" }),
                button(S("I sent it", "Ho inviato"), "sentOpen", { eid: e.id }, { primary: es === "copied" }),
              );
            }
            d.buttons.push(button(S("Edit", "Modifica"), "edit", { eid: e.id }));
            if (es === "approved" || es === "copied") {
              d.note = S(`Approved by ${e.approvedBy}. Any edit invalidates this approval. Copy does not mean sent.`, `Approvata da ${e.approvedBy}. Ogni modifica invalida questa approvazione. Copiare non significa inviare.`);
            }
          }
        }
        break;
      }

      case "rem": {
        const r = c.rems[it.rid ?? -1];
        if (!r) return;
        d.kicker = S("Check date", "Data di controllo");
        if (r.state === "proposed") {
          d.title = S("When should I remind you to check for a reply?", "Quando ti ricordo di controllare la risposta?");
          d.lines = [S(`Proposed: ${r.date}. Your contractual notice date stays ${C.cancel} — the check date never replaces it.`, `Proposta: ${r.date}. La scadenza contrattuale di disdetta resta il ${C.cancel} — la data di controllo non la sostituisce.`)];
          d.inputs = [{ label: S("Check date", "Data di controllo"), value: c.ui.remDate ?? r.date, ui: "remDate", type: "text" }];
          d.buttons = [button(S("Confirm date", "Conferma data"), "remOk", { rid: r.rid }, { primary: true, off: !wk, why: whyWork })];
          d.current = true;
        } else if (r.state === "set") {
          d.title = S(`Check set for ${r.date}`, `Controllo fissato per il ${r.date}`);
          d.lines = [S("Active even with the chat closed. It posts an app notice linked to this case — not a message to the supplier.", "Attivo anche a chat chiusa. Genera un avviso nell’app collegato a questo caso — non un messaggio al fornitore.")];
          d.current = true;
          d.tone = "wait";
        } else if (r.state === "due") {
          d.title = S("Did you receive a reply?", "Hai ricevuto una risposta?");
          d.lines = [S("No reply has been registered in Raffa.", "Nessuna risposta registrata in Raffa.")];
          d.buttons = [button(S("Paste reply", "Incolla risposta"), "openReply", {}, { primary: true }), button(S("Prepare follow-up", "Prepara sollecito"), "followup"), button(S("Change date", "Cambia data"), "remChange")];
          d.current = true;
        } else {
          d.title = S(`Check ${r.date} — closed`, `Controllo ${r.date} — chiuso`);
          d.dim = true;
          d.lines = [S("Cancelled or updated after a new reply or follow-up.", "Annullato o aggiornato dopo una nuova risposta o un sollecito.")];
        }
        break;
      }

      case "reply": {
        const r = c.reps[it.rid ?? -1];
        if (!r) return;
        d.kicker = `${S("Supplier reply pasted · round ", "Risposta del fornitore incollata · giro ")}${r.round}`;
        d.title = S("Original text kept", "Testo originale conservato");
        d.pre = r.text;
        d.note = S(`Pasted by ${r.by} on ${r.at}. The original stays next to Raffa’s interpretation.`, `Incollata da ${r.by} il ${r.at}. L’originale resta accanto all’interpretazione di Raffa.`);
        d.dim = true;
        break;
      }

      case "ana": {
        const r = c.reps[it.rid ?? -1];
        if (!r) return;
        d.kicker = `${S("Analysis · reply ", "Analisi · risposta ")}${r.round}`;
        d.title = S("What changed", "Cosa è cambiato");
        const statusLabel = {
          proposed: S("Proposed", "Proposto"),
          unclear: S("Unclear", "Poco chiaro"),
          rejected: S("Rejected", "Rifiutato"),
          conflict: S("Conflict", "Conflitto"),
        };
        d.table = {
          cols: [S("Term", "Condizione"), S("Supplier says", "Il fornitore dice"), S("Your plan", "Il tuo piano"), S("Status", "Stato")],
          layout: [{ min: 110, fr: 1 }, { min: 160, fr: 1.8 }, { min: 130, fr: 1.2 }, { min: 90, fr: 0.7 }],
          rows: r.rows.map((x): TableCell[] => [
            { v: x.label, w: 600 },
            { v: x.said + (x.warn ? ` — ${x.warn}` : ""), ...(x.warn && { tone: "alert" as const }) },
            { v: x.plan },
            { v: statusLabel[x.state], w: 600, ...(x.state !== "proposed" && { tone: "alert" as const }) },
          ]),
        };
        d.lines = [
          r.round === 1
            ? S("Nothing is agreed yet: a supplier proposal is not an agreement. Payment terms are unclear — I will ask for clarification in the counter-proposal.", "Nulla è concordato: una proposta del fornitore non è un accordo. I pagamenti sono poco chiari — chiederò chiarimenti nella controproposta.")
            : S("These are proposals backed by the supplier’s written reply. They become “agreed” only when you confirm them.", "Sono proposte con evidenza scritta del fornitore. Diventano «concordate» solo quando le confermi."),
        ];
        d.note = S("Prototype: the reading of the reply is scripted for the demo scenario.", "Prototipo: la lettura della risposta è scriptata per lo scenario demo.");
        break;
      }

      case "dec": {
        const q = c.qs[it.did ?? -1];
        if (!q) return;
        d.kicker = S("Decision needed", "Decisione richiesta");
        d.title = S("The discount is tied to 24 months. What is your limit?", "Lo sconto è legato a 24 mesi. Qual è il tuo limite?");
        if (q.state === "open") {
          const price24 = priceLabel(f, ctx.services.counterPrice(ctx, 24));
          d.lines = [
            S(
              `Your plan asked for ${answerLabel(c, ctx, 1)}. A longer commitment means less flexibility at the next renewal; a shorter one means no discount was offered. My only comparison is your current price (${priceLabel(f, f.p0)}) — no external benchmark.`,
              `Il tuo piano chiedeva ${answerLabel(c, ctx, 1)}. Un impegno più lungo riduce la flessibilità al prossimo rinnovo; uno più breve significa nessuno sconto. Il mio unico confronto è il tuo prezzo attuale (${priceLabel(f, f.p0)}) — nessun benchmark esterno.`,
            ),
          ];
          const options: Array<["a" | "b" | "c", string]> = [
            ["a", S(`Accept up to 24 months if unit price ≤ ${price24}`, `Accetto fino a 24 mesi se prezzo unitario ≤ ${price24}`)],
            ["b", S("Stay at 12 months — decline the discount", "Resto a 12 mesi — rifiuto lo sconto")],
            ["c", S("24 months with an exit option at month 12", "24 mesi con uscita al mese 12")],
          ];
          d.chips = options.map(([opt, label]): CardChip => ({ label, act: "decide", p: { did: q.did, opt }, on: false, off: !wk }));
          d.current = true;
          d.note = wk ? "" : S("Owner or delegate decides.", "Decide il responsabile o un delegato.");
        } else {
          d.dim = true;
          d.lines = [`${S("Decision: ", "Decisione: ")}${T(q.label)} · ${q.by}`];
        }
        break;
      }

      case "agree": {
        const q = c.qs[it.did ?? -1];
        if (!q) return;
        const conflict = liabilityConflict(c);
        d.kicker = S("Confirm terms", "Conferma condizioni");
        d.title = S("Mark these as agreed?", "Segnare queste come concordate?");
        const withUnit = (key: string, val: string | null | undefined): string => {
          if (!val) return "—";
          if (key === "term") return `${val}${S(" months", " mesi")}`;
          if (key === "liab") return `${val}${S(" months of fees", " mesi di canone")}`;
          return val;
        };
        d.rows = Object.entries(terms)
          .filter(([, t]) => t.state === "proposed" || t.state === "agreed")
          .map(([key, t]): CardRow => ({
            k: T(t.label),
            v: `${withUnit(key, t.val)} · ${t.state === "agreed" ? S("agreed", "concordato") : S(`proposed in reply ${t.ev}`, `proposto nella risposta ${t.ev}`)}`,
            w: 600,
          }));
        const missing = Object.values(terms)
          .filter((t) => t.state === "open")
          .map((t) => T(t.label));
        if (missing.length) {
          d.rows.push({ k: S("Not covered by this reply", "Non coperto da questa risposta"), v: `${missing.join(", ")}${S(" — still open, not part of this confirmation.", " — ancora aperto, non incluso in questa conferma.")}`, tone: "muted" });
        }
        if (q.state === "agree") {
          d.current = true;
          d.lines = [S("“Agreed” needs the written exchange as evidence and your confirmation. Silence is not acceptance.", "«Concordato» richiede lo scambio scritto come evidenza e la tua conferma. Il silenzio non è accettazione.")];
          if (conflict) {
            d.banner = S(
              "Liability 18 months conflicts with Procurement Policy v3.2 (binding, min. 24). A deviation needs authorization from the competent role — collected outside Raffa in this release.",
              "Responsabilità 18 mesi in conflitto con la Procurement Policy v3.2 (vincolante, min. 24). La deroga richiede l’autorizzazione del ruolo competente — raccolta fuori da Raffa in questo rilascio.",
            );
            d.inputs = [{ label: S("Exception authorized by (name / role)", "Deroga autorizzata da (nome / ruolo)"), value: c.ui.auth ?? "", ui: "auth", type: "text" }];
          }
          d.buttons = [button(S("Confirm as agreed", "Conferma come concordate"), "agreeOk", { did: q.did }, { primary: true, off: !wk, why: whyWork })];
        } else {
          d.dim = true;
          d.lines = [S(`Confirmed as agreed by ${q.by}`, `Confermate come concordate da ${q.by}`)];
        }
        break;
      }

      case "doc": {
        const doc = c.docs[(it.v ?? 0) - 1];
        if (!doc) return;
        const latest = it.v === c.docs.length;
        const diffs = doc.rows.filter((x) => x.res === "diff").length;
        const unread = doc.rows.filter((x) => x.res === "unread").length;
        const matching = doc.rows.filter((x) => x.res === "ok" || x.res === "manual").length;
        d.kicker = `${S("Contract draft v", "Bozza di contratto v")}${it.v}`;
        d.title = doc.file;
        d.banner = S("Draft — not an active contract. It does not count as spend and does not trigger a new renewal.", "Bozza — non è un contratto attivo. Non conta come spesa e non genera un nuovo rinnovo.");
        const resultLabel = { ok: S("Matches", "Coincide"), diff: S("Differs", "Differisce"), unread: S("Not read", "Non letto"), manual: S("Manual", "Manuale") };
        d.table = {
          cols: [S("Point", "Punto"), S("Agreed", "Concordato"), S("In the draft", "Nella bozza"), S("Result", "Esito"), S("Source", "Fonte")],
          layout: [{ min: 110, fr: 1 }, { min: 130, fr: 1.3 }, { min: 150, fr: 1.6 }, { min: 80, fr: 0.6 }, { min: 70, fr: 0.5 }],
          rows: doc.rows.map((x): TableCell[] => {
            const bad = x.res === "diff" || x.res === "unread";
            return [
              { v: x.label, w: 600 },
              { v: x.agreed },
              { v: x.draft, ...(bad && { tone: "alert" as const }) },
              { v: resultLabel[x.res], w: 600, ...(bad && { tone: "alert" as const }) },
              { v: x.src, tone: "muted" },
            ];
          }),
        };
        d.lines = [
          S(
            `${diffs} difference${diffs === 1 ? "" : "s"} · ${unread} point${unread === 1 ? "" : "s"} not read · ${matching} match`,
            `Differenze: ${diffs} · punti non letti: ${unread} · coincidono: ${matching}`,
          ),
        ];
        if (latest && c.phase === "doc_check") {
          d.current = true;
          if (diffs) d.buttons.push(button(S("Prepare correction email", "Prepara email correttiva"), "fixEmail", {}, { primary: true }));
          if (unread) d.buttons.push(button(S("Mark annex verified manually", "Segna allegato verificato a mano"), "markAnnex", { v: it.v }, { off: !wk, why: whyWork }));
          d.note = S("A revised draft restarts the check. When a new version arrives, add it with “Add contract”.", "Una bozza rivista riavvia la verifica. Quando arriva una nuova versione, aggiungila con «Aggiungi contratto».");
        } else if (!latest) {
          d.dim = true;
        } else {
          d.lines.push(S("Every point was read and matches the agreed position.", "Ogni punto è stato letto e coincide con la posizione concordata."));
        }
        break;
      }

      case "signwait":
        d.kicker = S("Signature", "Firma");
        d.title = S("Ready to sign — outside Raffa", "Pronta alla firma — fuori da Raffa");
        d.lines = [S("Signing is not done in Raffa and I do not certify signatures. When it is signed, add the signed contract.", "La firma non avviene in Raffa e non certifico le firme. Quando è firmato, aggiungi il contratto firmato.")];
        if (c.phase === "sign_wait" || c.phase === "sign_declared") {
          d.current = true;
          d.buttons = [button(S("Add signed contract (sample)", "Aggiungi contratto firmato (esempio)"), "addDoc", {}, { primary: true })];
          if (c.phase === "sign_wait") d.buttons.push(button(S("Signature declared — file missing", "Firma dichiarata — file mancante"), "signDeclared"));
          else d.banner = S("Signature declared — document to acquire. Task open.", "Firma dichiarata — documento da acquisire. Attività aperta.");
        } else {
          d.dim = true;
        }
        break;

      case "sig": {
        if (!c.signed) return;
        d.kicker = S("Signed document", "Documento firmato");
        d.title = c.signed.file;
        d.lines = [S(`Compared with the last verified draft (v${c.docs.length}): same content on all 8 points. No differences to document.`, `Confrontato con l’ultima bozza verificata (v${c.docs.length}): stesso contenuto su tutti gli 8 punti. Nessuna differenza da documentare.`)];
        if (c.phase === "sign_confirm") {
          d.current = true;
          d.lines.push(S("Confirm that this is the final signed version required by your process. A file name containing “signed” is not enough.", "Conferma che questa è la versione firmata definitiva richiesta dal tuo processo. Un nome file con «signed» non basta."));
          d.buttons = [button(S("Confirm as final signed document", "Conferma come documento firmato definitivo"), "sigOk", {}, { primary: true, off: !cc, why: T(NO_CLOSE) })];
          if (!cc) d.note = S(`Waiting for ${c.owner} or a delegate with closing rights.`, `In attesa di ${c.owner} o di un delegato con diritto di chiusura.`);
        } else {
          d.dim = true;
          d.lines.push(S(`Confirmed by ${c.signed.confirmedBy}.`, `Confermato da ${c.signed.confirmedBy}.`));
        }
        break;
      }

      case "pipe": {
        const pipe = c.pipe;
        if (!pipe) return;
        const stages = [S("Upload", "Caricamento"), S("Processing", "Elaborazione"), S("Extraction", "Estrazione"), S("Review", "Revisione"), S("Validation", "Validazione")];
        d.kicker = S("Negotiation completed · new contract in review", "Negoziazione completata · nuovo contratto in revisione");
        d.title = S("Standard pipeline — same one, inside the chat", "Pipeline standard — la stessa, dentro la chat");
        d.steps = stages.map((label, n) => ({ label, on: pipe.stage >= n, now: pipe.stage === n }));
        if (pipe.stage >= 3) {
          d.lines = [
            S(
              `${pipe.facts.total} facts extracted · ${pipe.facts.aboveThreshold} above threshold · ${pipe.weak.length} below 80% for you to decide. An extraction error never reopens the signed negotiation.`,
              `${pipe.facts.total} fatti estratti · ${pipe.facts.aboveThreshold} sopra soglia · ${pipe.weak.length} sotto l’80% da decidere. Un errore di estrazione non riapre la negoziazione firmata.`,
            ),
          ];
          d.fields = pipe.weak.map((w, n) => ({
            name: T(w.name),
            val: w.val,
            conf: `${w.conf}%`,
            state: w.state,
            open: w.state === "open",
            doneLabel: w.state === "accepted" ? S("Accepted", "Accettato") : w.state === "corrected" ? S("Corrected", "Corretto") : "",
            inKey: `cf${n}`,
            inVal: String(c.ui[`cf${n}`] ?? ""),
            i: n,
          }));
          if (pipe.stage === 3) {
            d.current = true;
            d.buttons = [
              button(S("Mark as validated", "Segna come validato"), "validate", {}, {
                primary: true,
                off: pipe.weak.some((x) => x.state === "open"),
                why: S("Decide both fields first", "Decidi prima i due campi"),
              }),
            ];
          }
        } else {
          d.lines = [S("Processing the signed contract…", "Elaborazione del contratto firmato…")];
        }
        break;
      }

      case "out": {
        const o = c.outcome;
        if (o?.kind !== "renewed") return;
        d.kicker = S("Outcome", "Esito");
        d.title = S("Renewed — new contract scheduled", "Rinnovato — nuovo contratto programmato");
        d.rows = [
          { k: S("Starts", "Decorrenza"), v: o.start },
          { k: S("Term ends", "Fine termine"), v: o.end },
          { k: S("Next notice deadline", "Prossima scadenza disdetta"), v: o.cancel },
          { k: S("New price", "Nuovo prezzo"), v: `${o.price}${S(" (was ", " (era ")}${o.was})` },
          { k: S("Annual change vs previous (same quantities)", "Variazione annua vs precedente (stesse quantità)"), v: `${o.delta > 0 ? "−" : "+"}${chf(Math.abs(o.delta))}`, w: 600 },
          { k: S("Previous contract", "Contratto precedente"), v: S(`stays active until ${C.end}`, `resta attivo fino al ${C.end}`) },
        ];
        d.lines = [
          S(
            "Signature, validation and start date are separate events. The change is computed from the two contracts’ prices — not a market saving. The next cycle is planned from the validated dates.",
            "Firma, validazione e decorrenza sono eventi distinti. La variazione è calcolata dai prezzi dei due contratti — non è un risparmio di mercato. Il prossimo ciclo è pianificato dalle date validate.",
          ),
        ];
        d.buttons = [button(S("Open Contract 360", "Apri Contract 360"), "nav", { to: "c360" }), button(S("See in Renewals", "Vedi in Rinnovi"), "nav", { to: "renewals" })];
        break;
      }

      case "ncheck": {
        const days = daysTo(ctx, C.cancel);
        const clause = ctx.detail.push.find((x) => /renew/i.test(x.type)) ?? ctx.detail.push[0];
        d.kicker = S("Non-renewal notice", "Disdetta");
        d.title = S("What the contract requires", "Cosa richiede il contratto");
        d.rows = [
          { k: S("Clause", "Clausola"), v: clause?.src ?? "—" },
          { k: S("Deadline", "Scadenza"), v: `${C.cancel} · ${days}${S(" days left", " giorni")}`, w: 600 },
          { k: S("Effective end", "Cessazione effettiva"), v: C.end },
          { k: S("Form", "Forma"), v: S("Written notice. The contract lists no email address for notices — recipient to be confirmed.", "Comunicazione scritta. Il contratto non indica un indirizzo email per le disdette — destinatario da confermare.") },
          { k: S("Tasks", "Attività"), v: S("1 · Email notice (draft below) · 2 · Registered letter if required — confirm with Legal", "1 · Email di disdetta (bozza sotto) · 2 · Raccomandata se richiesta — conferma con Legal") },
        ];
        if (days < 0) d.banner = S("The window appears expired or contradictory. I cannot promise an effective notice — verify with Legal.", "La finestra sembra scaduta o contraddittoria. Non posso promettere una disdetta efficace — verifica con Legal.");
        if (c.notice?.state === "check") {
          d.current = true;
          d.buttons = [button(S("Prepare the notice email", "Prepara l’email di disdetta"), "nStart", {}, { primary: true })];
        } else {
          d.dim = true;
        }
        break;
      }

      case "nrec": {
        const notice = c.notice;
        if (!notice) return;
        d.kicker = S("Notice communicated", "Disdetta comunicata");
        d.title = S("Status: notice communicated — outcome to verify", "Stato: disdetta comunicata — esito da verificare");
        d.lines = [S("Sending the email is not the same as the contract ending. Attach the supplier’s confirmation, then record the outcome.", "Inviare l’email non equivale alla fine del contratto. Allega la conferma del fornitore, poi registra l’esito.")];
        if (notice.state === "sent") {
          d.current = true;
          if (notice.evidence) {
            d.lines.push(S(`Evidence attached: ${notice.evFile}`, `Prova allegata: ${notice.evFile}`));
          } else {
            d.buttons.push(button(S("Add receipt (sample)", "Aggiungi ricevuta (esempio)"), "nReceipt"));
            d.inputs = [{ label: S("Proceed without evidence", "Procedi senza prova"), value: !!c.ui.noEv, ui: "noEv", type: "check" }];
          }
          d.buttons.push(button(S("Record outcome: not renewed", "Registra esito: non rinnovato"), "nOut", {}, { primary: true, off: !cc, why: T(NO_CLOSE) }));
        } else {
          d.dim = true;
        }
        break;
      }

      case "nout": {
        const exitTasks = ctx.detail.obligationsSupplier.filter((x) => /return|data|offboard/i.test(x.text)).map((x) => `${x.text} (${x.when})`);
        d.kicker = S("Outcome", "Esito");
        d.title = S("Not renewed — notice", "Non rinnovato — disdetta");
        d.rows = [
          { k: S("Effective end", "Cessazione effettiva"), v: C.end },
          { k: S("Successor", "Successore"), v: c.alt?.chosen ?? S("none — nothing activated", "nessuno — nulla attivato") },
          { k: S("Exit tasks still open", "Attività di uscita aperte"), v: exitTasks.join(" · ") || S("Return of data per the agreement", "Restituzione dei dati come da contratto") },
        ];
        d.buttons = [button(S("Open Contract 360", "Apri Contract 360"), "nav", { to: "c360" })];
        break;
      }

      case "areq": {
        const alt = c.alt;
        if (!alt) return;
        d.kicker = S("Alternatives", "Alternative");
        d.title = S("Essential requirements", "Requisiti essenziali");
        d.lines = [
          S(
            `From your contract and plan. Needed from ${addDays(C.end, 1)}. I compare only candidates you name and offers you upload — I do not search the market.`,
            `Dal tuo contratto e dal piano. Servono dal ${addDays(C.end, 1)}. Confronto solo i candidati che indichi e le offerte che carichi — non cerco sul mercato.`,
          ),
        ];
        if (alt.state === "req") {
          d.current = true;
          d.chips = alt.reqs.map((label): CardChip => ({ label, act: "ui", p: { k: "noop", v: 1 }, on: true }));
          d.buttons = [button(S("Confirm requirements", "Conferma requisiti"), "altReqs", {}, { primary: true })];
        } else {
          d.dim = true;
          d.rows = alt.reqs.map((r): CardRow => ({ k: "•", v: r }));
        }
        break;
      }

      case "acand": {
        const alt = c.alt;
        if (!alt) return;
        d.kicker = S("Alternatives", "Alternative");
        d.title = S("Which vendors should I ask?", "A quali fornitori chiedo?");
        d.lines = [alt.vendors.length ? `${S("Candidates: ", "Candidati: ")}${alt.vendors.join(", ")}` : S("No candidate yet.", "Nessun candidato ancora.")];
        if (alt.state === "cand") {
          d.current = true;
          d.inputs = [{ label: S("Vendor name", "Nome fornitore"), value: c.ui.altName ?? "", ui: "altName", type: "text" }];
          d.buttons = [
            button(S("Add vendor", "Aggiungi fornitore"), "altAdd"),
            ...(ctx.services.exampleVendors().length ? [button(S("Add two examples", "Aggiungi due esempi"), "altEx")] : []),
            button(S("Prepare RFQ emails", "Prepara email RFQ"), "altRfq", {}, { primary: true, off: !alt.vendors.length, why: S("Add at least one vendor", "Aggiungi almeno un fornitore") }),
          ];
        } else {
          d.dim = true;
        }
        break;
      }

      case "aoff": {
        const offer = c.alt?.offers[it.vendor ?? ""];
        if (!offer || offer === true) return;
        d.kicker = `${S("Offer · ", "Offerta · ")}${it.vendor}`;
        d.title = S("Declared by the vendor — specific evidence, not a market benchmark", "Dichiarata dal fornitore — evidenza specifica, non un benchmark di mercato");
        d.rows = [
          { k: S("Price", "Prezzo"), v: f.p0 < 1000 ? `CHF ${offer.price} per ${f.unit}` : `${chf(offer.total)} / yr` },
          { k: S("Term", "Durata"), v: `${offer.term}${S(" months", " mesi")}` },
          { k: S("Requirements covered", "Requisiti coperti"), v: offer.cover },
          { k: S("Migration", "Migrazione"), v: offer.mig },
          { k: S("Missing data", "Dati mancanti"), v: offer.gap, tone: "alert" },
        ];
        break;
      }

      case "acmp": {
        const alt = c.alt;
        if (!alt) return;
        const names = Object.keys(alt.offers).filter((k) => !k.endsWith("_rfq"));
        d.kicker = S("Comparison", "Confronto");
        d.title = S("Offers vs your current contract", "Offerte vs contratto attuale");
        const priceOf = (price: number, total: number): string => (f.p0 < 1000 ? `CHF ${price} per ${f.unit}` : `${chf(total)} / yr`);
        d.table = {
          cols: [S("Option", "Opzione"), S("Price", "Prezzo"), S("Term", "Durata"), S("Coverage", "Copertura"), S("Migration", "Migrazione"), S("Missing", "Mancante")],
          layout: [{ min: 100, fr: 1 }, { min: 130, fr: 1.2 }, { min: 70, fr: 0.6 }, { min: 70, fr: 0.6 }, { min: 80, fr: 0.7 }, { min: 130, fr: 1.2 }],
          rows: [
            [
              { v: S(`Current · ${C.supplier}`, `Attuale · ${C.supplier}`), w: 600 },
              { v: priceOf(f.p0, f.p0) },
              { v: S(`to ${C.end}`, `fino al ${C.end}`) },
              { v: "—" },
              { v: "—" },
              { v: S(`uplift ${C.uplift}`, `aumento ${C.uplift}`) },
            ],
            ...names.flatMap((n): TableCell[][] => {
              const o = alt.offers[n];
              if (!o || o === true) return [];
              return [[{ v: n, w: 600 }, { v: priceOf(o.price, o.total) }, { v: `${o.term}${S(" mo", " mesi")}` }, { v: o.cover }, { v: o.mig }, { v: o.gap, tone: "alert" }]];
            }),
          ],
        };
        d.lines = [S("Costs are what each vendor declared, for the lines they quoted. Transition effort and continuity matter: where data is missing I say so instead of estimating.", "I costi sono quelli dichiarati da ciascun fornitore per le righe quotate. Contano migrazione e continuità: dove mancano dati lo dico invece di stimare.")];
        if (alt.state === "compare") {
          d.current = true;
          d.buttons = names.map((n) => button(S(`Select ${n}`, `Scegli ${n}`), "altSel", { vendor: n }, { primary: true, off: !cc, why: T(NO_CLOSE) }));
          d.note = S("Add the other vendor’s offer from its thread to compare more.", "Aggiungi l’offerta dell’altro fornitore dal suo filone per confrontare di più.");
        } else {
          d.dim = true;
        }
        break;
      }

      case "asel": {
        const alt = c.alt;
        if (!alt) return;
        d.kicker = S("Replacement", "Sostituzione");
        d.title = S(`${alt.chosen} selected — the current contract is NOT closed`, `Scelto ${alt.chosen} — il contratto attuale NON è chiuso`);
        d.rows = [
          { k: S("Exit track", "Ramo uscita"), v: alt.exitDone ? S("Done — not renewed", "Fatto — non rinnovato") : S(`Open — send notice to ${C.supplier}`, `Aperto — disdetta a ${C.supplier}`), w: 600, ...(!alt.exitDone && { tone: "alert" as const }) },
          { k: S("New agreement", "Nuovo accordo"), v: alt.newDone ? S("Signed contract added", "Contratto firmato aggiunto") : S("Open — add the signed contract", "Aperto — aggiungi il contratto firmato"), w: 600, ...(!alt.newDone && { tone: "alert" as const }) },
        ];
        d.lines = [
          S(
            "The case closes as “replacement” only when the previous contract’s obligations are resolved or assigned, and the new contract is acquired. No implicit termination.",
            "Il caso si chiude come «sostituzione» solo quando gli adempimenti del contratto precedente sono risolti o assegnati e il nuovo contratto è acquisito. Nessuna cessazione implicita.",
          ),
        ];
        if (!alt.exitDone || !alt.newDone) {
          d.current = true;
          if (!alt.exitDone && !c.notice) d.buttons.push(button(S("Prepare non-renewal notice", "Prepara disdetta"), "startNotice", {}, { primary: true }));
          if (!alt.newDone) d.buttons.push(button(S("Add new vendor contract (sample)", "Aggiungi contratto nuovo fornitore (esempio)"), "altNew", { vendor: alt.chosen ?? "" }));
        }
        break;
      }

      case "asel2":
        d.kicker = S("New vendor", "Nuovo fornitore");
        d.title = S("New contract received — enters the standard pipeline once", "Nuovo contratto ricevuto — entra una sola volta nella pipeline standard");
        d.lines = [S(`Extraction and review of ${c.alt?.newFile} run like any other contract. It stays linked to this case and to the replaced contract.`, `Estrazione e revisione di ${c.alt?.newFile} seguono il flusso di ogni altro contratto. Resta collegato a questo caso e al contratto sostituito.`)];
        d.dim = true;
        break;
    }
    d.plain = it.k === "say" || it.k === "user";
    cards.push(d);
  });

  /* ---------- header ---------- */
  const rem = c.rems.find((x) => x.state === "set" || x.state === "due");
  const currentEmail = [...c.emails].reverse().find((e) => e.state !== "sent" && e.state !== "superseded" && e.kind !== "notice" && e.kind !== "rfq");
  const approverOrDelegate = Object.keys(c.delegates)[0] ?? c.approver;
  const phase = PHASES[c.phase] ?? PHASES.email;
  let nextText: Bilingual = phase.next;
  let nextWho = phase.who;
  if (c.phase === "email" && currentEmail) {
    if (currentEmail.state === "draft" && !currentEmail.to) {
      nextText = { en: "Add the recipient", it: "Aggiungi il destinatario" };
      nextWho = "owner";
    } else if (currentEmail.state === "draft") {
      nextText = { en: "Approve the email", it: "Approva l’email" };
      nextWho = "approver";
    } else {
      nextText = { en: "Copy, send and confirm", it: "Copia, invia e conferma" };
      nextWho = "owner";
    }
  }
  const nextActor =
    nextWho === "approver" ? approverOrDelegate : nextWho === "supplier" ? S("Supplier", "Fornitore") : nextWho === "you" ? S("You", "Tu") : nextWho === "owner" ? c.owner : "—";
  const nextLine = `${T(nextText)}${rem && c.phase === "waiting" ? ` · ${S("check ", "controllo ")}${rem.date}` : ""}`;

  let status = T(phase.status);
  if (c.outcome?.kind === "terminated") {
    status = S(`Not renewed${c.alt?.chosen ? ` · replaced by ${c.alt.chosen}` : ""}`, `Non rinnovato${c.alt?.chosen ? ` · sostituito da ${c.alt.chosen}` : ""}`);
  }

  const tracks: string[] = [];
  if (c.notice) {
    const label = { check: S("to prepare", "da preparare"), email: S("email in progress", "email in corso"), sent: S("communicated — outcome to verify", "comunicata — esito da verificare"), out: S("not renewed", "non rinnovato") }[c.notice.state];
    tracks.push(`${S("Notice: ", "Disdetta: ")}${label}`);
  }
  if (c.alt) {
    const label = {
      req: S("requirements", "requisiti"),
      cand: S("candidates", "candidati"),
      rfq: S("RFQ sent", "RFQ"),
      compare: S("comparing", "confronto"),
      selected: S(`selected ${c.alt.chosen}`, `scelto ${c.alt.chosen}`),
    }[c.alt.state];
    tracks.push(`${S("Alternatives: ", "Alternative: ")}${label}`);
  }

  const renewed = c.outcome?.kind === "renewed" ? c.outcome : null;
  const people: CasePerson[] = c.members.map((name) => {
    const responsibility = responsibilityOf(c, name);
    const closes = !!c.delegates[name]?.close;
    const role =
      responsibility === "approver"
        ? S("Approver · CPO", "Approvatore · CPO")
        : responsibility === "owner"
          ? S("Owner · works the case", "Responsabile · lavora il caso")
          : responsibility === "delegate"
            ? closes
              ? S("Delegate · can close", "Delegato · può chiudere")
              : S("Delegate", "Delegato")
            : S("Collaborator", "Collaboratore");
    return {
      name,
      role,
      responsibility,
      isOwner: responsibility === "owner" || responsibility === "approver",
      isDelegate: responsibility === "delegate",
      canClose: responsibility === "delegate" && closes,
      viewing: name === v,
    };
  });
  const responsibility = responsibilityOf(c, v);

  const hdr: CaseHeader = {
    title: `${S("Renewal", "Rinnovo")} · ${C.supplier} · ${C.name.split(" — ")[0]} ${C.start.slice(-4)}–${C.end.slice(-4)}`,
    supplier: C.supplier,
    contract: C.name,
    status,
    next: `${nextActor === "—" ? "" : `${nextActor} — `}${nextLine}`,
    approver: c.approver,
    noticeBy: renewed ? renewed.cancel : C.cancel,
    noticeDays: renewed ? renewed.cancelDays : daysTo(ctx, C.cancel),
    end: renewed ? `${renewed.end}${S(` (new contract; previous ends ${C.end})`, ` (nuovo contratto; il precedente termina il ${C.end})`)}` : C.end,
    owner: c.owner,
    tracks,
    warn: c.ui.warn ?? "",
    people,
    youRole: T(RESPONSIBILITY_LABEL[responsibility]),
  };

  /* ---------- summary ---------- */
  const termStateLabel = { open: S("Open", "Aperto"), proposed: S("Proposed", "Proposto"), agreed: S("Agreed", "Concordato"), rejected: S("Rejected", "Rifiutato"), unclear: S("Unclear", "Poco chiaro") };
  const termsView: TermView[] = Object.values(terms).map((t) => ({ label: T(t.label), val: t.val || "—", state: termStateLabel[t.state], status: t.state }));
  const summary: CaseView["summary"] = {
    goals: [
      { k: S("Products", "Prodotti"), v: c.goals.prod ? answerLabel(c, ctx, 0) : "—" },
      { k: S("Term", "Durata"), v: c.goals.term ? answerLabel(c, ctx, 1) : "—" },
      { k: S("Target", "Obiettivo"), v: c.goals.target !== undefined ? answerLabel(c, ctx, 2) : "—" },
      { k: S("Clauses", "Clausole"), v: c.goals.clauses ? answerLabel(c, ctx, 3) : "—" },
      { k: S("Plan", "Piano"), v: c.planVer ? `v${c.planVer}${c.planOk ? S(" · confirmed", " · confermato") : S(" · to confirm", " · da confermare")}` : "—" },
    ],
    terms: termsView,
    decisions: [...c.decs].reverse().map((x) => ({ by: x.by, at: x.at, text: T(x.text) })),
    refs: S(
      `${c.emails.length} emails · ${c.reps.length} replies · ${c.docs.length} drafts${c.signed ? " · 1 signed" : ""}`,
      `${c.emails.length} email · ${c.reps.length} risposte · ${c.docs.length} bozze${c.signed ? " · 1 firmato" : ""}`,
    ),
  };

  /* ---------- actions, threads, demo shortcuts ---------- */
  const closed = !!c.outcome || c.phase === "pipeline";
  const actions: CaseView["actions"] = [];
  if ((!closed && c.emails.some((e) => e.state === "sent")) || c.round > 0) actions.push({ label: S("Paste reply", "Incolla risposta"), act: "openReply" });
  if (!closed && c.agreed) actions.push({ label: S("Add contract", "Aggiungi contratto"), act: "addDoc" });
  if (!c.planOk && !c.goals.policy && c.phase !== "interview") actions.push({ label: S("Add policy", "Aggiungi policy"), act: "policy" });

  const threads: CaseView["threads"] =
    c.alt && c.alt.vendors.length && ["rfq", "compare", "selected"].includes(c.alt.state)
      ? c.alt.vendors.map((t) => ({ t, on: c.thread === t, offer: !c.alt?.offers[t] && c.alt?.state !== "selected", hasOffer: !!c.alt?.offers[t] }))
      : [];
  const demo: CaseView["demo"] = !closed && c.rems.some((r) => r.state === "set") ? [{ label: S("Jump to the check date", "Salta alla data di controllo"), act: "ff" }] : [];

  /* ---------- "since your last visit" ---------- */
  const sinceLabel: Partial<Record<FeedItem["k"], string>> = {
    email: S("prepared an email", "ha preparato un’email"),
    user: S("wrote in the chat", "ha scritto nella chat"),
    reply: S("pasted a supplier reply", "ha incollato una risposta del fornitore"),
    ana: S("analysed the reply", "ha analizzato la risposta"),
    dec: S("asked for a decision", "ha chiesto una decisione"),
    agree: S("proposed terms to confirm", "ha proposto condizioni da confermare"),
    doc: S("checked a draft", "ha verificato una bozza"),
    sig: S("received the signed file", "ha ricevuto il file firmato"),
    plan: S("prepared the plan", "ha preparato il piano"),
    rem: S("proposed a check date", "ha proposto una data di controllo"),
    say: S("posted an update", "ha pubblicato un aggiornamento"),
    policy: S("added a policy", "ha aggiunto una policy"),
  };
  let since: CaseView["since"] = null;
  if (c.ui.since) {
    const byUser: Record<string, string[]> = {};
    c.ui.since.items.forEach((x) => {
      const label = sinceLabel[x.k] ?? S("updated the case", "ha aggiornato il caso");
      const list = (byUser[x.by] ??= []);
      if (!list.includes(label)) list.push(label);
    });
    since = { n: c.ui.since.n, lines: Object.entries(byUser).map(([who, labels]) => `${who}: ${labels.join(", ")}`) };
  }

  /* ---------- timeline ---------- */
  const kindLabel: Partial<Record<FeedItem["k"], string>> = {
    email: S("Email", "Email"),
    reply: S("Supplier reply", "Risposta del fornitore"),
    ana: S("Analysis", "Analisi"),
    dec: S("Decision asked", "Decisione richiesta"),
    agree: S("Terms to confirm", "Condizioni da confermare"),
    doc: S("Draft checked", "Bozza verificata"),
    sig: S("Signed file", "File firmato"),
    plan: S("Plan", "Piano"),
    rem: S("Check date", "Data di controllo"),
    out: S("Outcome", "Esito"),
    nrec: S("Notice", "Disdetta"),
    nout: S("Outcome", "Esito"),
    pipe: S("Acquisition", "Acquisizione"),
    policy: S("Policy", "Policy"),
    ncheck: S("Notice check", "Verifica disdetta"),
    areq: S("Alternatives", "Alternative"),
    acand: S("Alternatives", "Alternative"),
    aoff: S("Offer", "Offerta"),
    acmp: S("Comparison", "Confronto"),
    asel: S("Replacement", "Sostituzione"),
    signwait: S("Signature", "Firma"),
  };
  const emailStateLabel: Record<Email["state"], string> = {
    draft: S("draft", "bozza"),
    approved: S("approved", "approvata"),
    copied: S("copied", "copiata"),
    sent: S("sent", "inviata"),
    superseded: S("superseded", "superata"),
  };
  const timeline: TimelineEntry[] = [];
  c.feed.forEach((it) => {
    if (it.k === "q" || it.k === "outlook") return;
    let label: string;
    let text: string;
    if (it.k === "say") {
      label = "Raffa.ai";
      text = T(it.text).split(/(?<=[.!?])\s/)[0] ?? "";
    } else if (it.k === "user") {
      const own = T(it.text);
      label = it.by;
      text = own.length > 90 ? `${own.slice(0, 88)}…` : own;
    } else if (it.k === "email") {
      const e = c.emails[it.eid ?? -1];
      label = kindLabel.email ?? "Email";
      text = e ? `${kindNoun(e)} v${e.ver} — ${emailStateLabel[e.state]}` : "";
    } else {
      label = kindLabel[it.k] ?? it.k;
      text = sinceLabel[it.k] ?? "";
    }
    timeline.push({ at: it.at.slice(0, 6), time: it.at.slice(-5), by: it.by, label, text, mine: it.by === v });
  });
  timeline.reverse();

  const upcoming: CaseView["upcoming"] = c.outcome
    ? []
    : [
        { date: addDays(C.cancel, -30), label: S("Go / no-go call: renew, renegotiate or exit", "Decisione: rinnovare, rinegoziare o uscire") },
        { date: C.cancel, label: S("Notice deadline", "Scadenza di disdetta") },
        { date: C.end, label: S("Term ends", "Fine termine") },
      ]
        .map((x) => ({ ...x, days: daysTo(ctx, x.date) }))
        .filter((x) => x.days >= 0)
        .map((x) => ({ date: x.date, label: x.label, when: `${x.days}${S(" days", " giorni")}` }));

  const emailsList: CaseView["emailsList"] = c.emails.map((e) => ({
    label: `${kindNoun(e)} · v${e.ver}`,
    state: { draft: S("Draft", "Bozza"), approved: S("Approved", "Approvata"), copied: S("Copied", "Copiata"), sent: S("Sent", "Inviata"), superseded: S("Superseded", "Superata") }[e.state],
    pending: e.state === "draft" || e.state === "approved" || e.state === "copied",
  }));
  const docsList: CaseView["docsList"] = [
    ...c.docs.map((d) => {
      const open = d.rows.filter((x) => x.res === "diff" || x.res === "unread").length;
      return { label: d.file, state: open ? `${open}${S(" open points", " punti aperti")}` : S("matches", "coincide"), open: open > 0, final: false };
    }),
    ...(c.signed ? [{ label: c.signed.file, state: c.signed.confirmedBy ? S("final · confirmed", "definitivo · confermato") : S("to confirm", "da confermare"), open: false, final: true }] : []),
  ];

  return {
    outlook: outlookTable(ctx),
    timeline,
    upcoming,
    stage: st,
    steps7: STEPS.map((x, n) => ({ label: T(x), n: n + 1, done: n < st, now: n === st })),
    check: checklist(c, ctx),
    example: T(EXAMPLES[st]),
    since,
    emailsList,
    docsList,
    hdr,
    summary,
    cards,
    actions,
    threads,
    demo,
    reply: { open: !!c.ui.replyOpen, text: c.ui.replyText ?? "", from: [...c.emails].reverse().find((e) => e.to)?.to ?? "—" },
    shortStatus: shortStatus(c, lang),
    permissions: permissionsOf(c, v),
    responsibility,
  };
}

function daysTo(ctx: CaseContext, date: string): number {
  return daysBetween(ctx.today, date);
}
