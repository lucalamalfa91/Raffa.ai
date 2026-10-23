/**
 * Scripted implementation of `CaseServices` -- the demo scenario from the design prototype
 * (Helvetia Foods AG; Salesforce, DocuSign, AWS, Microsoft). Supplier replies, the reading of a
 * reply, the draft diff, vendor offers and the extraction pipeline are all scripted here and nowhere
 * else, so the engine can be pointed at real services without touching its logic.
 *
 * Nothing in this file is evidence about a real supplier: it exists so the wizard can be walked end to
 * end before the LLM reply analysis, contract diff and extraction pipeline exist.
 */

import { figures, POLICY, priceLabel, productList, type Figures } from "./model";
import { msg, translator, type Text } from "../../../i18n/translate";
import type { CaseServices, ReplyAnalysis } from "./services";
import type { AltOffer, Case, CaseContext, DraftRow, ReplyRow } from "./types";

const firstOffer = (f: Figures): number => Math.round(f.p0 * 0.95);
const counterOffer = (f: Figures, term: number): number => Math.round(f.p0 * (term === 12 ? 0.97 : 0.923));

/** The uplift cap the supplier would concede: about 57% of the contract's uplift, at least 1 point. */
function concededCap(ctx: CaseContext): number {
  const uplift = parseFloat(ctx.contract.uplift);
  return uplift > 0 ? Math.max(1, Math.round(uplift * 0.57)) : 0;
}

const VENDOR_OFFERS = [
  { name: "Vendor A", price: 0.82, term: 24, cover: "4 of 5", mig: "8 weeks", gap: "Support SLA not stated" },
  { name: "Vendor B", price: 0.89, term: 12, cover: "5 of 5", mig: "12 weeks", gap: "Data-migration cost not stated" },
];

function sampleReply(c: Readonly<Case>, ctx: CaseContext, round: number): string {
  const f = figures(ctx);
  const sup = ctx.contract.supplier;
  const cap = concededCap(ctx);
  if (round === 1) {
    return `Hi Luca,\n\nthanks for reaching out. For a 24-month renewal we can offer ${priceLabel(f, firstOffer(f))} on ${f.product.name} (today: ${priceLabel(f, f.p0)})${cap ? `. We cannot cap the annual uplift below ${cap}%` : ""}. On payment terms we may be flexible, subject to a credit review. The liability cap follows our standard terms and is not negotiable.\n\nBest regards,\nAnna Meyer\n${sup}`;
  }
  const term = c.limits?.term ?? 24;
  return `Hi Luca,\n\nthank you. We can do ${priceLabel(f, counterOffer(f, term))} for ${term} months${cap ? ", with the annual uplift capped at 3%" : ""}. Quarterly invoicing in advance is fine. On liability we can go to 18 months of fees, but not 24. Please confirm and we will send the revised Order Form.\n\nBest regards,\nAnna Meyer\n${sup}`;
}

function analyzeReply(c: Readonly<Case>, ctx: CaseContext, rid: number): ReplyAnalysis {
  const tr = translator(ctx.lang);
  const f = figures(ctx);
  const round = c.reps[rid]?.round ?? 1;
  const cap = concededCap(ctx);
  const hasUplift = parseFloat(ctx.contract.uplift) > 0;
  const rows: ReplyRow[] = [];
  const terms: ReplyAnalysis["terms"] = [];
  const target =
    c.goals.target === "p5" ? tr("−5% target") : c.goals.target === "p10" ? tr("−10% target") : c.goals.target === "budget" ? tr("budget target") : tr("no target set");
  const row = (label: string, said: string, plan: string, state: ReplyRow["state"], warn = ""): void => {
    rows.push({ label, said, plan, state, warn });
  };
  const term = (key: string, label: Text, state: ReplyAnalysis["terms"][number]["state"], val: string | null): void => {
    terms.push({ key, label, state, val });
  };
  const policyActive = c.goals.policy;

  if (round === 1) {
    const price = firstOffer(f);
    const pct = Math.round(((f.p0 - price) / f.p0) * 100);
    row(tr("Unit price"), tr("{price} ({change}) — only with 24 months", { price: priceLabel(f, price), change: pct ? `−${pct}%` : "=" }), target, "proposed");
    term("price", msg("Unit price"), "proposed", priceLabel(f, price));
    row(
      tr("Term"),
      tr("24 months required"),
      c.goals.term === "custom" ? (c.goals.termText ?? "") : tr("{n} months", { n: c.goals.term ?? "" }),
      String(c.goals.term) === "24" ? "proposed" : "conflict",
    );
    term("term", msg("Term"), "proposed", "24");
    if (hasUplift) {
      row(tr("Uplift"), tr("cap at {cap}%", { cap }), c.terms.uplift ? tr("cap at 3% or freeze") : tr("not in your plan"), "proposed");
      term("uplift", msg("Uplift"), "proposed", `${cap}%`);
    }
    row(tr("Payment terms"), tr("“may be flexible, subject to credit review”"), c.terms.payment ? tr("quarterly") : tr("not in your plan"), "unclear");
    term("payment", msg("Payment terms"), "unclear", null);
    row(
      tr("Liability cap"),
      tr("standard cap, not negotiable"),
      c.terms.liab ? tr("24 months") : tr("not in your plan"),
      "rejected",
      policyActive && POLICY.liab ? tr("Conflicts with policy (min. 24 months)") : "",
    );
    term("liab", msg("Liability cap"), "rejected", "12");
  } else {
    const months = c.limits?.term ?? 24;
    const price = counterOffer(f, months);
    row(
      tr("Unit price"),
      priceLabel(f, price),
      c.limits?.priceMax ? tr("your limit: ≤ {priceLabel}", { priceLabel: priceLabel(f, c.limits.priceMax) }) : target,
      "proposed",
    );
    term("price", msg("Unit price"), "proposed", priceLabel(f, price));
    row(tr("Term"), tr("{n} months", { n: months }), tr("your limit: {n} months", { n: months }), "proposed");
    term("term", msg("Term"), "proposed", String(months));
    if (hasUplift) {
      row(tr("Uplift"), tr("capped at 3%"), tr("cap at 3%"), "proposed");
      term("uplift", msg("Uplift"), "proposed", "3%");
    }
    row(tr("Payment terms"), tr("quarterly in advance"), tr("quarterly"), "proposed");
    term("payment", msg("Payment terms"), "proposed", "quarterly");
    row(
      tr("Liability cap"),
      tr("18 months of fees (not 24)"),
      tr("24 months"),
      "proposed",
      policyActive ? tr("Conflicts with policy (min. 24 months) — needs authorization") : "",
    );
    term("liab", msg("Liability cap"), "proposed", "18");
  }
  return { rows, terms };
}

function verifyDraft(c: Readonly<Case>, ctx: CaseContext, version: number): DraftRow[] {
  const tr = translator(ctx.lang);
  const f = figures(ctx);
  const A = c.agreed;
  if (!A) return [];
  const first = version === 1;
  const rows: DraftRow[] = [];
  const point = (label: string, agreed: string, draft: string, res: DraftRow["res"], src: string): void => {
    rows.push({ label, agreed, draft, res, src });
  };
  point(tr("Quantities"), productList(ctx), tr("same"), "ok", "p.2 §1");
  point(tr("Unit price"), priceLabel(f, A.price), priceLabel(f, A.price), "ok", "p.3 §2");
  point(tr("Term"), tr("{n} months", { n: A.term }), tr("{n} months", { n: A.term }), "ok", "p.2 §3");
  if (A.cap) point(tr("Uplift cap"), `${A.cap}%`, first ? "5%" : `${A.cap}%`, first ? "diff" : "ok", "p.9 §6.2");
  point(
    tr("Payment"),
    tr("quarterly in advance"),
    first ? tr("annual in advance, Net 30") : tr("quarterly in advance"),
    first ? "diff" : "ok",
    "p.5 §5.1",
  );
  point(tr("Liability cap"), tr("{n} months of fees", { n: A.liab }), tr("{n} months of fees", { n: A.liab }), "ok", "p.27 §17.2");
  point(tr("Governing law"), ctx.detail.terms.find((t) => t.label === "Governing law")?.value ?? "—", tr("same as previous contract"), "ok", "p.32 §22");
  point(
    tr("Annex — SLA schedule"),
    tr("to be checked"),
    first ? tr("not read: scanned page unreadable") : tr("read; matches 99.9% uptime"),
    first ? "unread" : "ok",
    "Annex B",
  );
  return rows;
}

function vendorOffer(ctx: CaseContext, vendor: string, by: string): AltOffer {
  const f = figures(ctx);
  const template = VENDOR_OFFERS.find((o) => o.name === vendor) ?? VENDOR_OFFERS[0];
  if (!template) throw new Error("No scripted vendor offers");
  const unit = Math.round(f.p0 * template.price);
  const total = f.p0 < 1000 ? f.qty * unit * f.mult : unit;
  return { price: f.p0 < 1000 ? unit : total, total, term: template.term, cover: template.cover, mig: template.mig, gap: template.gap, by };
}

export const scriptedServices: CaseServices = {
  sampleReply,
  counterPrice: (ctx, term) => counterOffer(figures(ctx), term),
  analyzeReply,
  verifyDraft,
  exampleVendors: () => VENDOR_OFFERS.map((o) => o.name),
  vendorOffer,
  startExtraction: () => ({
    stage: 0,
    weak: [
      { n: "uplift", name: msg("Price increase cap"), val: "3% (years 2–3)", conf: 76, state: "open" },
      { n: "pay", name: msg("Payment terms"), val: "Quarterly in advance, Net 30", conf: 78, state: "open" },
    ],
    facts: { total: 41, aboveThreshold: 39 },
  }),
  // Demo only: a real service must not provide this (Raffa never invents addresses).
  exampleAddress: (email, ctx) =>
    email.kind === "rfq"
      ? `rfp@${(email.vendor ?? "vendor").toLowerCase().replace(/\s+/g, "")}.example`
      : `anna.meyer@${ctx.contract.supplier.toLowerCase().replace(/\s+/g, "")}.example`,
};
