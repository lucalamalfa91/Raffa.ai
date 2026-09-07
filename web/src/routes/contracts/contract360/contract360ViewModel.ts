import type {
  Contract360Body,
  Contract360ClauseBody,
  Contract360DocumentBody,
  Contract360HeaderBody,
  Contract360ObligationBody,
  Contract360ProductBody,
  Contract360RiskBody,
  RenewalPipelineItemBody,
  RenewalPriorityBody,
} from "../../../api/client";
import { getConfidenceTag, type SemanticTag } from "../../../styles/semantics";
import { daysUntil } from "../portfolioAttention";
import { formatDateOnly } from "../portfolioTableFormatters";

/**
 * Pure view-model helpers for the Contract 360 screen (route `/contracts/:contractId`, ADR-018;
 * ADR-020 screen 5 "header + 10 tabs"; task E07/F02/US01/T01, us-01-contract-360 AC-1/AC-2/AC-3/
 * AC-4). Same one-concern-per-file split `../portfolioAttention.ts`/`../portfolioTableFormatters.ts`
 * already established for this folder: no React here, so every rule below is unit-testable without
 * rendering anything.
 *
 * **Facts vs AI (ADR-019, council decision "AI recommendation lives in its own labelled block,
 * never mixed with deterministic facts")**: this module keeps that separation at the *type* level,
 * not just in markup -- `buildRecommendation` returns a `Recommendation` (statement/rationale/
 * drivers, sourced from the Renewals module's own derived text) that is never merged into a
 * `FactRow[]` array, and every `FactRow`-returning function below only ever reads extracted/
 * contract-level facts, never recommendation text. `Contract360Route`/`OverviewTab` render the two
 * in visually distinct blocks (`.ai-recommendation` vs `.table`) but the separation is real here
 * first -- a caller cannot accidentally concatenate the two into one list even if it tried, because
 * they are different, non-overlapping shapes.
 */

/** Exact tab order -- ADR-020 screen 5 / day1-demo.html's own `tabNames` array (`kt`-driven `.table` template for every tab except Overview/Benchmark/Renewal, which each add their own extra block). */
export const CONTRACT_360_TABS = [
  "Overview",
  "Commercials",
  "Products",
  "Clauses",
  "Obligations",
  "Risks",
  "Documents",
  "Benchmark",
  "Renewal",
  "Activity",
] as const;

export type Contract360TabName = (typeof CONTRACT_360_TABS)[number];

/**
 * Task E07/F04/US01/T01 (ask-contigo-ui): a citation chip on the Ask Contigo screen
 * (`../../ask/`) navigates here with `{ state: { tab: "Clauses" } }` so "opening Contract 360 >
 * Clauses" (AC-2) actually lands on that tab instead of always resetting to Overview. `index.tsx`
 * reads `useLocation().state?.tab` through this guard rather than trusting an arbitrary string --
 * an unrecognised or absent value falls back to Overview, the same default this screen already had
 * before that task existed.
 */
export function isContract360TabName(value: unknown): value is Contract360TabName {
  return typeof value === "string" && (CONTRACT_360_TABS as readonly string[]).includes(value);
}

/**
 * One row of the shared Term / Value / Source / Confidence table template (screens.md #5: "one
 * template ... .table (Term · Value · Source · Confidence pattern)"), used by every tab except
 * Overview (the recommendation/attention/risks screen) and the non-generic half of Renewal (the
 * priority-score component table, `PriorityComponentRow` below).
 */
export interface FactRow {
  key: string;
  term: string;
  value: string;
  /** `null` renders as "—" -- a contract-level field (e.g. Commercials/Renewal) has no source span. */
  source: string | null;
  /** 0-100, or `null` when this fact carries no extraction confidence at all. Never a 0-1 fraction -- see `toConfidencePercent`. */
  confidencePct: number | null;
}

function formatMoney(amount: number | null, currency: string): string {
  if (amount === null) return "—";
  return `${currency} ${new Intl.NumberFormat("en-GB").format(amount)}`;
}

function formatPlainNumber(value: number | null): string {
  if (value === null) return "—";
  return new Intl.NumberFormat("en-GB").format(value);
}

/**
 * `Contract360ProductBody.confidence`/`Contract360ClauseBody.confidence`/etc. are 0-1 fractions on
 * the wire (backend `double? Confidence`, e.g. `0.92`) -- `styles/semantics.ts#getConfidenceTag`
 * expects a 0-100 percentage (its own thresholds are `>95`, `>=80`). `null` in, `null` out.
 */
export function toConfidencePercent(confidence: number | null): number | null {
  return confidence === null ? null : confidence * 100;
}

interface SourceFields {
  sourceDocumentId: string | null;
  sourceSpan: string | null;
  sourcePage: number | null;
}

/** `FactRow.source` for any extracted row (Products/Clauses/Obligations/Risks). `null` when there is no linked source document at all -- distinct from "linked, but no page/span recorded" (`"Linked document"`), an honest "not yet available" rather than a fabricated citation. */
function formatSource(row: SourceFields): string | null {
  if (row.sourceDocumentId === null) return null;
  const parts: string[] = [];
  if (row.sourcePage !== null) parts.push(`p.${row.sourcePage}`);
  if (row.sourceSpan !== null && row.sourceSpan.trim() !== "") parts.push(row.sourceSpan);
  return parts.length > 0 ? parts.join(" · ") : "Linked document";
}

/**
 * Overview tab's own supplementary "Contract details" block. `Contract360Overview`'s own backend
 * doc comment names these as "the descriptive/administrative Contract fields not already surfaced
 * on Contract360Header ... an implementer judgment call (spec §8.2 names the tab, not its fields)".
 * The cited prototype's Overview screen never renders these fields (its own `keyTerms` array is a
 * curated, unrelated "Needs your attention" source -- see `computeNeedsAttention` below) because
 * `governingLaw`/`effectiveDate`/`version`/`createdAt` have no other tab home in the whole 10-tab
 * set (not Commercials, not Renewal), so dropping them entirely would silently discard real
 * extracted/contract-level data rather than leaving a named gap. Rendered as a plain `.table` below
 * the recommendation card, never inside `.ai-recommendation` (ADR-019 facts/AI separation).
 */
export function buildOverviewDetailRows(overview: Contract360Body["tabs"]["overview"]): FactRow[] {
  const rows: FactRow[] = [
    { key: "effectiveDate", term: "Effective date", value: formatDateOnly(overview.effectiveDate), source: null, confidencePct: null },
    { key: "governingLaw", term: "Governing law", value: overview.governingLaw ?? "—", source: null, confidencePct: null },
    { key: "version", term: "Version", value: `v${overview.version}`, source: null, confidencePct: null },
  ];
  if (overview.parentContractId !== null) {
    rows.push({ key: "parentContractId", term: "Parent contract", value: overview.parentContractId, source: null, confidencePct: null });
  }
  return rows;
}

/** Commercials tab (screens.md #5). Contract-level aggregate fields -- no per-field source/confidence exists for any of them (Contract360Commercials carries none), unlike Products/Clauses/Obligations/Risks. */
export function buildCommercialsRows(commercials: Contract360Body["tabs"]["commercials"]): FactRow[] {
  return [
    { key: "annualSpend", term: "Annual spend", value: formatMoney(commercials.annualSpend, commercials.currency), source: null, confidencePct: null },
    { key: "totalContractValue", term: "Total contract value (TCV)", value: formatMoney(commercials.totalContractValue, commercials.currency), source: null, confidencePct: null },
    { key: "paymentTerms", term: "Payment terms", value: commercials.paymentTerms ?? "—", source: null, confidencePct: null },
    { key: "autoRenewal", term: "Auto-renewal", value: commercials.autoRenewal ? "Yes" : "No", source: null, confidencePct: null },
    {
      key: "renewalTermMonths",
      term: "Renewal term",
      value: commercials.renewalTermMonths !== null ? `${commercials.renewalTermMonths} months` : "—",
      source: null,
      confidencePct: null,
    },
    { key: "lineItemCount", term: "Line items", value: formatPlainNumber(commercials.lineItemCount), source: null, confidencePct: null },
    {
      key: "lineItemAnnualCostTotal",
      term: "Line items — annual cost",
      value: formatMoney(commercials.lineItemAnnualCostTotal, commercials.currency),
      source: null,
      confidencePct: null,
    },
    {
      key: "lineItemTotalCostTotal",
      term: "Line items — total cost",
      value: formatMoney(commercials.lineItemTotalCostTotal, commercials.currency),
      source: null,
      confidencePct: null,
    },
  ];
}

function formatProductValue(p: Contract360ProductBody): string {
  const parts: string[] = [];
  if (p.quantity !== null) parts.push(`${formatPlainNumber(p.quantity)}${p.unit ? ` ${p.unit}` : ""}`);
  if (p.unitPrice !== null) parts.push(`@ ${formatPlainNumber(p.unitPrice)}`);
  if (p.discount !== null) parts.push(`${formatPlainNumber(p.discount)}% disc.`);
  if (p.annualCost !== null) parts.push(`→ ${formatPlainNumber(p.annualCost)}/yr`);
  return parts.length > 0 ? parts.join(" ") : "—";
}

/** Products tab (AC-2 "products read from ... line items", Contract360ProductLineItem). */
export function buildProductsRows(products: readonly Contract360ProductBody[]): FactRow[] {
  return products.map((p) => ({
    key: p.lineItemId,
    term: p.description !== "" ? p.description : (p.sku ?? "Line item"),
    value: formatProductValue(p),
    source: formatSource(p),
    confidencePct: toConfidencePercent(p.confidence),
  }));
}

function formatClauseValue(c: Contract360ClauseBody): string {
  const base = c.normalizedValue ?? c.rawText;
  return c.riskLevel !== null ? `${base} — ${c.riskLevel} risk` : base;
}

/** Clauses tab (AC-2 "clauses ... from extracted facts", Contract360Clause). */
export function buildClausesRows(clauses: readonly Contract360ClauseBody[]): FactRow[] {
  return clauses.map((c) => ({
    key: c.clauseId,
    term: c.clauseType,
    value: formatClauseValue(c),
    source: formatSource(c),
    confidencePct: toConfidencePercent(c.confidence),
  }));
}

function formatObligationValue(o: Contract360ObligationBody): string {
  const parts = [o.description];
  if (o.dueDate !== null) parts.push(`due ${formatDateOnly(o.dueDate)}`);
  if (o.criticality !== null) parts.push(o.criticality);
  return parts.join(" · ");
}

/** Obligations tab (AC-2 "obligations ... from extracted facts", Contract360Obligation). */
export function buildObligationsRows(obligations: readonly Contract360ObligationBody[]): FactRow[] {
  return obligations.map((o) => ({
    key: o.obligationId,
    term: o.obligationType,
    value: formatObligationValue(o),
    source: formatSource(o),
    confidencePct: toConfidencePercent(o.confidence),
  }));
}

/** Risks tab (AC-2 "risks ... from extracted facts", Contract360Risk). Severity is always textual (ADR-019 "no colour-only semantics"), never rendered as a bare tint. */
export function buildRisksRows(risks: readonly Contract360RiskBody[]): FactRow[] {
  return risks.map((r) => ({
    key: r.riskId,
    term: r.riskType,
    value: `${r.description} (${r.severity} risk)`,
    source: formatSource(r),
    confidencePct: toConfidencePercent(r.confidence),
  }));
}

/**
 * Documents tab. Kept inside the same generic template as every other fact tab (ADR-020's "one
 * template" for Commercials..Activity) rather than forking into a second, differently-shaped table
 * -- `source` is the document's own MIME type (there is no span/page for a document *about itself*)
 * and `confidencePct` is always `null` (a document has no extraction confidence of its own).
 */
export function buildDocumentsRows(documents: readonly Contract360DocumentBody[]): FactRow[] {
  return documents.map((d) => ({
    key: d.documentId,
    term: d.fileName,
    value: `${d.documentType} · ${d.processingStatus}`,
    source: d.mimeType,
    confidencePct: null,
  }));
}

/** Renewal tab's generic facts half (screens.md #5 "Renewal adds priority-score component table" -- this is the *other*, template-shaped half of that same tab). Contract360Renewal carries no per-field source/confidence, same as Commercials. */
export function buildRenewalFactRows(renewal: Contract360Body["tabs"]["renewal"]): FactRow[] {
  return [
    { key: "endDate", term: "End date", value: formatDateOnly(renewal.endDate), source: null, confidencePct: null },
    { key: "renewalDate", term: "Renewal date", value: formatDateOnly(renewal.renewalDate), source: null, confidencePct: null },
    {
      key: "cancellationDeadline",
      term: "Cancellation deadline",
      value: formatDateOnly(renewal.cancellationDeadline),
      source: null,
      confidencePct: null,
    },
    { key: "autoRenewal", term: "Auto-renewal", value: renewal.autoRenewal ? "Yes" : "No", source: null, confidencePct: null },
    {
      key: "renewalTermMonths",
      term: "Renewal term",
      value: renewal.renewalTermMonths !== null ? `${renewal.renewalTermMonths} months` : "—",
      source: null,
      confidencePct: null,
    },
  ];
}

export interface PriorityComponentRow {
  key: string;
  label: string;
  score: number;
  explanation: string;
}

/** Order + labels quoted from `PriorityScoreCalculator`'s own product-spec §9.2 term order ("Spend Weight + Time Urgency + Benchmark Opportunity + Price Increase Risk + Contract Risk"). */
const PRIORITY_COMPONENT_LABELS: ReadonlyArray<{ key: keyof RenewalPriorityBody["components"]; label: string }> = [
  { key: "spendWeight", label: "Spend weight" },
  { key: "timeUrgency", label: "Time urgency" },
  { key: "benchmarkOpportunity", label: "Benchmark opportunity" },
  { key: "priceIncreaseRisk", label: "Price-increase risk" },
  { key: "contractRisk", label: "Contract risk" },
];

/** Renewal tab's priority-score component table (screens.md #5). `null` (the priority fetch failed/is still loading) renders as an empty list -- `RenewalTab.tsx` shows its own honest "not yet available" note in that case, never a fabricated all-zero table. */
export function buildPriorityComponentRows(priority: RenewalPriorityBody | null): PriorityComponentRow[] {
  if (priority === null) return [];
  return PRIORITY_COMPONENT_LABELS.map(({ key, label }) => ({
    key,
    label,
    score: priority.components[key].score,
    explanation: priority.components[key].explanation,
  }));
}

/** Contract 360 header's "priority {score}/100" fact (screens.md #5 fact-row cell 6, `cur.score`). */
export function formatPriorityFact(priority: RenewalPriorityBody | null): string {
  return priority !== null ? `priority ${Math.round(priority.totalScore)}/100` : "priority not yet available";
}

export interface AttentionTerm {
  key: string;
  term: string;
  confidencePct: number;
  tag: SemanticTag;
}

/**
 * Overview tab's "Needs your attention" (screens.md #5; day1-demo.html's own `attnTerms:
 * keyTerms.filter(k=>k.tag!=='tag-neutral').slice(0,3)`, i.e. every key term whose confidence is
 * NOT the >95% "Accepted" band, lowest confidence first, capped at 3). The prototype's `keyTerms` is
 * a small illustrative list; this applies the same rule to every REAL per-field confidence this
 * aggregate carries -- Products, Clauses, and Obligations (Commercials/Overview/Renewal have no
 * per-field confidence: they are contract-level aggregates, not span-extractions; Risks gets its
 * own "Top risks" section below instead of competing for the same 3 slots). A row with `confidence:
 * null` is excluded rather than treated as "needs attention" -- there is no number to grade it
 * against (an honest omission, not a downgrade to "accepted").
 */
export function computeNeedsAttention(tabs: Contract360Body["tabs"]): AttentionTerm[] {
  const candidates: AttentionTerm[] = [];

  const consider = (key: string, term: string, confidence: number | null) => {
    if (confidence === null) return;
    const confidencePct = toConfidencePercent(confidence) as number;
    const tag = getConfidenceTag(confidencePct);
    if (tag.variant !== "neutral") candidates.push({ key, term, confidencePct, tag });
  };

  for (const p of tabs.products) consider(`product-${p.lineItemId}`, p.description, p.confidence);
  for (const c of tabs.clauses) consider(`clause-${c.clauseId}`, c.clauseType, c.confidence);
  for (const o of tabs.obligations) consider(`obligation-${o.obligationId}`, o.obligationType, o.confidence);

  return candidates.sort((a, b) => a.confidencePct - b.confidencePct).slice(0, 3);
}

const RISK_SEVERITY_RANK: Readonly<Record<string, number>> = { Critical: 3, High: 2, Medium: 1, Low: 0 };

/**
 * Overview tab's "Top risks" (screens.md #5; day1-demo.html's own `topRisks: curRisks.slice(0,2)`).
 * Highest severity first; ties broken by ascending confidence (the least-certain highest-severity
 * risk surfaces first, since that is the one most worth a human's attention).
 */
export function computeTopRisks(risks: readonly Contract360RiskBody[]): Contract360RiskBody[] {
  return [...risks]
    .sort((a, b) => {
      const bySeverity = (RISK_SEVERITY_RANK[b.severity] ?? 0) - (RISK_SEVERITY_RANK[a.severity] ?? 0);
      if (bySeverity !== 0) return bySeverity;
      return (a.confidence ?? 1) - (b.confidence ?? 1);
    })
    .slice(0, 2);
}

export interface RecommendationDriver {
  key: string;
  label: string;
  value: string;
}

export interface Recommendation {
  /** False when this contract has no entry in `GET /api/renewals` (not auto-renewing, or beyond that endpoint's own page-size ceiling) -- a real, named gap, never a fabricated recommendation. */
  hasRecommendation: boolean;
  statement: string;
  rationale: string;
  drivers: readonly RecommendationDriver[];
}

/**
 * Overview's recommended-action block (screens.md #5: "recommended-action block (big statement +
 * rationale + Open in renewals / Why this score) with 3 driver numbers"; council decision "AI
 * recommendation lives in its own labelled block, never mixed with deterministic facts", carried
 * into us-01-contract-360). `statement`/`rationale` are the Renewals module's own real, deterministic
 * (not LLM) `recommendedAction`/`explanation` text for whichever `GET /api/renewals` pipeline item
 * matches this contract's id -- never invented UI copy (see `GetRenewalsResult`'s own doc comment in
 * `api/client.ts` for why that call, not this contract's own header/tabs, is the only honest
 * source). The 3 "driver numbers" are quoted verbatim from day1-demo.html's own `cur.cancelDays`/
 * `cur.market`/`cur.potential` block: Cancellation deadline is a real header fact (shown regardless
 * of whether a recommendation exists); Market position/Potential savings are Recommendations-module
 * fields that are honestly `null` until the R3 Benchmark/Savings modules exist
 * (`RenewalInsightRecommendations`'s own backend doc comment) -- rendered as "Not yet available",
 * never guessed (Appendix C rule 10).
 */
export function buildRecommendation(
  header: Contract360HeaderBody,
  renewals: readonly RenewalPipelineItemBody[],
  now: Date = new Date(),
): Recommendation {
  const item = renewals.find((r) => r.contractId === header.contractId) ?? null;
  const cancelDays = daysUntil(header.cancellationDeadline, now);

  const drivers: RecommendationDriver[] = [
    { key: "cancellationDeadline", label: "Cancellation deadline", value: cancelDays !== null ? `${cancelDays} days` : "—" },
    { key: "marketPosition", label: "Market position", value: item?.insightCard.recommendations.marketPosition ?? "Not yet available" },
    {
      key: "potentialSavings",
      label: "Potential savings",
      value: item?.insightCard.recommendations.potentialSavingsRange ?? "Not yet available",
    },
  ];

  if (item === null) {
    return {
      hasRecommendation: false,
      statement: "No renewal recommendation for this contract",
      rationale: header.autoRenewal
        ? "This contract did not appear in the current renewal pipeline yet."
        : "Contigo tracks renewal recommendations only for auto-renewing contracts; this contract ends on its end date with no renewal to act on.",
      drivers,
    };
  }

  return {
    hasRecommendation: true,
    statement: item.action,
    rationale: item.insightCard.recommendations.explanation,
    drivers,
  };
}
