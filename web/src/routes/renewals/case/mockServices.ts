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
import { B, pick } from "./text";
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
  const S = pick(ctx.lang);
  const f = figures(ctx);
  const round = c.reps[rid]?.round ?? 1;
  const cap = concededCap(ctx);
  const hasUplift = parseFloat(ctx.contract.uplift) > 0;
  const rows: ReplyRow[] = [];
  const terms: ReplyAnalysis["terms"] = [];
  const target =
    c.goals.target === "p5" ? S("−5% target", "target −5%") : c.goals.target === "p10" ? S("−10% target", "target −10%") : c.goals.target === "budget" ? S("budget target", "target di budget") : S("no target set", "nessun target");
  const row = (label: string, said: string, plan: string, state: ReplyRow["state"], warn = ""): void => {
    rows.push({ label, said, plan, state, warn });
  };
  const term = (key: string, label: ReturnType<typeof B>, state: ReplyAnalysis["terms"][number]["state"], val: string | null): void => {
    terms.push({ key, label, state, val });
  };
  const policyActive = c.goals.policy;

  if (round === 1) {
    const price = firstOffer(f);
    const pct = Math.round(((f.p0 - price) / f.p0) * 100);
    row(S("Unit price", "Prezzo unitario"), `${priceLabel(f, price)} (${pct ? `−${pct}%` : "="})${S(" — only with 24 months", " — solo con 24 mesi")}`, target, "proposed");
    term("price", B("Unit price", "Prezzo unitario"), "proposed", priceLabel(f, price));
    row(
      S("Term", "Durata"),
      S("24 months required", "24 mesi richiesti"),
      c.goals.term === "custom" ? (c.goals.termText ?? "") : `${c.goals.term}${S(" months", " mesi")}`,
      String(c.goals.term) === "24" ? "proposed" : "conflict",
    );
    term("term", B("Term", "Durata"), "proposed", "24");
    if (hasUplift) {
      row(S("Uplift", "Aumento annuo"), S(`cap at ${cap}%`, `tetto al ${cap}%`), c.terms.uplift ? S("cap at 3% or freeze", "tetto al 3% o blocco") : S("not in your plan", "non nel tuo piano"), "proposed");
      term("uplift", B("Uplift", "Aumento annuo"), "proposed", `${cap}%`);
    }
    row(S("Payment terms", "Pagamenti"), S("“may be flexible, subject to credit review”", "«forse flessibili, previa verifica di credito»"), c.terms.payment ? S("quarterly", "trimestrale") : S("not in your plan", "non nel tuo piano"), "unclear");
    term("payment", B("Payment terms", "Pagamenti"), "unclear", null);
    row(
      S("Liability cap", "Limite di responsabilità"),
      S("standard cap, not negotiable", "tetto standard, non negoziabile"),
      c.terms.liab ? S("24 months", "24 mesi") : S("not in your plan", "non nel tuo piano"),
      "rejected",
      policyActive && POLICY.liab ? S("Conflicts with policy (min. 24 months)", "In conflitto con la policy (min. 24 mesi)") : "",
    );
    term("liab", B("Liability cap", "Limite di responsabilità"), "rejected", "12");
  } else {
    const months = c.limits?.term ?? 24;
    const price = counterOffer(f, months);
    row(
      S("Unit price", "Prezzo unitario"),
      priceLabel(f, price),
      c.limits?.priceMax ? S(`your limit: ≤ ${priceLabel(f, c.limits.priceMax)}`, `tuo limite: ≤ ${priceLabel(f, c.limits.priceMax)}`) : target,
      "proposed",
    );
    term("price", B("Unit price", "Prezzo unitario"), "proposed", priceLabel(f, price));
    row(S("Term", "Durata"), `${months}${S(" months", " mesi")}`, `${S("your limit: ", "tuo limite: ")}${months}${S(" months", " mesi")}`, "proposed");
    term("term", B("Term", "Durata"), "proposed", String(months));
    if (hasUplift) {
      row(S("Uplift", "Aumento annuo"), S("capped at 3%", "tetto al 3%"), S("cap at 3%", "tetto al 3%"), "proposed");
      term("uplift", B("Uplift", "Aumento annuo"), "proposed", "3%");
    }
    row(S("Payment terms", "Pagamenti"), S("quarterly in advance", "trimestrale anticipato"), S("quarterly", "trimestrale"), "proposed");
    term("payment", B("Payment terms", "Pagamenti"), "proposed", "quarterly");
    row(
      S("Liability cap", "Limite di responsabilità"),
      S("18 months of fees (not 24)", "18 mesi di canone (non 24)"),
      S("24 months", "24 mesi"),
      "proposed",
      policyActive ? S("Conflicts with policy (min. 24 months) — needs authorization", "In conflitto con la policy (min. 24 mesi) — serve autorizzazione") : "",
    );
    term("liab", B("Liability cap", "Limite di responsabilità"), "proposed", "18");
  }
  return { rows, terms };
}

function verifyDraft(c: Readonly<Case>, ctx: CaseContext, version: number): DraftRow[] {
  const S = pick(ctx.lang);
  const f = figures(ctx);
  const A = c.agreed;
  if (!A) return [];
  const first = version === 1;
  const rows: DraftRow[] = [];
  const point = (label: string, agreed: string, draft: string, res: DraftRow["res"], src: string): void => {
    rows.push({ label, agreed, draft, res, src });
  };
  point(S("Quantities", "Quantità"), productList(ctx), S("same", "uguali"), "ok", "p.2 §1");
  point(S("Unit price", "Prezzo unitario"), priceLabel(f, A.price), priceLabel(f, A.price), "ok", "p.3 §2");
  point(S("Term", "Durata"), `${A.term}${S(" months", " mesi")}`, `${A.term}${S(" months", " mesi")}`, "ok", "p.2 §3");
  if (A.cap) point(S("Uplift cap", "Tetto aumento"), `${A.cap}%`, first ? "5%" : `${A.cap}%`, first ? "diff" : "ok", "p.9 §6.2");
  point(
    S("Payment", "Pagamenti"),
    S("quarterly in advance", "trimestrale anticipato"),
    first ? S("annual in advance, Net 30", "annuale anticipato, Net 30") : S("quarterly in advance", "trimestrale anticipato"),
    first ? "diff" : "ok",
    "p.5 §5.1",
  );
  point(S("Liability cap", "Limite di responsabilità"), `${A.liab}${S(" months of fees", " mesi di canone")}`, `${A.liab}${S(" months of fees", " mesi di canone")}`, "ok", "p.27 §17.2");
  point(S("Governing law", "Legge applicabile"), ctx.detail.terms.find((t) => t.label === "Governing law")?.value ?? "—", S("same as previous contract", "come contratto precedente"), "ok", "p.32 §22");
  point(
    S("Annex — SLA schedule", "Allegato — SLA"),
    S("to be checked", "da verificare"),
    first ? S("not read: scanned page unreadable", "non letto: pagina scansionata illeggibile") : S("read; matches 99.9% uptime", "letto; coerente con uptime 99,9%"),
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
      { n: "uplift", name: B("Price increase cap", "Tetto aumento prezzo"), val: "3% (years 2–3)", conf: 76, state: "open" },
      { n: "pay", name: B("Payment terms", "Pagamenti"), val: "Quarterly in advance, Net 30", conf: 78, state: "open" },
    ],
    facts: { total: 41, aboveThreshold: 39 },
  }),
  // Demo only: a real service must not provide this (Raffa never invents addresses).
  exampleAddress: (email, ctx) =>
    email.kind === "rfq"
      ? `rfp@${(email.vendor ?? "vendor").toLowerCase().replace(/\s+/g, "")}.example`
      : `anna.meyer@${ctx.contract.supplier.toLowerCase().replace(/\s+/g, "")}.example`,
};
