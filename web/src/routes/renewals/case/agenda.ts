/**
 * What the case needs next: `checklist` (the guide for the current wizard step) and `todo` (the
 * actionable items behind Today and the primary button of each step). Both are derived from the case
 * and its context alone.
 */

import { daysBetween } from "./dates";
import { EMAIL_KIND, liabilityConflict, STEPS, stage } from "./model";
import { B, pick, tx, type Bilingual } from "./text";
import type { Case, CaseContext, ChecklistItem, EmailKind, TodoItem, TodoKind } from "./types";

export function checklist(c: Case, ctx: CaseContext): ChecklistItem[] {
  const S = pick(ctx.lang);
  const st = stage(c);
  const out: ChecklistItem[] = [];
  const item = (done: unknown, en: string, it: string, who = ""): void => {
    out.push({ done: !!done, label: S(en, it), who });
  };

  if (st === 0) {
    item(c.owner, "Owner assigned", "Responsabile assegnato", c.approver);
    item(c.goals.prod, "Products and quantities", "Prodotti e quantità", c.owner);
    item(c.goals.term, "Term", "Durata", c.owner);
    item(c.goals.target !== undefined, "Economic goal (or “no target”)", "Obiettivo economico (o «nessun target»)", c.owner);
    item(c.planVer > 0, "Clauses to change", "Clausole da cambiare", c.owner);
  }
  if (st === 1) {
    item(c.planVer > 0, "Plan drafted by Raffa", "Piano preparato da Raffa");
    item(c.planOk, "Plan confirmed", "Piano confermato", c.owner);
  }
  if (st === 2) {
    const cur = [...c.emails].reverse().find((e) => e.state !== "superseded" && e.kind !== "notice" && e.kind !== "rfq");
    if (cur?.state === "draft" && !cur.to) item(false, "Recipient added", "Destinatario aggiunto", c.owner);
    item(cur && cur.state !== "draft", `Email approved by ${c.approver}`, `Email approvata da ${c.approver}`, c.approver);
    item(cur?.state === "sent", "Send confirmed", "Invio confermato", c.owner);
    item(c.rems.some((x) => x.state !== "proposed") && !c.rems.some((x) => x.state === "proposed"), "Check date set", "Data di controllo impostata", c.owner);
    item(c.reps.length >= (cur?.state === "sent" ? c.round + 1 : c.round), "Supplier reply registered", "Risposta del fornitore registrata", c.owner);
    if (c.qs.some((q) => q.state === "open" || q.opt)) {
      item(c.qs.every((q) => q.state !== "open"), "Trade-off decided", "Compromesso deciso", c.owner);
    }
  }
  if (st === 3) {
    if (liabilityConflict(c)) item((c.ui.auth ?? "").trim(), "Policy exception authorized", "Deroga alla policy autorizzata", c.approver);
    item(c.agreed, "Terms confirmed as agreed", "Condizioni confermate come concordate", c.owner);
  }
  if (st === 4) {
    const request = c.emails.find((e) => e.kind === "docreq");
    const last = c.docs[c.docs.length - 1];
    item(request?.state === "sent", "Contract requested from the supplier", "Contratto richiesto al fornitore", c.owner);
    item(c.docs.length > 0, "Draft received", "Bozza ricevuta", c.owner);
    item(last && !last.rows.some((r) => r.res === "diff"), "No differences with the agreement", "Nessuna differenza con l’accordo", c.owner);
    item(last && !last.rows.some((r) => r.res === "unread"), "Every point read", "Ogni punto letto", c.owner);
  }
  if (st === 5) {
    item(c.signed, "Signed file added", "File firmato aggiunto", c.owner);
    item(c.signed?.confirmedBy, `Confirmed as final by ${c.approver}`, `Confermato come definitivo da ${c.approver}`, c.approver);
  }
  if (st === 6) {
    item(c.pipe && c.pipe.stage >= 3, "Extraction done", "Estrazione completata");
    item(c.pipe && !c.pipe.weak.some((w) => w.state === "open"), "Flagged fields decided", "Campi segnalati decisi", c.owner);
    item(c.outcome, "New contract validated and scheduled", "Nuovo contratto validato e programmato");
  }
  return out;
}

/** Open items for this case, in the order the case raises them. Empty once the case has an outcome. */
export function todo(c: Case, ctx: CaseContext): TodoItem[] {
  const lang = ctx.lang;
  const C = ctx.contract;
  const out: TodoItem[] = [];
  if (c.outcome) return out;
  const st = stage(c);
  // Approvals go to the first delegate if there is one, otherwise to the approver.
  const approverOrDelegate = Object.keys(c.delegates)[0] ?? c.approver;

  const add = (kind: TodoKind, who: string, title: Bilingual, why: Bilingual, due?: string, ref = ""): void => {
    const date = due ?? C.cancel;
    out.push({
      kind,
      who,
      title: tx(title, lang),
      why: tx(why, lang),
      due: date,
      dueDays: daysBetween(ctx.today, date),
      stage: st,
      stageLabel: tx(STEPS[st], lang),
      ref,
      stalled: 0,
    });
  };
  const noun = (kind: EmailKind) => EMAIL_KIND[kind].noun;

  c.emails.forEach((e) => {
    if (e.state === "superseded" || e.state === "sent") return;
    const n = noun(e.kind);
    if (e.state === "draft" && !e.to) {
      add("recipient", c.owner, B(`Add the recipient: ${n.en}`, `Aggiungi il destinatario: ${n.it}`), B("Not in the contract — Raffa never invents addresses.", "Non è nel contratto — Raffa non inventa indirizzi."));
    } else if (e.state === "draft") {
      add("approve", approverOrDelegate, B(`Approve the ${n.en}`, `Approva la ${n.it}`), B(`Drafted for ${c.owner}. Nothing is sent until you approve.`, `Preparata per ${c.owner}. Nulla parte finché non approvi.`));
    } else {
      add("send", c.owner, B(`Send and confirm: ${n.en}`, `Invia e conferma: ${n.it}`), B(`Approved by ${e.approvedBy}. Copy, send from your mailbox, then confirm.`, `Approvata da ${e.approvedBy}. Copia, invia dalla tua casella, poi conferma.`));
    }
  });

  // A case opened on the non-renewal branch has no interview question: nothing to complete yet.
  const planOpen = c.phase === "plan" || (c.phase === "interview" && c.feed.some((x) => x.k === "q"));
  if (planOpen) {
    add(
      "plan",
      c.owner,
      B("Complete the renewal plan", "Completa il piano di rinnovo"),
      c.phase === "plan" ? B("Raffa drafted the plan — confirm or edit it.", "Raffa ha preparato il piano — confermalo o modificalo.") : B(`Question ${c.q + 1} of 4 is open.`, `La domanda ${c.q + 1} di 4 è aperta.`),
    );
  }

  c.rems.forEach((r) => {
    if (r.state === "proposed") add("remind", c.owner, B("Confirm the check date", "Conferma la data di controllo"), B(`Proposed: ${r.date}`, `Proposta: ${r.date}`));
    if (r.state === "due") {
      add("reply", c.owner, B("Register the supplier reply", "Registra la risposta del fornitore"), B(`Check date ${r.date} reached — no reply registered in Raffa.`, `Data di controllo ${r.date} raggiunta — nessuna risposta registrata in Raffa.`), r.date);
    }
  });

  c.qs.forEach((q) => {
    if (q.state === "open") {
      add("decide", c.owner, B("Decide the price / term trade-off", "Decidi il compromesso prezzo / durata"), B("The supplier ties the discount to 24 months.", "Il fornitore lega lo sconto a 24 mesi."));
    }
    if (q.state === "agree") {
      if (liabilityConflict(c) && !(c.ui.auth ?? "").trim()) {
        add("policy", approverOrDelegate, B("Policy exception: liability 18 vs 24 months", "Deroga alla policy: responsabilità 18 vs 24 mesi"), B("Binding rule in Procurement Policy v3.2. Record who authorizes it.", "Regola vincolante della Procurement Policy v3.2. Registra chi la autorizza."));
      } else {
        add("agree", c.owner, B("Confirm the agreed terms", "Conferma le condizioni concordate"), B("The supplier confirmed in writing.", "Il fornitore ha confermato per iscritto."));
      }
    }
  });

  if (c.phase === "doc_check") add("draft", c.owner, B("Resolve the draft differences", "Risolvi le differenze della bozza"), B("The draft differs from what was agreed.", "La bozza differisce da quanto concordato."));
  if (c.phase === "sign_wait" || c.phase === "sign_declared") add("sign", c.owner, B("Add the signed contract", "Aggiungi il contratto firmato"), B("Signing happens outside Raffa.", "La firma avviene fuori da Raffa."));
  if (c.phase === "sign_confirm") add("sigok", approverOrDelegate, B("Confirm the signed contract", "Conferma il contratto firmato"), B("Only you can close the negotiation.", "Solo tu puoi chiudere la negoziazione."));
  if (c.pipe?.stage === 3) {
    const n = c.pipe.weak.length;
    add("fields", c.owner, B("Validate the flagged fields", "Valida i campi segnalati"), B(`${n} field${n === 1 ? "" : "s"} below 80% in the new contract.`, `${n} camp${n === 1 ? "o sotto" : "i sotto"} l’80% nel nuovo contratto.`));
  }
  if (c.notice?.state === "check") add("notice", c.owner, B("Prepare the non-renewal notice", "Prepara la disdetta"), B("Clause and deadline already checked.", "Clausola e scadenza già verificate."));
  if (c.notice?.state === "sent") add("nout", approverOrDelegate, B("Record the notice outcome", "Registra l’esito della disdetta"), B("Notice communicated — outcome to verify.", "Disdetta comunicata — esito da verificare."));
  if (c.alt?.state === "compare") add("alt", approverOrDelegate, B("Choose an alternative", "Scegli un’alternativa"), B("Offers received — the current contract stays open.", "Offerte ricevute — il contratto attuale resta aperto."));

  const stalledDays = -daysBetween(ctx.today, c.updated || ctx.today);
  out.forEach((x) => {
    x.stalled = stalledDays >= 7 ? stalledDays : 0;
  });
  return out;
}
