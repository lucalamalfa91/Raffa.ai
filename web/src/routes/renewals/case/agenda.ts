/**
 * What the case needs next: `checklist` (the guide for the current wizard step) and `todo` (the
 * actionable items behind Today and the primary button of each step). Both are derived from the case
 * and its context alone.
 */

import { daysBetween } from "./dates";
import { EMAIL_KIND, liabilityConflict, STEPS, stage } from "./model";
import { msg, resolveText, translator, type Msg, type Vars } from "../../../i18n/translate";
import type { Case, CaseContext, ChecklistItem, EmailKind, TodoItem, TodoKind } from "./types";

export function checklist(c: Case, ctx: CaseContext): ChecklistItem[] {
  const tr = translator(ctx.lang);
  const st = stage(c);
  const out: ChecklistItem[] = [];
  const item = (done: unknown, id: string, who = "", vars?: Vars): void => {
    out.push({ done: !!done, label: tr(id, vars), who });
  };

  if (st === 0) {
    item(c.owner, "Owner assigned", c.approver);
    item(c.goals.prod, "Products and quantities", c.owner);
    item(c.goals.term, "Term", c.owner);
    item(c.goals.target !== undefined, "Economic goal (or “no target”)", c.owner);
    item(c.planVer > 0, "Clauses to change", c.owner);
  }
  if (st === 1) {
    item(c.planVer > 0, "Plan drafted by Raffa", "");
    item(c.planOk, "Plan confirmed", c.owner);
  }
  if (st === 2) {
    const cur = [...c.emails].reverse().find((e) => e.state !== "superseded" && e.kind !== "notice" && e.kind !== "rfq");
    if (cur?.state === "draft" && !cur.to) item(false, "Recipient added", c.owner);
    item(cur && cur.state !== "draft", "Email approved by {approver}", c.approver, { approver: c.approver });
    item(cur?.state === "sent", "Send confirmed", c.owner);
    item(c.rems.some((x) => x.state !== "proposed") && !c.rems.some((x) => x.state === "proposed"), "Check date set", c.owner);
    item(c.reps.length >= (cur?.state === "sent" ? c.round + 1 : c.round), "Supplier reply registered", c.owner);
    if (c.qs.some((q) => q.state === "open" || q.opt)) {
      item(c.qs.every((q) => q.state !== "open"), "Trade-off decided", c.owner);
    }
  }
  if (st === 3) {
    if (liabilityConflict(c)) item((c.ui.auth ?? "").trim(), "Policy exception authorized", c.approver);
    item(c.agreed, "Terms confirmed as agreed", c.owner);
  }
  if (st === 4) {
    const request = c.emails.find((e) => e.kind === "docreq");
    const last = c.docs[c.docs.length - 1];
    item(request?.state === "sent", "Contract requested from the supplier", c.owner);
    item(c.docs.length > 0, "Draft received", c.owner);
    item(last && !last.rows.some((r) => r.res === "diff"), "No differences with the agreement", c.owner);
    item(last && !last.rows.some((r) => r.res === "unread"), "Every point read", c.owner);
  }
  if (st === 5) {
    item(c.signed, "Signed file added", c.owner);
    item(c.signed?.confirmedBy, "Confirmed as final by {approver}", c.approver, { approver: c.approver });
  }
  if (st === 6) {
    item(c.pipe && c.pipe.stage >= 3, "Extraction done", "");
    item(c.pipe && !c.pipe.weak.some((w) => w.state === "open"), "Flagged fields decided", c.owner);
    item(c.outcome, "New contract validated and scheduled", "");
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

  const add = (kind: TodoKind, who: string, title: Msg, why: Msg, due?: string, ref = ""): void => {
    const date = due ?? C.cancel;
    out.push({
      kind,
      who,
      title: resolveText(title, lang),
      why: resolveText(why, lang),
      due: date,
      dueDays: daysBetween(ctx.today, date),
      stage: st,
      stageLabel: resolveText(STEPS[st], lang),
      ref,
      stalled: 0,
    });
  };
  const noun = (kind: EmailKind) => EMAIL_KIND[kind].noun;

  c.emails.forEach((e) => {
    if (e.state === "superseded" || e.state === "sent") return;
    const n = noun(e.kind);
    if (e.state === "draft" && !e.to) {
      add("recipient", c.owner, msg("Add the recipient: {noun}", { noun: n }), msg("Not in the contract — Raffa never invents addresses."));
    } else if (e.state === "draft") {
      add("approve", approverOrDelegate, msg("Approve the {noun}", { noun: n }), msg("Drafted for {owner}. Nothing is sent until you approve.", { owner: c.owner }));
    } else {
      add("send", c.owner, msg("Send and confirm: {noun}", { noun: n }), msg("Approved by {approvedBy}. Copy, send from your mailbox, then confirm.", { approvedBy: e.approvedBy ?? "" }));
    }
  });

  // A case opened on the non-renewal branch has no interview question: nothing to complete yet.
  const planOpen = c.phase === "plan" || (c.phase === "interview" && c.feed.some((x) => x.k === "q"));
  if (planOpen) {
    add(
      "plan",
      c.owner,
      msg("Complete the renewal plan"),
      c.phase === "plan" ? msg("Raffa drafted the plan — confirm or edit it.") : msg("Question {q} of 4 is open.", { q: c.q + 1 }),
    );
  }

  c.rems.forEach((r) => {
    if (r.state === "proposed") add("remind", c.owner, msg("Confirm the check date"), msg("Proposed: {date}", { date: r.date }));
    if (r.state === "due") {
      add("reply", c.owner, msg("Register the supplier reply"), msg("Check date {date} reached — no reply registered in Raffa.", { date: r.date }), r.date);
    }
  });

  c.qs.forEach((q) => {
    if (q.state === "open") {
      add("decide", c.owner, msg("Decide the price / term trade-off"), msg("The supplier ties the discount to 24 months."));
    }
    if (q.state === "agree") {
      if (liabilityConflict(c) && !(c.ui.auth ?? "").trim()) {
        add("policy", approverOrDelegate, msg("Policy exception: liability 18 vs 24 months"), msg("Binding rule in Procurement Policy v3.2. Record who authorizes it."));
      } else {
        add("agree", c.owner, msg("Confirm the agreed terms"), msg("The supplier confirmed in writing."));
      }
    }
  });

  if (c.phase === "doc_check") add("draft", c.owner, msg("Resolve the draft differences"), msg("The draft differs from what was agreed."));
  if (c.phase === "sign_wait" || c.phase === "sign_declared") add("sign", c.owner, msg("Add the signed contract"), msg("Signing happens outside Raffa."));
  if (c.phase === "sign_confirm") add("sigok", approverOrDelegate, msg("Confirm the signed contract"), msg("Only you can close the negotiation."));
  if (c.pipe?.stage === 3) {
    const n = c.pipe.weak.length;
    add("fields", c.owner, msg("Validate the flagged fields"), msg(n === 1 ? "{n} field below 80% in the new contract." : "{n} fields below 80% in the new contract.", { n }));
  }
  if (c.notice?.state === "check") add("notice", c.owner, msg("Prepare the non-renewal notice"), msg("Clause and deadline already checked."));
  if (c.notice?.state === "sent") add("nout", approverOrDelegate, msg("Record the notice outcome"), msg("Notice communicated — outcome to verify."));
  if (c.alt?.state === "compare") add("alt", approverOrDelegate, msg("Choose an alternative"), msg("Offers received — the current contract stays open."));

  const stalledDays = -daysBetween(ctx.today, c.updated || ctx.today);
  out.forEach((x) => {
    x.stalled = stalledDays >= 7 ? stalledDays : 0;
  });
  return out;
}
