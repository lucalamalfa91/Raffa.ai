/**
 * Demo tenant data for the renewal case (Helvetia Foods AG). Fictional, taken from the design
 * prototype: the four contracts the scenario walks through. In the real app this comes from
 * Contract 360 (facts, products and clauses with page refs), not from here.
 */

import { scriptedServices } from "./mockServices";
import type { CaseContext, ContractDetail, ContractFacts } from "./types";
import type { Lang } from "./text";

export const DEMO_TODAY = "08 Sep 2026";
export const DEMO_COMPANY = "Helvetia Foods AG";
export const DEMO_APPROVER = "Luca Lamalfa";
export const DEMO_OWNER = "Marta Keller";
/** Job titles of the demo people (shown under their name on Today). */
export const DEMO_TITLES: Readonly<Record<string, string>> = { [DEMO_APPROVER]: "Head of Procurement", [DEMO_OWNER]: "Buyer" };
/** Everyone the demo tenant has; the Team tab and `?as=` draw from here. */
export const DEMO_PEOPLE: readonly string[] = [DEMO_APPROVER, DEMO_OWNER];

export const DEMO_CONTRACTS: Record<string, { facts: ContractFacts; detail: ContractDetail }> = {
  "1": {
    facts: { supplier: "Salesforce", name: "Sales Cloud Enterprise — MSA + Order Form", file: "Salesforce_MSA_2024.pdf", spend: 640000, start: "01 Mar 2024", end: "15 Jan 2027", cancel: "18 Oct 2026", cancelDays: 40, notice: 90, uplift: "7%", autoRenews: true, rationale: "The notice window closes in 40 days and the 7% uplift clause compounds at renewal. Both facts are validated and cited." },
    detail: {
      products: [
        { name: "Sales Cloud Enterprise", unit: "user / month", qty: 300, price: 156 },
        { name: "Sales Cloud CPQ", unit: "user / month", qty: 60, price: 75 },
        { name: "Premier Success Plan", unit: "flat / year", qty: 1, price: 24400 },
      ],
      push: [
        { type: "Price increase", norm: "Up to 7% or CPI, whichever is higher, at each renewal.", ask: "Cap at 3% or freeze for the renewed term.", src: "p.9 §6.2" },
        { type: "Auto-renewal", norm: "Renews for successive 12-month terms unless notice ≥90 days before term end.", ask: "Shorten notice to 30 days; remove auto-renewal.", src: "p.12 §8.4" },
      ],
      raise: [
        { type: "Limitation of liability", norm: "Capped at 12 months of fees; carve-outs for confidentiality and IP.", ask: "Raise to 24 months; add data-breach carve-out.", src: "p.27 §17.2" },
        { type: "Termination for convenience", norm: "Not permitted during the term.", ask: "Allow termination at each anniversary with 60 days notice.", src: "p.26 §16.1" },
        { type: "Payment terms", norm: "Annual in advance, Net 30.", ask: "Quarterly in advance — improves cash by ~CHF 480k.", src: "p.5 §5.1" },
        { type: "Service credits", norm: "99.9% uptime; credits capped at 10% of monthly fee.", ask: "Lift the cap to 25%; add chronic-failure exit.", src: "Sch. 2 SLA" },
      ],
      terms: [
        { label: "Annual spend", value: "CHF 640,000" },
        { label: "Term", value: "01 Mar 2024 → 15 Jan 2027" },
        { label: "Notice period", value: "90 days" },
        { label: "Uplift at renewal", value: "7% or CPI" },
        { label: "Payment terms", value: "Annual in advance · Net 30" },
        { label: "Governing law", value: "Switzerland" },
      ],
      obligationsSupplier: [
        { text: "Notify any price increase", when: "≥60 days before renewal — by 16 Nov 2026" },
        { text: "Maintain 99.9% monthly uptime", when: "every month" },
        { text: "Deliver SOC 2 Type II report", when: "on request, annually" },
        { text: "Return customer data after termination", when: "within 30 days" },
      ],
    },
  },
  "2": {
    facts: { supplier: "Microsoft", name: "Enterprise Agreement · M365 E5", file: "Microsoft_EA_Enrollment.pdf", spend: 1200000, start: "01 Jul 2024", end: "30 Jun 2027", cancel: "31 Mar 2027", cancelDays: 204, notice: 90, uplift: "5%", autoRenews: true, rationale: "Notice is due by 31 Mar 2027 and the true-up locks counts for 12 months. Usage data is not in the contract — add it to size the licence mix." },
    detail: {
      products: [
        { name: "Microsoft 365 E5", unit: "user / month", qty: 1200, price: 52 },
        { name: "Microsoft 365 E3", unit: "user / month", qty: 400, price: 33 },
        { name: "Power BI Pro", unit: "user / month", qty: 600, price: 9.5 },
        { name: "Azure consumption commit", unit: "flat / year", qty: 1, price: 224400 },
      ],
      push: [{ type: "Price protection", norm: "Prices fixed for the term; true-up counts cannot be reduced within a year.", ask: "Allow downward adjustment at each anniversary.", src: "p.9 §5" }],
      raise: [
        { type: "Limitation of liability", norm: "Amounts paid under the affected Enrollment in the preceding 12 months.", ask: "Raise to 24 months for data-protection claims.", src: "p.22 §14" },
        { type: "Licence reassignment", norm: "Reassignment allowed after 90 days.", ask: "Reduce to 30 days to enable faster right-sizing.", src: "p.11 §6" },
        { type: "Azure commit shortfall", norm: "Unused commit is forfeited at term end.", ask: "Carry forward up to 20% into the next term.", src: "MACC §3" },
      ],
      terms: [
        { label: "Annual spend", value: "CHF 1,200,000" },
        { label: "Term", value: "01 Jul 2024 → 30 Jun 2027" },
        { label: "Notice period", value: "90 days" },
        { label: "Uplift at renewal", value: "5%" },
        { label: "Governing law", value: "Ireland" },
      ],
      obligationsSupplier: [
        { text: "Hold prices for the enrollment term", when: "through 30 Jun 2027" },
        { text: "Meet 99.9% financially backed SLA", when: "every month" },
        { text: "Give 12 months notice of product retirement", when: "—" },
      ],
    },
  },
  "3": {
    facts: { supplier: "AWS", name: "Enterprise Discount Program", file: "AWS_EDP_Amendment_3.pdf", spend: 2400000, start: "01 Sep 2024", end: "31 Aug 2027", cancel: "01 Jun 2027", cancelDays: 266, notice: 90, uplift: "0%", autoRenews: false, rationale: "No auto-renewal: the contract ends on 31 Aug 2027 unless you renegotiate. Start before the 01 Jun 2027 window." },
    detail: {
      products: [
        { name: "EC2 + RDS committed usage", unit: "commit / year", qty: 1, price: 1800000 },
        { name: "S3 + data transfer", unit: "commit / year", qty: 1, price: 400000 },
        { name: "Enterprise Support", unit: "10% of usage", qty: 1, price: 200000 },
      ],
      push: [{ type: "Commit shortfall", norm: "Unused commit is invoiced at term end.", ask: "Allow a one-year carry-forward of up to 15%.", src: "p.6 §4" }],
      raise: [
        { type: "Limitation of liability", norm: "Greater of USD 500k or 12 months fees; carve-outs for confidentiality and IP.", ask: "Add a data-breach carve-out.", src: "p.19 §11.2" },
        { type: "Support fees", norm: "10% of monthly usage.", ask: "Tiered rate above CHF 1m.", src: "p.8 §7" },
      ],
      terms: [
        { label: "Annual spend", value: "CHF 2,400,000" },
        { label: "Term", value: "01 Sep 2024 → 31 Aug 2027" },
        { label: "Notice period", value: "90 days" },
        { label: "Uplift at renewal", value: "0%" },
        { label: "Governing law", value: "Luxembourg" },
      ],
      obligationsSupplier: [
        { text: "Apply the discount tier to monthly usage", when: "every month" },
        { text: "Quarterly TAM business review", when: "every quarter" },
        { text: "Give 12 months notice of service deprecation", when: "—" },
      ],
    },
  },
  "4": {
    facts: { supplier: "DocuSign", name: "eSignature Business Pro", file: "DocuSign_OrderForm.pdf", spend: 58000, start: "01 Nov 2023", end: "31 Oct 2026", cancel: "02 Oct 2026", cancelDays: 24, notice: 30, uplift: "8%", autoRenews: true, rationale: "The notice window closes in 24 days. Decide whether to renegotiate, give notice or replace." },
    detail: {
      products: [
        { name: "eSignature Business Pro", unit: "seat / month", qty: 60, price: 48 },
        { name: "Envelope allowance", unit: "12,000 / year", qty: 1, price: 18000 },
        { name: "Premier Support", unit: "flat / year", qty: 1, price: 5440 },
      ],
      push: [
        { type: "Auto-renewal", norm: "Renews for 12 months unless 30-day notice.", ask: "Give notice by 02 Oct 2026; re-sign on the smaller plan.", src: "p.2 §4" },
        { type: "Price increase", norm: "Up to 8% at renewal.", ask: "Freeze for the renewed term.", src: "p.2 §5" },
      ],
      raise: [{ type: "Unused envelopes", norm: "Non-refundable; do not roll over.", ask: "Roll over up to 25% into the next year.", src: "p.2 §3" }],
      terms: [
        { label: "Annual spend", value: "CHF 58,000" },
        { label: "Term", value: "01 Nov 2023 → 31 Oct 2026" },
        { label: "Notice period", value: "30 days" },
        { label: "Uplift at renewal", value: "8%" },
        { label: "Governing law", value: "Ireland" },
      ],
      obligationsSupplier: [
        { text: "Maintain 99.9% uptime", when: "every month" },
        { text: "Provide monthly envelope usage report", when: "every month" },
      ],
    },
  },
};

/** A ready-to-use context for a demo contract (the mock store and the tests use this). */
export function demoContext(cid: keyof typeof DEMO_CONTRACTS | string, viewer: string, lang: Lang = "en"): CaseContext {
  const entry = DEMO_CONTRACTS[cid];
  if (!entry) throw new Error(`Unknown demo contract: ${cid}`);
  return { lang, viewer, today: DEMO_TODAY, company: DEMO_COMPANY, contract: entry.facts, detail: entry.detail, services: scriptedServices };
}
