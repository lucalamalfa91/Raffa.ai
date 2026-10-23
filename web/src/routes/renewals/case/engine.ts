/**
 * Renewal case engine -- the wizard's state machine, ported from the design prototype
 * (`renewal-case-v3.js`, global `RC`).
 *
 * **Pure.** `create` and `act` never mutate their input and never read a clock, a locale or the
 * network: they take a `Case` plus a `CaseContext` (viewer, contract, today, scripted services) and
 * return a new `Case`. The backend (or a persisted store) owns `Case` and its audit trail (`decs`);
 * the client calls `act(case, type, payload, ctx)`. `view`, `todo`, `stage` and `checklist` derive
 * everything the UI shows from the same two arguments.
 *
 * **What Raffa never does here.** It sends no email and invents no address: an email goes
 * draft → approved → copied → "sent" only because the user *declares* it sent. "Copy" never means
 * sent. A term becomes "agreed" only on explicit confirmation, and an exception to a binding company
 * policy needs a recorded authoriser.
 *
 * **Scripted parts** (reply analysis, draft diff, vendor offers, extraction, sample replies) live
 * behind `CaseServices` (`types.ts`); `mockServices.ts` reproduces the prototype's scenario.
 */

import { addDays, addMonths, daysBetween, isAfter } from "./dates";
import {
  EMAIL_KIND,
  NO_APPROVE,
  NO_CLOSE,
  NO_DELEGATE,
  NO_REASSIGN,
  NO_WORK,
  answerLabel,
  canApprove,
  canClose,
  canWork,
  clausesOf,
  figures,
  liabilityConflict,
  priceLabel,
  productList,
  stage,
} from "./model";
import { chf } from "./text";
import { msg, resolveText, translator, type Msg, type Text } from "../../../i18n/translate";
import type {
  ActPayload,
  ActionType,
  Case,
  CaseContext,
  Email,
  EmailKind,
  FeedItem,
  TermStatus,
} from "./types";

export { checklist, todo } from "./agenda";
export { shortStatus, stage, permissionsOf, responsibilityOf } from "./model";
export { view } from "./view";

/* ---------- small helpers ---------- */

type NewFeedItem = Omit<FeedItem, "id" | "st" | "at" | "by"> & { by?: string };

const clone = <T,>(value: T): T => JSON.parse(JSON.stringify(value)) as T;

function push(c: Case, ctx: CaseContext, item: NewFeedItem): FeedItem {
  c.seq += 1;
  const minutes = String(Math.min(59, 20 + Math.floor(c.seq * 0.8))).padStart(2, "0");
  // Timestamps are derived from the sequence so the engine stays deterministic; the backend stamps
  // the real time when it persists the entry.
  const entry: FeedItem = {
    ...item,
    id: `f${c.seq}`,
    st: stage(c),
    at: `${ctx.today.slice(0, 6)} · 14:${minutes}`,
    by: item.by ?? "Raffa.ai",
  };
  c.feed.push(entry);
  return entry;
}

function log(c: Case, ctx: CaseContext, by: string, text: Text): void {
  c.decs.push({ by, at: ctx.today, text });
}

function deny(c: Case, ctx: CaseContext, why: Msg): Case {
  c.ui.warn = resolveText(why, ctx.lang);
  return c;
}

function emailOf(c: Case, id: number | undefined): Email | undefined {
  return id === undefined ? undefined : c.emails[id];
}

function cancelReminders(c: Case, states: Array<"proposed" | "set" | "due">): void {
  c.rems.forEach((r) => {
    if ((states as string[]).includes(r.state)) r.state = "cancelled";
  });
}

function setTerm(c: Case, key: string, label: Text, state: TermStatus, val: string | null, ev: number | null): void {
  const term = (c.terms[key] ??= { label, state: "open", plan: false });
  term.state = state;
  term.val = val;
  term.ev = ev;
}

function pushKeys(ctx: CaseContext): string[] {
  return clausesOf(ctx)
    .filter((x) => x.stance === "push")
    .map((x) => x.k);
}

/* ---------- create ---------- */

export interface CreateOptions {
  /** Who approves emails, deroghe and closures (the CPO). Required: the engine never assumes a name. */
  approver: string;
  /** Who works the case. Defaults to the viewer. */
  owner?: string;
  /** Everyone on the case (for the Team tab). Defaults to approver + owner. */
  members?: string[];
}

/** `branch: "notice"` opens the case on the non-renewal branch instead of the renewal interview. */
export function create(cid: string, ctx: CaseContext, branch: "notice" | null, opts: CreateOptions): Case {
  const C = ctx.contract;
  const owner = opts.owner ?? ctx.viewer;
  const c: Case = {
    cid,
    owner,
    approver: opts.approver,
    updated: ctx.today,
    seen: {},
    delegates: {},
    members: opts.members ?? [...new Set([opts.approver, owner])],
    feed: [],
    seq: 0,
    phase: "interview",
    q: 0,
    goals: { prod: null, term: null, clauses: null, policy: false },
    planVer: 0,
    planOk: false,
    emails: [],
    rems: [],
    reps: [],
    round: 0,
    terms: {},
    limits: null,
    decs: [],
    qs: [],
    agreed: null,
    docs: [],
    signed: null,
    pipe: null,
    outcome: null,
    notice: null,
    alt: null,
    thread: null,
    ui: {},
    history: [{ who: owner, at: ctx.today }],
  };
  const products = productList(ctx);
  push(c, ctx, {
    k: "say",
    text: msg("Renewal case opened for {supplier} · {name}. The term ends {end} and notice is due by {cancel} ({cancelDays} days). From the contract I already know: {products}; annual spend {chf}; uplift {uplift}. I will ask four short questions, then propose a plan. Nothing is sent from here: I draft, the approver (CPO) approves, then you copy and send from your own mailbox.", { supplier: C.supplier, name: C.name, end: C.end, cancel: C.cancel, cancelDays: C.cancelDays, products, chf: chf(C.spend), uplift: C.uplift }),
  });
  if (branch === "notice") {
    startNotice(c, ctx);
  } else {
    push(c, ctx, { k: "outlook" });
    push(c, ctx, { k: "q", i: 0 });
  }
  return c;
}

/* ---------- plan / terms ---------- */

function makePlan(c: Case, ctx: CaseContext): void {
  c.planVer += 1;
  c.planOk = false;
  c.phase = "plan";
  push(c, ctx, { k: "plan", ver: c.planVer });
}

function initTerms(c: Case, ctx: CaseContext): void {
  c.terms = { price: { label: msg("Unit price"), state: "open", plan: false }, term: { label: msg("Term"), state: "open", plan: false } };
  clausesOf(ctx).forEach((x) => {
    if ((c.goals.clauses ?? []).includes(x.k)) c.terms[x.lk] = { label: x.type, state: "open", plan: true };
  });
}

/* ---------- emails ---------- */

interface EmailOptions {
  to?: string;
  thread?: string | null;
  vendor?: string;
  since?: string;
}

function mkEmail(c: Case, ctx: CaseContext, kind: EmailKind, o: EmailOptions = {}): Email {
  const C = ctx.contract;
  const f = figures(ctx);
  const g = c.goals;
  const sup = C.supplier;
  const owner = c.owner;
  const signature = `Kind regards,\n${owner}\nProcurement, ${ctx.company}`;
  const picked = clausesOf(ctx).filter((x) => (g.clauses ?? []).includes(x.k));
  const prodTxt =
    g.prod === "reduce"
      ? "a reduction of quantities (details to follow)"
      : g.prod === "add"
        ? "additional products (details to follow)"
        : `the same products and quantities as today (${productList(ctx)})`;
  const priceTxt =
    g.target === "p5"
      ? `a unit price about 5% below today’s ${priceLabel(f, f.p0)}`
      : g.target === "p10"
        ? `a unit price about 10% below today’s ${priceLabel(f, f.p0)}`
        : g.target === "budget"
          ? `an annual budget of ${chf(C.spend * 0.9)}`
          : "a revised price proposal";
  const lastTo = [...c.emails].reverse().find((e) => e.to)?.to ?? "";
  const to = o.to !== undefined ? o.to : lastTo;
  let subject = "";
  let body = "";
  let attach: string[] = [C.file];

  switch (kind) {
    case "first": {
      subject = `${C.name} — renewal discussion — ${ctx.company}`;
      const lines = [
        `• Products and quantities: ${prodTxt}`,
        `• Term: ${g.term === "custom" ? g.termText : `${g.term} months`}`,
        `• Commercials: ${priceTxt}`,
        ...picked.map((x) => `• ${x.type}: ${x.ask}`),
      ];
      body = `Dear ${sup} team,\n\nOur ${C.name} agreement ends on ${C.end}. Ahead of the ${C.cancel} notice date we would like to open the renewal discussion.\n\nWhat we are looking for:\n${lines.join("\n")}\n\nCould you send a revised proposal by ${addDays(ctx.today, 10)}? We are keeping all options open until we receive it.\n\n${signature}`;
      break;
    }
    case "followup":
      subject = `Re: ${C.name} — renewal discussion — follow-up`;
      body = `Dear ${sup} team,\n\nFollowing up on our message of ${o.since ?? ctx.today} about the renewal of ${C.name}. We have not registered a reply on our side yet. Could you let us know when we can expect your proposal? Our notice date under the agreement is ${C.cancel}.\n\n${signature}`;
      break;
    case "counter": {
      const limits = c.limits;
      const price = ctx.services.counterPrice(ctx, limits?.term === 12 ? 12 : 24);
      subject = `Re: ${C.name} — counter-proposal`;
      const lines = [
        `• Unit price: ${priceLabel(f, price)}${limits?.term === 24 ? " for a 24-month term" : " for a 12-month term"}${limits?.exit ? `, with an exit option at month ${limits.exit}` : ""}`,
        "• Uplift: capped at 3% (you offered 4%)",
        "• Payment terms: quarterly in advance. Please clarify the credit-review conditions you mentioned",
        "• Limitation of liability: we ask for 24 months of fees",
      ];
      body = `Dear ${sup} team,\n\nThank you for your proposal. We can move forward on the following basis:\n${lines.join("\n")}\n\nIf this works for you, please confirm in writing and send the revised Order Form.\n\n${signature}`;
      break;
    }
    case "docreq": {
      const A = c.agreed;
      subject = `${C.name} — revised Order Form for signature`;
      body = `Dear ${sup} team,\n\nThank you for confirming. Please send the revised Order Form / amendment reflecting what we agreed:\n• Quantities: unchanged\n• Unit price: ${A ? priceLabel(f, A.price) : "—"}\n• Term: ${A?.term} months\n• Uplift: capped at ${A?.cap}%\n• Payment: quarterly in advance\n• Limitation of liability: ${A?.liab} months of fees\n\nWe will review it and come back before signature.\n\n${signature}`;
      break;
    }
    case "fix":
      subject = `${C.name} — corrections to the draft Order Form`;
      body = `Dear ${sup} team,\n\nThank you for the draft. Before we can sign, two points differ from what we agreed:\n• Uplift: the draft states 5%; we agreed a cap of ${c.agreed?.cap}%.\n• Payment: the draft states annual in advance; we agreed quarterly in advance.\nPlease also send the Annex (SLA schedule) in a readable version.\n\n${signature}`;
      break;
    case "notice":
      subject = `Notice of non-renewal — ${C.name} — ${ctx.company}`;
      body = `Dear ${sup} team,\n\nIn accordance with the renewal clause of our agreement, ${ctx.company} gives written notice that it does not intend to renew ${C.name} beyond the current term ending ${C.end}.\n\nThis notice is sent on ${ctx.today}, ahead of the ${C.cancel} deadline. Please confirm receipt in writing and share the offboarding and data-return steps set out in the agreement.\n\n${signature}`;
      break;
    case "rfq":
      subject = `Request for proposal — ${C.name} replacement — ${ctx.company}`;
      attach = [];
      body = `Dear ${o.vendor} team,\n\n${ctx.company} is evaluating options for ${C.name} from ${addDays(C.end, 1)}. Our essential requirements:\n${(c.alt?.reqs ?? []).map((r) => `• ${r}`).join("\n")}\n\nPlease send your offer, including pricing, term, migration plan and support model, by ${addDays(ctx.today, 14)}.\n\n${signature}`;
      break;
  }

  const email: Email = {
    id: c.emails.length,
    kind,
    ver: 1,
    to,
    subject,
    body,
    attach,
    state: "draft",
    approvedBy: null,
    sent: null,
    thread: o.thread ?? null,
    vendor: o.vendor ?? null,
  };
  c.emails.push(email);
  push(c, ctx, { k: "email", eid: email.id, thread: email.thread });
  return email;
}

function afterSent(c: Case, ctx: CaseContext, e: Email): void {
  if (e.kind === "notice") {
    if (c.notice) {
      c.notice.state = "sent";
      c.notice.sentAt = ctx.today;
    }
    push(c, ctx, { k: "nrec" });
    return;
  }
  if (e.kind === "rfq") {
    push(c, ctx, {
      k: "say",
      thread: e.thread,
      text: msg("RFQ to {vendor} recorded as sent. Paste or attach their offer here when it arrives — it stays in this thread.", { vendor: e.vendor }),
    });
    return;
  }
  proposeReminder(c, ctx, e);
}

/** Proposes a check date: +7 days (+5 after a reply, +3 after a follow-up), never later than notice −3 days. */
function proposeReminder(c: Case, ctx: CaseContext, e: Email): void {
  let date = addDays(ctx.today, e.kind === "followup" ? 3 : c.round ? 5 : 7);
  const cap = addDays(ctx.contract.cancel, -3);
  if (isAfter(date, cap)) date = cap;
  const rid = c.rems.length;
  c.rems.push({ rid, date, state: "proposed" });
  c.ui.remDate = date;
  c.phase = "waiting_rem";
  push(c, ctx, { k: "rem", rid });
}

/* ---------- notice / alternatives ---------- */

function startNotice(c: Case, ctx: CaseContext): void {
  if (c.notice) return;
  c.notice = { state: "check", sentAt: null, evidence: false };
  push(c, ctx, { k: "ncheck" });
}

function startAlt(c: Case, ctx: CaseContext): void {
  if (c.alt) return;
  const reqs = [...ctx.detail.products.map((p) => `${p.qty > 1 ? `${p.qty.toLocaleString("en-US")} × ` : ""}${p.name}`), "Integration with current ERP", "EU data residency"];
  c.alt = { state: "req", reqs, vendors: [], offers: {} };
  c.ui.altReq = [...reqs];
  push(c, ctx, { k: "areq" });
}

/* ---------- reducer ---------- */

/** Actions that only move UI state and therefore do not bump `updated` (the "stalled" clock). */
const UI_ONLY: ReadonlySet<ActionType> = new Set<ActionType>([
  "ui",
  "visit",
  "thread",
  "sinceOk",
  "sentOpen",
  "sentCancel",
  "edit",
  "cancelEdit",
  "openReply",
  "closeReply",
  "sampleReply",
]);

export function act(c0: Case, type: ActionType, payload: ActPayload | undefined, ctx: CaseContext): Case {
  const c = clone(c0);
  const p = payload ?? {};
  c.ui.warn = "";
  const v = ctx.viewer;
  const tr = translator(ctx.lang);
  const f = figures(ctx);
  const C = ctx.contract;
  const sv = ctx.services;
  if (!UI_ONLY.has(type)) c.updated = ctx.today;

  switch (type) {
    case "visit": {
      const seen = c.seen[v] ?? 0;
      const news = seen === 0 ? [] : c.feed.filter((x) => Number(x.id.slice(1)) > seen && x.by !== v && x.k !== "q");
      c.ui.since = news.length ? { n: news.length, items: news.slice(-4).map((x) => ({ by: x.by, k: x.k })) } : null;
      c.seen[v] = c.seq;
      break;
    }
    case "sinceOk":
      c.ui.since = null;
      break;
    case "ui": {
      if (p.k === undefined) break;
      if (p.k.startsWith("to:")) {
        const e = emailOf(c, Number(p.k.slice(3)));
        if (e) e.to = String(p.v ?? "");
      } else {
        c.ui[p.k] = p.v;
      }
      break;
    }

    /* ----- interview and plan ----- */
    case "ans": {
      const key = p.q === undefined ? undefined : (["prod", "term", "target"] as const)[p.q];
      if (!key || c.q !== p.q || p.val === undefined) break;
      c.goals[key] = p.val;
      push(c, ctx, { k: "user", by: v, text: answerLabel(c, ctx, p.q ?? 0) });
      c.q += 1;
      if (c.q === 3) c.goals.clauses = pushKeys(ctx);
      push(c, ctx, { k: "q", i: c.q });
      break;
    }
    case "toggle": {
      const picked = c.goals.clauses ?? [];
      const at = p.k === undefined ? -1 : picked.indexOf(p.k);
      if (at > -1) picked.splice(at, 1);
      else if (p.k !== undefined) picked.push(p.k);
      c.goals.clauses = picked;
      break;
    }
    case "policy":
      c.goals.policy = true;
      push(c, ctx, { k: "policy", by: v });
      break;
    case "q3done":
      push(c, ctx, { k: "user", by: v, text: answerLabel(c, ctx, 3) });
      makePlan(c, ctx);
      break;
    case "editPlan":
      c.q = 0;
      c.planOk = false;
      c.phase = "interview";
      push(c, ctx, {
        k: "say",
        text: msg("Okay — reopening the interview. Your previous answers stay; change what you need."),
      });
      push(c, ctx, { k: "q", i: 0 });
      break;
    case "confirmPlan":
      if (!canWork(c, v)) return deny(c, ctx, NO_WORK);
      c.planOk = true;
      initTerms(c, ctx);
      c.phase = "email";
      log(c, ctx, v, msg("Plan v{planVer} confirmed", { planVer: c.planVer }));
      push(c, ctx, {
        k: "say",
        text: msg("Plan v{planVer} confirmed by {v}. Here is the first email. I do not have the supplier contact in the contract, so add the recipient before approving.", { planVer: c.planVer, v }),
      });
      mkEmail(c, ctx, "first", { to: "" });
      break;

    /* ----- email lifecycle: draft → approved → copied → sent (declared) ----- */
    case "approve": {
      if (!canApprove(c, v)) return deny(c, ctx, NO_APPROVE);
      const e = emailOf(c, p.eid);
      if (!e) break;
      if (!e.to) {
        c.ui.warn = tr("Add the recipient first — I never invent addresses.");
        break;
      }
      e.state = "approved";
      e.approvedBy = v;
      e.approvedVer = e.ver;
      log(c, ctx, v, msg("Email v{ver} approved", { ver: e.ver }));
      break;
    }
    case "edit": {
      const e = emailOf(c, p.eid);
      if (!e) break;
      c.ui.edit = e.id;
      c.ui.editBody = e.body;
      break;
    }
    case "cancelEdit":
      c.ui.edit = null;
      break;
    case "saveEdit": {
      const e = emailOf(c, p.eid);
      if (!e) break;
      if (c.ui.editBody !== undefined && c.ui.editBody !== e.body) {
        e.body = c.ui.editBody;
        e.ver += 1;
        if (e.state !== "draft") {
          log(c, ctx, v, msg("Email edited — previous approval invalidated"));
        }
        e.state = "draft";
        e.approvedBy = null;
      }
      c.ui.edit = null;
      break;
    }
    case "copy": {
      const e = emailOf(c, p.eid);
      if (!e || e.state === "draft") break;
      c.clip = `To: ${e.to}\nSubject: ${e.subject}\n\n${e.body}`;
      // Copying is not sending: the email only moves to "copied".
      if (e.state === "approved") e.state = "copied";
      break;
    }
    case "sentOpen":
      c.ui.sentOpen = p.eid ?? null;
      c.ui.sentDate = `${ctx.today} · 14:40`;
      c.ui.sentEdited = false;
      c.ui.sentText = "";
      break;
    case "sentCancel":
      c.ui.sentOpen = null;
      break;
    case "sentOk": {
      if (!canWork(c, v)) return deny(c, ctx, NO_WORK);
      const e = emailOf(c, p.eid);
      if (!e || e.state === "draft" || e.state === "superseded") break;
      const edited = !!c.ui.sentEdited;
      e.sent = { by: v, at: c.ui.sentDate || ctx.today, to: e.to, edited, text: edited && c.ui.sentText ? c.ui.sentText : null };
      e.state = "sent";
      c.ui.sentOpen = null;
      log(
        c,
        ctx,
        v,
        msg(edited ? "Send confirmed (declared by user) — text edited after approval" : "Send confirmed (declared by user)"),
      );
      afterSent(c, ctx, e);
      break;
    }
    case "toEx": {
      const e = emailOf(c, p.eid);
      if (e && sv.exampleAddress) e.to = sv.exampleAddress(e, ctx);
      break;
    }

    /* ----- waiting for the supplier ----- */
    case "remOk": {
      if (!canWork(c, v)) return deny(c, ctx, NO_WORK);
      const r = p.rid === undefined ? undefined : c.rems[p.rid];
      if (!r) break;
      r.date = c.ui.remDate || r.date;
      r.state = "set";
      c.phase = "waiting";
      log(c, ctx, v, msg("Check date set: {date}", { date: r.date }));
      push(c, ctx, {
        k: "say",
        text: msg("Check date {date} confirmed. It stays active when the chat is closed and posts an app notice linked to this case. Your contractual notice date remains {cancel}.", { date: r.date, cancel: C.cancel }),
      });
      break;
    }
    case "remChange": {
      const r = c.rems.find((x) => x.state === "due");
      if (r) {
        r.state = "proposed";
        c.ui.remDate = addDays(ctx.today, 5);
        r.date = c.ui.remDate;
      }
      break;
    }
    case "ff": {
      // Demo shortcut: jump to the check date.
      const r = c.rems.find((x) => x.state === "set");
      if (r) {
        r.state = "due";
        c.phase = "waiting_due";
        push(c, ctx, {
          k: "say",
          text: msg("Check date reached ({date}). No reply has been registered in Raffa — I cannot see your mailbox, so I will not say the supplier did not answer.", { date: r.date }),
        });
      }
      break;
    }
    case "followup": {
      const last = [...c.emails].reverse().find((x) => x.state === "sent");
      c.rems.forEach((x) => {
        if (x.state === "due") x.state = "cancelled";
      });
      c.phase = "email";
      mkEmail(c, ctx, "followup", { since: last?.sent ? last.sent.at.slice(0, 6) : ctx.today });
      break;
    }
    case "openReply":
      c.ui.replyOpen = true;
      if (!c.ui.replyText) c.ui.replyText = "";
      break;
    case "closeReply":
      c.ui.replyOpen = false;
      break;
    case "sampleReply":
      c.ui.replyText = sv.sampleReply(c, ctx, c.round + 1);
      break;

    /* ----- reply analysis and trade-off ----- */
    case "reply": {
      const text = (p.text ?? c.ui.replyText ?? "").trim();
      if (!text) break;
      c.round += 1;
      const rid = c.reps.length;
      c.reps.push({ rid, round: c.round, text, by: v, at: ctx.today, rows: [] });
      c.ui.replyOpen = false;
      c.ui.replyText = "";
      cancelReminders(c, ["set", "due", "proposed"]);
      c.emails.forEach((x) => {
        if (x.state !== "sent" && x.state !== "superseded" && x.kind !== "notice" && x.kind !== "rfq") {
          if (x.state === "approved" || x.state === "copied") {
            log(c, ctx, "Raffa.ai", msg("Draft v{ver} superseded by a newer reply", { ver: x.ver }));
          }
          x.state = "superseded";
        }
      });
      push(c, ctx, { k: "reply", rid, by: v });
      const analysis = sv.analyzeReply(c, ctx, rid);
      analysis.terms.forEach((t) => setTerm(c, t.key, t.label, t.state, t.val, rid + 1));
      const reply = c.reps[rid];
      if (reply) reply.rows = analysis.rows;
      push(c, ctx, { k: "ana", rid });
      c.phase = "review";
      if (c.round === 1) {
        if (String(c.goals.term) !== "24") {
          c.qs.push({ did: c.qs.length, state: "open" });
          push(c, ctx, { k: "dec", did: c.qs.length - 1 });
        } else {
          c.limits = { term: 24, priceMax: sv.counterPrice(ctx, 24) };
          push(c, ctx, {
            k: "say",
            text: msg("Your plan already asks for 24 months, so no trade-off to decide. I drafted the counter-proposal."),
          });
          mkEmail(c, ctx, "counter");
          c.phase = "email";
        }
      } else {
        c.qs.push({ did: c.qs.length, state: "agree" });
        push(c, ctx, { k: "agree", did: c.qs.length - 1, rid });
        c.phase = "agree";
      }
      break;
    }
    case "decide": {
      if (!canWork(c, v)) return deny(c, ctx, NO_WORK);
      const q = p.did === undefined ? undefined : c.qs[p.did];
      if (!q || q.state !== "open" || !p.opt) break;
      const price24 = sv.counterPrice(ctx, 24);
      const options = {
        a: { term: 24, priceMax: price24, exit: undefined, label: msg("Accept up to 24 months if the unit price is ≤ {priceLabel}", { priceLabel: priceLabel(f, price24) }) },
        b: { term: 12, priceMax: undefined, exit: undefined, label: msg("Stay at 12 months — decline the discount tied to 24") },
        c: { term: 24, priceMax: price24, exit: 12, label: msg("Counter: 24 months with an exit option at month 12") },
      };
      const O = options[p.opt];
      q.state = "done";
      q.opt = p.opt;
      q.by = v;
      q.label = O.label;
      c.limits = { term: O.term, ...(O.priceMax !== undefined && { priceMax: O.priceMax }), ...(O.exit !== undefined && { exit: O.exit }) };
      log(c, ctx, v, msg("Limit recorded: {label}", { label: O.label }));
      push(c, ctx, {
        k: "say",
        text: msg("Registered. I will use this limit in the next turns. Here is the counter-proposal — it needs approval like any email."),
      });
      mkEmail(c, ctx, "counter");
      c.phase = "email";
      break;
    }

    /* ----- agreement: only on explicit confirmation ----- */
    case "agreeOk": {
      if (!canWork(c, v)) return deny(c, ctx, NO_WORK);
      const q = p.did === undefined ? undefined : c.qs[p.did];
      if (!q || q.state !== "agree") break;
      if (liabilityConflict(c) && !(c.ui.auth ?? "").trim()) {
        c.ui.warn = tr("Record who authorized the exception to the policy first.");
        break;
      }
      q.state = "done";
      q.by = v;
      Object.values(c.terms).forEach((t) => {
        if (t.state === "proposed") t.state = "agreed";
      });
      const term = c.limits?.term ?? 24;
      c.agreed = {
        price: sv.counterPrice(ctx, term),
        term,
        cap: parseFloat(C.uplift) > 0 ? parseFloat(c.terms.uplift?.val ?? "") || 0 : 0,
        liab: parseFloat(c.terms.liab?.val ?? "") || 0,
      };
      if (c.ui.auth) c.agreed.auth = c.ui.auth;
      const auth = c.ui.auth;
      log(
        c,
        ctx,
        v,
        auth
          ? msg("Terms confirmed as agreed (evidence: reply {round}) · exception authorized by {auth} (recorded outside Raffa)", { round: c.round, auth })
          : msg("Terms confirmed as agreed (evidence: reply {round})", { round: c.round }),
      );
      c.phase = "doc_wait";
      push(c, ctx, {
        k: "say",
        text: msg("Agreed terms recorded. Next I need the contract draft. Here is the request email; when the draft arrives, add it with “Add contract” — it will not become an active contract."),
      });
      mkEmail(c, ctx, "docreq");
      break;
    }

    /* ----- draft contract and signature ----- */
    case "addDoc": {
      if (!c.agreed) {
        push(c, ctx, {
          k: "say",
          text: msg("I received a document before terms were confirmed as agreed. I cannot verify it against an agreed position yet — confirm the terms first."),
        });
        break;
      }
      if (c.phase === "sign_wait" || c.phase === "sign_confirm" || c.phase === "sign_declared") {
        c.signed = { file: `${C.supplier}_OrderForm_signed.pdf`, by: v, at: ctx.today };
        c.phase = "sign_confirm";
        push(c, ctx, { k: "user", by: v, text: tr("Added signed contract: {file}", { file: c.signed.file }) });
        push(c, ctx, { k: "sig" });
        break;
      }
      cancelReminders(c, ["set", "due", "proposed"]);
      const version = c.docs.length + 1;
      const file = `${C.supplier}_OrderForm_draft_v${version}.pdf`;
      c.docs.push({ v: version, file, by: v, at: ctx.today, rows: sv.verifyDraft(c, ctx, version), manual: false });
      push(c, ctx, { k: "user", by: v, text: tr("Added contract draft: {file}", { file }) });
      push(c, ctx, { k: "doc", v: version });
      c.phase = "doc_check";
      if (!c.docs[version - 1]?.rows.some((r) => r.res !== "ok")) {
        c.phase = "sign_wait";
        push(c, ctx, { k: "signwait" });
      }
      break;
    }
    case "fixEmail":
      c.phase = "email";
      mkEmail(c, ctx, "fix");
      break;
    case "markAnnex": {
      if (!canWork(c, v)) return deny(c, ctx, NO_WORK);
      const d = p.v === undefined ? undefined : c.docs[Number(p.v) - 1];
      if (!d) break;
      d.rows.forEach((r) => {
        if (r.res === "unread") {
          r.res = "manual";
          r.draft = tr("verified manually by {v}", { v });
        }
      });
      log(c, ctx, v, msg("Annex verified manually"));
      break;
    }
    case "signDeclared":
      c.phase = "sign_declared";
      push(c, ctx, {
        k: "say",
        text: msg("Status: signature declared — document to acquire. The task stays open until you add the signed file."),
      });
      break;
    case "sigOk": {
      if (!canClose(c, v)) return deny(c, ctx, NO_CLOSE);
      if (!c.signed) break;
      c.signed.confirmedBy = v;
      cancelReminders(c, ["set", "due", "proposed"]);
      c.phase = "pipeline";
      c.pipe = sv.startExtraction(ctx);
      log(c, ctx, v, msg("Signed document confirmed as final — negotiation completed"));
      push(c, ctx, { k: "pipe" });
      c.tick = true;
      break;
    }

    /* ----- acquisition: the standard extraction pipeline ----- */
    case "tick":
      if (!c.pipe || c.pipe.stage >= 3) break;
      c.pipe.stage += 1;
      if (c.pipe.stage < 3) c.tick = true;
      break;
    case "acceptF": {
      const w = p.i === undefined ? undefined : c.pipe?.weak[p.i];
      if (w) w.state = "accepted";
      break;
    }
    case "correctF": {
      const w = p.i === undefined ? undefined : c.pipe?.weak[p.i];
      const next = String(c.ui[`cf${p.i}`] ?? "").trim();
      if (w && next) {
        w.val = next;
        w.state = "corrected";
      }
      break;
    }
    case "validate": {
      if (!c.pipe || !c.agreed || c.pipe.weak.some((w) => w.state === "open")) break;
      c.pipe.stage = 4;
      c.phase = "done";
      const A = c.agreed;
      const spend = C.spend - f.qty * (f.p0 - A.price) * f.mult;
      const start = addDays(C.end, 1);
      const end = addMonths(C.end, A.term);
      const cancel = addDays(end, -C.notice);
      c.outcome = {
        kind: "renewed",
        start,
        end,
        cancel,
        cancelDays: daysBetween(ctx.today, cancel),
        spend,
        delta: C.spend - spend,
        price: priceLabel(f, A.price),
        was: f.p0,
        file: c.signed?.file ?? "",
      };
      push(c, ctx, { k: "out" });
      break;
    }

    /* ----- non-renewal notice ----- */
    case "startNotice":
      startNotice(c, ctx);
      break;
    case "nStart":
      if (!c.notice) break;
      c.notice.state = "email";
      mkEmail(c, ctx, "notice");
      break;
    case "nReceipt":
      if (!c.notice) break;
      c.notice.evidence = true;
      c.notice.evFile = "Notice_receipt_confirmation.pdf";
      push(c, ctx, { k: "user", by: v, text: tr("Added receipt: {evFile}", { evFile: c.notice.evFile }) });
      break;
    case "nOut": {
      if (!canClose(c, v)) return deny(c, ctx, NO_CLOSE);
      if (!c.notice) break;
      if (!c.notice.evidence && !c.ui.noEv) {
        c.ui.warn = tr("Attach evidence of receipt, or tick that you proceed without it.");
        break;
      }
      cancelReminders(c, ["set", "due", "proposed"]);
      c.notice.state = "out";
      c.notice.by = v;
      c.phase = c.phase === "done" ? "done" : "notice_out";
      if (!c.alt || c.alt.state !== "selected") {
        c.outcome = { kind: "terminated", end: C.end, when: ctx.today };
      } else {
        c.alt.exitDone = true;
        if (c.alt.newDone) c.outcome = { kind: "terminated", end: C.end, when: ctx.today, ...(c.alt.chosen && { replacedBy: c.alt.chosen }) };
      }
      log(c, ctx, v, msg("Outcome recorded: not renewed"));
      push(c, ctx, { k: "nout" });
      break;
    }

    /* ----- alternatives ----- */
    case "startAlt":
      startAlt(c, ctx);
      break;
    case "altReqs":
      if (!c.alt) break;
      c.alt.state = "cand";
      push(c, ctx, { k: "user", by: v, text: tr("Requirements confirmed") });
      push(c, ctx, { k: "acand" });
      break;
    case "altAdd": {
      const name = (c.ui.altName ?? "").trim();
      if (c.alt && name && !c.alt.vendors.includes(name)) c.alt.vendors.push(name);
      c.ui.altName = "";
      break;
    }
    case "altEx":
      sv.exampleVendors().forEach((name) => {
        if (c.alt && !c.alt.vendors.includes(name)) c.alt.vendors.push(name);
      });
      break;
    case "altRfq": {
      const alt = c.alt;
      if (!alt) break;
      alt.state = "rfq";
      c.thread = alt.vendors[0] ?? null;
      alt.vendors.forEach((vendor) => {
        if (!alt.offers[`${vendor}_rfq`]) {
          alt.offers[`${vendor}_rfq`] = true;
          mkEmail(c, ctx, "rfq", { vendor, thread: vendor, to: "" });
        }
      });
      break;
    }
    case "thread":
      c.thread = p.t ?? null;
      break;
    case "altOffer": {
      const alt = c.alt;
      if (!alt || !p.vendor) break;
      alt.offers[p.vendor] = sv.vendorOffer(ctx, p.vendor, v);
      push(c, ctx, { k: "user", by: v, text: tr("Added offer from {vendor}", { vendor: p.vendor }), thread: p.vendor });
      push(c, ctx, { k: "aoff", vendor: p.vendor, thread: p.vendor });
      const received = Object.keys(alt.offers).filter((k) => !k.endsWith("_rfq"));
      if (received.length >= 1 && !alt.cmp) {
        alt.cmp = true;
        alt.state = "compare";
        push(c, ctx, { k: "acmp" });
      }
      break;
    }
    case "altSel":
      if (!canClose(c, v)) return deny(c, ctx, NO_CLOSE);
      if (!c.alt || !p.vendor) break;
      c.alt.state = "selected";
      c.alt.chosen = p.vendor;
      log(c, ctx, v, msg("Alternative selected: {vendor} (current contract not closed)", { vendor: p.vendor }));
      push(c, ctx, { k: "asel" });
      break;
    case "altNew": {
      const alt = c.alt;
      if (!alt || !p.vendor) break;
      alt.newDone = true;
      alt.newFile = `${p.vendor}_Contract_signed.pdf`;
      push(c, ctx, { k: "user", by: v, text: tr("Added new vendor contract: {newFile}", { newFile: alt.newFile }) });
      c.pipeNew = true;
      if (alt.exitDone) c.outcome = { kind: "terminated", end: C.end, when: ctx.today, ...(alt.chosen && { replacedBy: alt.chosen }) };
      push(c, ctx, { k: "asel2" });
      break;
    }

    /* ----- collaboration: only the approver manages the team ----- */
    case "delegate": {
      if (v !== c.approver) return deny(c, ctx, NO_DELEGATE);
      if (!p.who) break;
      if (p.on) c.delegates[p.who] = { close: false };
      else delete c.delegates[p.who];
      log(c, ctx, v, p.on ? msg("Delegate added: {who}", { who: p.who }) : msg("Delegation removed: {who}", { who: p.who }));
      break;
    }
    case "delegateClose": {
      if (v !== c.approver) return deny(c, ctx, NO_DELEGATE);
      const delegate = p.who ? c.delegates[p.who] : undefined;
      if (delegate) delegate.close = !delegate.close;
      break;
    }
    case "transfer":
      if (v !== c.approver) return deny(c, ctx, NO_REASSIGN);
      if (!p.who) break;
      c.owner = p.who;
      if (!c.members.includes(p.who)) c.members.push(p.who);
      c.history.push({ who: p.who, at: ctx.today });
      log(c, ctx, v, msg("Ownership reassigned to {who}", { who: p.who }));
      break;

    /* ----- free text: answers the open question, or runs a typed command ----- */
    case "say": {
      const text = (p.text ?? "").trim();
      if (!text) break;
      if (c.phase === "interview" && c.q < 3 && c.feed.some((x) => x.k === "q" && x.i === c.q)) {
        const key = (["prod", "term", "target"] as const)[c.q];
        if (key) {
          c.goals[key] = "custom";
          c.goals[`${key}Text`] = text;
        }
        push(c, ctx, { k: "user", by: v, text });
        c.q += 1;
        if (c.q === 3) c.goals.clauses = pushKeys(ctx);
        push(c, ctx, { k: "q", i: c.q });
        break;
      }
      if (c.phase === "interview" && c.q === 3) {
        c.goals.clausesText = text;
        push(c, ctx, { k: "user", by: v, text });
        break;
      }
      const open = [...c.emails].reverse().find((x) => x.state !== "sent" && x.state !== "superseded");
      if (/^(approva|approve)/i.test(text) && open) return act(c, "approve", { eid: open.id }, ctx);
      if (/^(copia|copy)/i.test(text) && open) return act(c, "copy", { eid: open.id }, ctx);
      if (/^(ho inviato|i sent|sent)/i.test(text) && open) {
        c.ui.sentOpen = open.id;
        c.ui.sentDate = `${ctx.today} · 14:40`;
        break;
      }
      if (/^(conferma piano|confirm plan)/i.test(text) && c.phase === "plan") return act(c, "confirmPlan", {}, ctx);
      push(c, ctx, { k: "user", by: v, text, tag: msg("internal comment") });
      break;
    }
  }
  return c;
}

/** Name of an email kind in the viewer's language (shared with the Today queue). */
export function emailKindLabel(kind: EmailKind, lang: CaseContext["lang"]): string {
  return resolveText(EMAIL_KIND[kind].noun, lang);
}
