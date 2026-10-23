/**
 * Shared, pure building blocks of the renewal case engine: contract figures, clauses, permissions,
 * the 7-step stage, the interview definitions and the phase table. No state is read or written here;
 * `engine.ts`, `agenda.ts` and `view.ts` build on it.
 */

import { addDays, daysBetween } from "./dates";
import { chf, num } from "./text";
import { msg, resolveText, translator, type Lang, type Msg } from "../../../i18n/translate";
import type {
  Case,
  CaseContext,
  CardTable,
  ClauseSource,
  EmailKind,
  Permissions,
  Phase,
  Product,
  Responsibility,
} from "./types";

/* ---------- contract figures ---------- */

export interface Figures {
  product: Product;
  /** Current unit price of the lead product. */
  p0: number;
  qty: number;
  unit: string;
  /** Monthly units are billed twelve times a year. */
  mult: number;
}

export function figures(ctx: CaseContext): Figures {
  const product = ctx.detail.products[0];
  if (!product) throw new Error("A renewal case needs at least one product on the contract");
  return { product, p0: product.price, qty: product.qty, unit: product.unit, mult: /month/.test(product.unit) ? 12 : 1 };
}

/** "CHF 132 per user / month" for unit prices, "CHF 640,000 / yr" for commits. */
export function priceLabel(f: Figures, amount: number): string {
  return f.p0 < 1000 ? `CHF ${amount} per ${f.unit}` : `${chf(amount)} / yr`;
}

export function productLine(p: Product): string {
  return `${p.qty > 1 ? `${num(p.qty)} × ` : ""}${p.name}`;
}

export function productList(ctx: CaseContext): string {
  return ctx.detail.products.map(productLine).join(", ");
}

/* ---------- clauses ---------- */

export interface Clause extends ClauseSource {
  /** Stable key within the case: `p0…` for clauses to push on, `r0…` for ones to raise. */
  k: string;
  /** Policy / term key shared across contracts (`liab`, `payment`, `uplift`, `renew`, or `c_<name>`). */
  lk: string;
  stance: "push" | "raise";
}

export function clauseKey(type: string): string {
  if (/liab/i.test(type)) return "liab";
  if (/payment/i.test(type)) return "payment";
  if (/increase|uplift|protection/i.test(type)) return "uplift";
  if (/renew/i.test(type)) return "renew";
  return `c_${type.toLowerCase().replace(/\W+/g, "")}`;
}

export function clausesOf(ctx: CaseContext): Clause[] {
  return [
    ...ctx.detail.push.map((x, i): Clause => ({ ...x, k: `p${i}`, lk: clauseKey(x.type), stance: "push" })),
    ...ctx.detail.raise.map((x, i): Clause => ({ ...x, k: `r${i}`, lk: clauseKey(x.type), stance: "raise" })),
  ];
}

/** Company-policy rules (the sample "Add company policy" loads these). */
export const POLICY: Partial<Record<string, { bind: boolean; min?: number; text: Msg }>> = {
  liab: { bind: true, min: 24, text: msg("Liability cap: at least 24 months of fees") },
  renew: { bind: true, text: msg("No auto-renewal on software contracts above CHF 100k") },
  payment: { bind: false, text: msg("Payment terms: quarterly or Net 60 preferred") },
};

/** The supplier's liability position is below the binding company minimum (needs a recorded exception). */
export function liabilityConflict(c: Case): boolean {
  const min = POLICY.liab?.min;
  const offered = parseFloat(c.terms.liab?.val ?? "");
  return c.goals.policy && min !== undefined && Number.isFinite(offered) && offered < min;
}

/* ---------- permissions ---------- */

export const NO_APPROVE = msg("Only the approver (CPO) or an explicit delegate can approve emails.");
export const NO_CLOSE = msg("Only the approver (CPO), or a delegate with closing rights, can confirm this.");
export const NO_WORK = msg("Only people working on this renewal can do this.");
export const NO_DELEGATE = msg("Only the approver (CPO) manages delegations.");
export const NO_REASSIGN = msg("Only the approver (CPO) can reassign.");

/** The strongest responsibility `who` holds on this case (a person can hold several). */
export function responsibilityOf(c: Case, who: string): Responsibility {
  if (who === c.approver) return "approver";
  if (who === c.owner) return "owner";
  if (c.delegates[who]) return "delegate";
  return "collab";
}

export function canApprove(c: Case, who: string): boolean {
  return who === c.approver || !!c.delegates[who];
}

export function canWork(c: Case, who: string): boolean {
  return who === c.owner || who === c.approver || !!c.delegates[who];
}

export function canClose(c: Case, who: string): boolean {
  return who === c.approver || !!c.delegates[who]?.close;
}

export function permissionsOf(c: Case, who: string): Permissions {
  return { approve: canApprove(c, who), work: canWork(c, who), close: canClose(c, who), manageTeam: who === c.approver };
}

/* ---------- steps and phases ---------- */

export const STEPS: readonly Msg[] = [
  msg("Start"),
  msg("Plan"),
  msg("Negotiation"),
  msg("Agreement"),
  msg("Draft"),
  msg("Signature"),
  msg("Acquisition"),
];

/** Index (0–6) of the wizard step the case is in. */
export function stage(c: Pick<Case, "phase" | "agreed">): number {
  const p = c.phase;
  if (p === "interview") return 0;
  if (p === "plan") return 1;
  if (p === "email" || p === "waiting_rem" || p === "waiting" || p === "waiting_due" || p === "review") return c.agreed ? 4 : 2;
  if (p === "agree") return 3;
  if (p === "doc_wait" || p === "doc_check") return 4;
  if (p.startsWith("sign")) return 5;
  return 6;
}

export type NextActor = "you" | "owner" | "supplier" | "approver" | "none";

export interface PhaseInfo {
  /** Short status for lists and tags. */
  short: Msg;
  /** Status line in the case header. */
  status: Msg;
  /** What happens next. */
  next: Msg;
  who: NextActor;
}

export const PHASES: Record<Phase, PhaseInfo> = {
  interview: { short: msg("Interview"), status: msg("Interview"), next: msg("Answer the questions"), who: "you" },
  plan: { short: msg("Plan ready"), status: msg("Plan to confirm"), next: msg("Confirm or edit the plan"), who: "owner" },
  email: { short: msg("Email to approve"), status: msg("Email to approve and send"), next: msg("Approve, copy, send and confirm"), who: "owner" },
  waiting_rem: { short: msg("Email to approve"), status: msg("Sent — choose a check date"), next: msg("Confirm the check date"), who: "owner" },
  waiting: { short: msg("Waiting for supplier"), status: msg("Waiting for the supplier"), next: msg("Supplier reply"), who: "supplier" },
  waiting_due: { short: msg("Reply to register"), status: msg("Check date reached"), next: msg("Paste the reply or prepare a follow-up"), who: "owner" },
  review: { short: msg("Reply to review"), status: msg("Reply analysed"), next: msg("Decide"), who: "owner" },
  agree: { short: msg("Terms to confirm"), status: msg("Terms proposed"), next: msg("Confirm as agreed"), who: "owner" },
  doc_wait: { short: msg("Waiting for contract draft"), status: msg("Terms agreed — waiting for the draft"), next: msg("Draft contract from supplier"), who: "supplier" },
  doc_check: { short: msg("Checking draft"), status: msg("Draft under check"), next: msg("Resolve differences"), who: "owner" },
  sign_wait: { short: msg("Waiting for signature"), status: msg("Ready to sign — signing is outside Raffa"), next: msg("Sign, then add the signed file"), who: "owner" },
  sign_declared: { short: msg("Signature declared"), status: msg("Signature declared — document to acquire"), next: msg("Add the signed file"), who: "owner" },
  sign_confirm: { short: msg("Signed · to confirm"), status: msg("Signed file received"), next: msg("Confirm it is the final signed version"), who: "owner" },
  pipeline: { short: msg("Signed · in review"), status: msg("Negotiation completed · new contract in review"), next: msg("Resolve flagged fields"), who: "owner" },
  done: { short: msg("Renewed"), status: msg("Completed — contract signed"), next: msg("Nothing open"), who: "none" },
  notice_out: { short: msg("Not renewed"), status: msg("Not renewed / notice"), next: msg("Exit tasks"), who: "owner" },
};

export function shortStatus(c: Case, lang: Lang = "en"): string {
  const tr = translator(lang);
  if (c.outcome?.kind === "terminated") return tr("Not renewed");
  if (c.notice?.state === "sent") return tr("Notice sent · outcome to verify");
  if (c.alt?.state === "selected" && !c.outcome) return tr("Replacement in progress");
  return resolveText((PHASES[c.phase] ?? PHASES.email).short, lang);
}

export const EMAIL_KIND: Record<EmailKind, { noun: Msg; title: Msg }> = {
  first: { noun: msg("first email"), title: msg("First email") },
  followup: { noun: msg("follow-up"), title: msg("Follow-up") },
  counter: { noun: msg("counter-proposal"), title: msg("Counter-proposal") },
  docreq: { noun: msg("contract request"), title: msg("Contract request") },
  fix: { noun: msg("correction email"), title: msg("Correction email") },
  notice: { noun: msg("non-renewal notice"), title: msg("Non-renewal notice") },
  rfq: { noun: msg("request for proposal"), title: msg("Request for proposal") },
};

/** What to paste or upload at each step -- shown under the step's checklist. */
export const EXAMPLES: readonly Msg[] = [
  msg("Example: “Keep everything, but reduce CPQ to 40 seats.”"),
  msg("Read the plan: confirm it, or press Edit to change an answer."),
  msg("Paste the supplier email as it is — the whole thread is fine. E.g. “Hi Luca, for a 24-month renewal we can offer…”"),
  msg("Confirm only what the supplier wrote. Silence is not acceptance."),
  msg("Add the PDF the supplier sent: Order Form, amendment or redline."),
  msg("Add the signed PDF (a scan is fine). The file name alone is not proof."),
  msg("Decide the fields below 80% — the same review as in Documents."),
];

/* ---------- interview ---------- */

export interface QuestionDef {
  text: string;
  hint: string;
  /** `[value, label]` pairs. */
  opts: Array<[string, string]>;
  multi?: boolean;
}

export function questionDef(i: number, ctx: CaseContext): QuestionDef {
  const tr = translator(ctx.lang);
  const f = figures(ctx);
  const C = ctx.contract;
  switch (i) {
    case 0:
      return {
        text: tr("First: what do you want to do with the products and quantities?"),
        hint: tr("The contract has no usage data, so I cannot say how many licences are unused. Tell me if you know — I will not guess."),
        opts: [
          ["keep", tr("Keep everything as today")],
          ["reduce", tr("Reduce quantities")],
          ["add", tr("Add products")],
        ],
      };
    case 1:
      return {
        text: tr("Term: how long should the renewed contract run?"),
        hint: tr("Current cycle ends {end}; the new term would start on {addDays}.", { end: C.end, addDays: addDays(C.end, 1) }),
        opts: [
          ["12", tr("12 months")],
          ["24", tr("24 months")],
          ["36", tr("36 months")],
        ],
      };
    case 2:
      return {
        text: tr("Economic goal: do you have a price or budget target?"),
        hint: tr("“No target for now” is a valid answer. I compare only with your current contract price ({priceLabel}) and offers you receive — I have no reliable market benchmark.", { priceLabel: priceLabel(f, f.p0) }),
        opts: [
          ["none", tr("No target for now")],
          ["p5", tr("−5% on unit price")],
          ["p10", tr("−10% on unit price")],
          ["budget", tr("Budget: {chf} / yr (−10%)", { chf: chf(C.spend * 0.9) })],
        ],
      };
    default:
      return {
        text: tr("Last: which clauses do you want to change?"),
        hint: tr("Pre-selected are the ones I found costing you money or freedom. Add or remove, or upload a company policy."),
        multi: true,
        opts: clausesOf(ctx).map((x): [string, string] => [x.k, x.type]),
      };
  }
}

/** The label of what the user answered to question `i` (shown in the plan and in the history). */
export function answerLabel(c: Case, ctx: CaseContext, i: number): string {
  const tr = translator(ctx.lang);
  const key = (["prod", "term", "target", "clauses"] as const)[i];
  if (i === 3) {
    const picked = c.goals.clauses ?? [];
    const names = clausesOf(ctx)
      .filter((x) => picked.includes(x.k))
      .map((x) => x.type)
      .concat(c.goals.clausesText ? [c.goals.clausesText] : []);
    return names.length ? names.join(", ") : tr("none for now");
  }
  const value = c.goals[key as "prod" | "term" | "target"];
  if (value === "custom") return (c.goals[`${key as "prod" | "term" | "target"}Text`] as string | undefined) ?? "";
  const option = questionDef(i, ctx).opts.find((o) => o[0] === String(value));
  return option ? option[1] : "";
}

/* ---------- "what to expect" table ---------- */

const OUTLOOK: Array<[RegExp, Msg, Msg]> = [
  [/price increase|uplift|price protection/i, msg("Increase tied to CPI with a cap of 3–5%, or fixed for the term"), msg("Likely — suppliers usually concede a cap or a freeze in exchange for a longer term.")],
  [/auto-renew/i, msg("Renewal by written agreement, or at least 60–90 days notice before term end"), msg("Likely — easy to win if you open the talks before the notice date.")],
  [/liab/i, msg("Cap at 12–24 months of fees, with a separate higher cap for data-breach claims"), msg("Possible — large vendors rarely move on standard paper; a data-breach carve-out is the realistic ask.")],
  [/terminat/i, msg("Exit at convenience is uncommon on committed terms; it usually comes with a fee"), msg("Difficult — expect a trade: exit option against a higher unit price.")],
  [/payment/i, msg("Annual in advance is common; quarterly or Net 45–60 is often accepted"), msg("Possible — often granted, sometimes with a small price premium.")],
  [/credit/i, msg("Credits capped at 10–25% of monthly fees; an exit for chronic failure is common in enterprise terms"), msg("Possible — worth asking if uptime matters to you.")],
  [/commit|envelope|carry|roll|unused/i, msg("Carry-forward of 10–20% of unused volume is a common concession at renewal"), msg("Possible — vendors prefer to grant it when you renew or grow the commit.")],
  [/reassign/i, msg("Moving licences between users within 30 days is common"), msg("Likely — low cost for the vendor.")],
  [/support/i, msg("Support priced as a percentage of spend, with lower tiers above a volume threshold"), msg("Possible — tiered rates come up as spend grows.")],
];

const OUTLOOK_FALLBACK: [Msg, Msg] = [
  msg("Usually negotiable at renewal, especially when tied to term or volume"),
  msg("Depends on your leverage."),
];

export function outlookTable(ctx: CaseContext): CardTable {
  const lang = ctx.lang;
  const tr = translator(lang);
  return {
    layout: [
      { min: 110, fr: 0.8 },
      { min: 0, fr: 1.3 },
      { min: 0, fr: 1.3 },
      { min: 0, fr: 1.5 },
    ],
    cols: [tr("Clause"), tr("In your contract"), tr("Typical practice"), tr("What to expect")],
    rows: clausesOf(ctx).map((x) => {
      const hit = OUTLOOK.find((o) => o[0].test(x.type));
      const [practice, expect] = hit ? [hit[1], hit[2]] : OUTLOOK_FALLBACK;
      return [
        { v: x.type, w: 700 as const },
        { v: `${x.norm} · ${x.src}` },
        { v: resolveText(practice, lang) },
        { v: `${resolveText(expect, lang)} ${tr("Your ask: {ask}", { ask: x.ask })}`, w: 600 as const },
      ];
    }),
  };
}

/* ---------- misc ---------- */

export function daysToDate(ctx: CaseContext, date: string): number {
  return daysBetween(ctx.today, date);
}
