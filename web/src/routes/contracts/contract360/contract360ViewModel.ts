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
import { getConfidenceTag, isDeadlineCritical, type SemanticTag } from "../../../styles/semantics";
import { daysUntil } from "../portfolioAttention";
import { formatDateOnly, formatSupplier, getContractTypeLabel, getPortfolioStatusTag } from "../portfolioTableFormatters";

/**
 * Pure view-model helpers for the V2 Contract 360 screen (route `/contracts/:contractId`; ADR-024
 * V2 IA; screens-v2.md #5 "Contract 360 — no tabs"; `raffa-v2/markup.html` "CONTRACT 360 — three
 * answers, then proof, then details" block; `app.jsx` `cur` / `clauses` / `otherRows` / `steps360`).
 * No React here, so every rule below is unit-testable without rendering anything
 * (`contract360ViewModel.test.ts`).
 *
 * **Facts vs AI (ADR-019).** The "What to do" answer is the Renewals module's own deterministic
 * recommendation text (`buildRecommendation`), never merged into a `FactRow[]`; every `FactRow`-
 * returning function below only ever reads extracted or contract-level facts. The two shapes share
 * no keys, so a caller cannot concatenate them by accident.
 *
 * The Day-1 ten-tab layout (`CONTRACT_360_TABS`, `isContract360TabName`) is gone with V2: the
 * screen is one page -- header, answers band, "Why — the clauses behind it", then a "Details ▾"
 * drawer holding what the tabs used to hold.
 */

// ---------------------------------------------------------------------------------------------
// Header
// ---------------------------------------------------------------------------------------------

export interface BackLink {
  label: string;
  href: string;
}

/**
 * screens-v2.md #5: "Back label follows the origin (Ask Raffa / Documents / Portfolio /
 * Renewals)" -- plus Savings, whose rows open this screen (`app.jsx` `back:'home'`). Read from
 * `location.state.from`; an absent or unrecognised origin resolves to `null` and the header falls
 * back to a plain "← Back" (`app.jsx`: `backLabels[s.back]||'Back'`).
 */
const BACK_LINKS: Readonly<Record<string, BackLink>> = {
  ask: { label: "Ask Raffa", href: "/ask" },
  documents: { label: "Documents", href: "/documents" },
  portfolio: { label: "Portfolio", href: "/contracts" },
  renewals: { label: "Renewals", href: "/renewals" },
  savings: { label: "Savings", href: "/savings" },
};

export function resolveBackLink(from: unknown): BackLink | null {
  if (typeof from !== "string") return null;
  const known = BACK_LINKS as Record<string, BackLink | undefined>;
  return known[from] ?? null;
}

export interface SupplierLabel {
  label: string;
  title: string | undefined;
}

/**
 * R-SUP-04 / ADR-024 "Supplier identity": the header kicker is the wire's resolved `supplierName`,
 * never a guid. `null` (no supplier, or an id that no longer resolves for this tenant) and a blank
 * name fall back to the same id-fragment label the Portfolio table renders -- never a fabricated
 * supplier.
 */
export function resolveSupplierLabel(header: Contract360HeaderBody): SupplierLabel {
  if (header.supplierName !== null && header.supplierName.trim() !== "") {
    return { label: header.supplierName, title: header.supplierId ?? undefined };
  }
  return formatSupplier(header.supplierId);
}

function formatMoney(amount: number | null, currency: string): string {
  if (amount === null) return "—";
  return `${currency} ${new Intl.NumberFormat("en-GB").format(amount)}`;
}

function formatPlainNumber(value: number | null): string {
  if (value === null) return "—";
  return new Intl.NumberFormat("en-GB").format(value);
}

/** `markup.html`: "{{ cur.type }} · {{ cur.spendFmt }} / year · {{ cur.docCount }} documents · {{ cur.status }}". */
export function formatHeaderMeta(header: Contract360HeaderBody, currency: string, docCount: number): string {
  const spend = header.annualSpend === null ? "spend not recorded" : `${formatMoney(header.annualSpend, currency)} / year`;
  return `${getContractTypeLabel(header.type)} · ${spend} · ${docCount} document${docCount === 1 ? "" : "s"} · ${getPortfolioStatusTag(header.status).label}`;
}

// ---------------------------------------------------------------------------------------------
// Answers band: Where you can save · When you must move · What to do
// ---------------------------------------------------------------------------------------------

export interface Recommendation {
  /** False when this contract has no entry in `GET /api/renewals` (not auto-renewing, or beyond that endpoint's own page-size ceiling) -- a real, named gap, never a fabricated recommendation. */
  hasRecommendation: boolean;
  statement: string;
  rationale: string;
}

/**
 * "What to do": the Renewals module's own real, deterministic `recommendedAction` / `explanation`
 * for whichever `GET /api/renewals` pipeline item matches this contract -- never invented UI copy.
 */
export function buildRecommendation(header: Contract360HeaderBody, renewals: readonly RenewalPipelineItemBody[]): Recommendation {
  const item = renewals.find((r) => r.contractId === header.contractId) ?? null;

  if (item === null) {
    return {
      hasRecommendation: false,
      statement: "No renewal recommendation for this contract",
      rationale: header.autoRenewal
        ? "This contract did not appear in the current renewal pipeline yet."
        : "Raffa tracks renewal recommendations only for auto-renewing contracts; this contract ends on its end date with no renewal to act on.",
    };
  }

  return { hasRecommendation: true, statement: item.action, rationale: item.insightCard.recommendations.explanation };
}

export interface SaveAnswer {
  /** `potentialSavingsRange` from the pipeline item, or an honest "Not yet available". */
  estimate: string;
  /** The lever sentence under it -- market position / uplift when known, else why it is not. */
  lever: string;
}

export interface MoveAnswer {
  /** The notice deadline as a date, or "Not determined". */
  deadline: string;
  cancelDays: number | null;
  /** Inside the locked 45-day window (`styles/semantics.ts#isDeadlineCritical`). */
  isUrgent: boolean;
  /** `markup.html`: "in **N days** — last day to give notice. Term ends {end}{ and auto-renews for N months}." */
  detail: string;
}

export interface AnswersBand {
  save: SaveAnswer;
  move: MoveAnswer;
  act: Recommendation;
}

export const SAVINGS_NOT_YET_AVAILABLE = "Not yet available";
export const LEVER_NOT_YET_AVAILABLE =
  "Market position and savings estimate light up with the Benchmark Service — the notice deadline and clauses below are already validated.";

/**
 * The three answers (`markup.html` "CONTRACT 360 — three answers"). Every figure is either a real
 * header/pipeline field or an honest "not yet" -- the prototype's own `cur.saving`/`cur.lever` are
 * benchmark outputs the R3 Benchmark Service does not produce yet (`RenewalInsightRecommendations`'
 * backend doc comment), so they render as such rather than as a guess (Appendix C rule 10).
 */
export function buildAnswers(
  header: Contract360HeaderBody,
  renewal: Contract360Body["tabs"]["renewal"],
  renewals: readonly RenewalPipelineItemBody[],
  now: Date = new Date(),
): AnswersBand {
  const item = renewals.find((r) => r.contractId === header.contractId) ?? null;
  const recommendations = item?.insightCard.recommendations ?? null;

  const upliftLever =
    recommendations?.annualUpliftPercent !== null && recommendations?.annualUpliftPercent !== undefined
      ? `A ${recommendations.annualUpliftPercent}% uplift clause applies at renewal.`
      : null;
  const save: SaveAnswer = {
    estimate: recommendations?.potentialSavingsRange ?? SAVINGS_NOT_YET_AVAILABLE,
    lever: recommendations?.marketPosition ?? upliftLever ?? LEVER_NOT_YET_AVAILABLE,
  };

  const cancelDays = daysUntil(header.cancellationDeadline, now);
  const termEnd = header.endDate !== null ? `Term ends ${formatDateOnly(header.endDate)}` : "Term end not recorded";
  const autoText =
    header.autoRenewal && renewal.renewalTermMonths !== null
      ? ` and auto-renews for ${renewal.renewalTermMonths} months`
      : header.autoRenewal
        ? " and auto-renews"
        : "";
  let detail: string;
  if (cancelDays === null) detail = `No notice deadline determined. ${termEnd}${autoText}.`;
  else if (cancelDays < 0) detail = `${Math.abs(cancelDays)} day${cancelDays === -1 ? "" : "s"} ago — the notice window has closed. ${termEnd}${autoText}.`;
  else detail = `in ${cancelDays} day${cancelDays === 1 ? "" : "s"} — last day to give notice. ${termEnd}${autoText}.`;

  const move: MoveAnswer = {
    deadline: header.cancellationDeadline !== null ? formatDateOnly(header.cancellationDeadline) : "Not determined",
    cancelDays,
    isUrgent: cancelDays !== null && isDeadlineCritical(cancelDays),
    detail,
  };

  return { save, move, act: buildRecommendation(header, renewals) };
}

// ---------------------------------------------------------------------------------------------
// Negotiation tracker (after "Start negotiation")
// ---------------------------------------------------------------------------------------------

export interface NegotiationStep {
  label: string;
  due: string;
}

/** `app.jsx` `stepDefs`, with the real supplier and deadline: 4 steps, due "this week" · "+10 days" · "+20 days" · "by {cancel}". */
export function buildNegotiationSteps(supplierLabel: string, deadlineLabel: string): NegotiationStep[] {
  return [
    { label: `Notify ${supplierLabel} of intent to renegotiate`, due: "this week" },
    { label: "Request revised pricing and licence mix", due: "+10 days" },
    { label: "Counter with the market benchmark", due: "+20 days" },
    { label: "Sign, or send non-renewal notice", due: `by ${deadlineLabel}` },
  ];
}

/** `markup.html`: "target {{ cur.saving }} · close by {{ cur.cancel }}". */
export function formatTrackerMeta(save: SaveAnswer, move: MoveAnswer): string {
  return `target ${save.estimate} · close by ${move.deadline}`;
}

// ---------------------------------------------------------------------------------------------
// Why — the clauses behind it
// ---------------------------------------------------------------------------------------------

export interface ClauseRow {
  clauseId: string;
  type: string;
  normalized: string;
  /** "p.27 · §17.2" -- page and span when recorded, else "Linked document", else null (no source at all). */
  source: string | null;
  risk: SemanticTag | null;
  confidencePct: number | null;
}

/** High/Critical risk gets the accent tag, the rest stay neutral; `null` when the clause carries no risk level. Text first, colour only as emphasis. */
export function getClauseRiskTag(riskLevel: string | null): SemanticTag | null {
  if (riskLevel === null || riskLevel.trim() === "") return null;
  const emphasised = riskLevel === "High" || riskLevel === "Critical";
  return { variant: emphasised ? "accent" : "neutral", label: riskLevel };
}

export function buildClauseRows(clauses: readonly Contract360ClauseBody[]): ClauseRow[] {
  return clauses.map((c) => ({
    clauseId: c.clauseId,
    type: c.clauseType,
    normalized: c.normalizedValue ?? c.rawText,
    source: formatSource(c),
    risk: getClauseRiskTag(c.riskLevel),
    confidencePct: toConfidencePercent(c.confidence),
  }));
}

export interface ClauseEvidence {
  /** "{file} · page N · §span", each part only when known. */
  citation: string;
  before: string;
  quote: string;
  after: string;
}

/**
 * The evidence card (`markup.html` `hl`: "{{ hl.doc }} · page {{ hl.page }} · §{{ hl.sec }}" over
 * `{{ hl.before }}<mark>{{ hl.quote }}</mark>{{ hl.after }}`). The backend `Clause` carries one
 * `rawText`, not separate before/quote/after strings: when the normalised value is a literal
 * substring of the raw text that substring is the highlighted quote, otherwise the whole original
 * wording is -- never a synthesised excerpt.
 */
export function buildClauseEvidence(clause: Contract360ClauseBody, documents: readonly Contract360DocumentBody[]): ClauseEvidence {
  const document = documents.find((d) => d.documentId === clause.sourceDocumentId) ?? null;
  const parts: string[] = [];
  if (document !== null) parts.push(document.fileName);
  if (clause.sourcePage !== null) parts.push(`page ${clause.sourcePage}`);
  if (clause.sourceSpan !== null && clause.sourceSpan.trim() !== "") parts.push(clause.sourceSpan.trim().startsWith("§") ? clause.sourceSpan.trim() : `§${clause.sourceSpan.trim()}`);
  const citation = parts.length > 0 ? parts.join(" · ") : clause.clauseType;

  const raw = clause.rawText;
  const normalized = clause.normalizedValue;
  if (normalized !== null && normalized.trim() !== "" && normalized !== raw) {
    const index = raw.indexOf(normalized);
    if (index >= 0) {
      return { citation, before: raw.slice(0, index), quote: normalized, after: raw.slice(index + normalized.length) };
    }
  }
  return { citation, before: "", quote: raw, after: "" };
}

/**
 * Citation landing (`/contracts/:id?clause=<clauseId>` or `?page=<n>`): `pageParam` is consulted
 * **only** when `clauseParam` is entirely absent -- a citation names one or the other, and retrying
 * by page when the clause id it gave does not resolve risks highlighting a *different* clause than
 * the one cited. Returns `null` when neither identifies a real clause: nothing is highlighted.
 */
export function resolveHighlightedClauseId(
  clauses: readonly Contract360ClauseBody[],
  clauseParam: string | null,
  pageParam: string | null,
): string | null {
  if (clauseParam !== null) {
    return clauses.find((c) => c.clauseId === clauseParam)?.clauseId ?? null;
  }
  if (pageParam !== null) {
    const pageNumber = Number(pageParam);
    if (!Number.isFinite(pageNumber)) return null;
    return clauses.find((c) => c.sourcePage === pageNumber)?.clauseId ?? null;
  }
  return null;
}

// ---------------------------------------------------------------------------------------------
// Details ▾ — key terms, documents, facts to decide, priority score, extracted lists
// ---------------------------------------------------------------------------------------------

export const DETAILS_LABEL_CLOSED = "All terms, documents and open facts ▾";
export const DETAILS_LABEL_OPEN = "Hide details";

/**
 * One row of the Term / Value / Source / Confidence template used by the drawer's extracted lists
 * (Products / Obligations / Risks) and by the key-terms list.
 */
export interface FactRow {
  key: string;
  term: string;
  value: string;
  /** `null` renders as "—" -- a contract-level field has no source span. */
  source: string | null;
  /** 0-100, or `null` when this fact carries no extraction confidence at all. Never a 0-1 fraction -- see `toConfidencePercent`. */
  confidencePct: number | null;
}

/** Wire confidences are 0-1 fractions; `styles/semantics.ts#getConfidenceTag` expects 0-100. `null` in, `null` out. */
export function toConfidencePercent(confidence: number | null): number | null {
  return confidence === null ? null : confidence * 100;
}

interface SourceFields {
  sourceDocumentId: string | null;
  sourceSpan: string | null;
  sourcePage: number | null;
}

/** `null` when there is no linked source document at all -- distinct from "linked, but no page/span recorded" (`"Linked document"`). */
function formatSource(row: SourceFields): string | null {
  if (row.sourceDocumentId === null) return null;
  const parts: string[] = [];
  if (row.sourcePage !== null) parts.push(`p.${row.sourcePage}`);
  if (row.sourceSpan !== null && row.sourceSpan.trim() !== "") parts.push(row.sourceSpan);
  return parts.length > 0 ? parts.join(" · ") : "Linked document";
}

/**
 * "Key terms" (`app.jsx` `otherRows`: Annual spend · Start → end · Notice period · Uplift ·
 * Auto-renewal), from the real contract-level fields on the header / commercials / renewal /
 * overview. Contract-level fields carry no per-field source or confidence (they are aggregates, not
 * span extractions), so those cells stay empty rather than borrowing a clause's.
 */
export function buildKeyTerms(contract: Contract360Body): FactRow[] {
  const { header, tabs } = contract;
  const currency = tabs.commercials.currency;
  const rows: FactRow[] = [
    { key: "annualSpend", term: "Annual spend", value: formatMoney(header.annualSpend, currency), source: null, confidencePct: null },
    { key: "totalContractValue", term: "Total contract value", value: formatMoney(header.totalContractValue, currency), source: null, confidencePct: null },
    { key: "term", term: "Start → end", value: `${formatDateOnly(header.startDate)} → ${formatDateOnly(header.endDate)}`, source: null, confidencePct: null },
    { key: "cancellationDeadline", term: "Notice deadline", value: formatDateOnly(header.cancellationDeadline), source: null, confidencePct: null },
    { key: "autoRenewal", term: "Auto-renewal", value: header.autoRenewal ? "Yes" : "No", source: null, confidencePct: null },
    {
      key: "renewalTermMonths",
      term: "Renewal term",
      value: tabs.renewal.renewalTermMonths !== null ? `${tabs.renewal.renewalTermMonths} months` : "—",
      source: null,
      confidencePct: null,
    },
    { key: "paymentTerms", term: "Payment terms", value: tabs.commercials.paymentTerms ?? "—", source: null, confidencePct: null },
    { key: "governingLaw", term: "Governing law", value: tabs.overview.governingLaw ?? "—", source: null, confidencePct: null },
    { key: "effectiveDate", term: "Effective date", value: formatDateOnly(tabs.overview.effectiveDate), source: null, confidencePct: null },
    { key: "lineItemCount", term: "Line items", value: formatPlainNumber(tabs.commercials.lineItemCount), source: null, confidencePct: null },
  ];
  if (tabs.overview.parentContractId !== null) {
    rows.push({ key: "parentContractId", term: "Parent contract", value: tabs.overview.parentContractId, source: null, confidencePct: null });
  }
  return rows;
}

function formatProductValue(p: Contract360ProductBody): string {
  const parts: string[] = [];
  if (p.quantity !== null) parts.push(`${formatPlainNumber(p.quantity)}${p.unit ? ` ${p.unit}` : ""}`);
  if (p.unitPrice !== null) parts.push(`@ ${formatPlainNumber(p.unitPrice)}`);
  if (p.discount !== null) parts.push(`${formatPlainNumber(p.discount)}% disc.`);
  if (p.annualCost !== null) parts.push(`→ ${formatPlainNumber(p.annualCost)}/yr`);
  return parts.length > 0 ? parts.join(" ") : "—";
}

/** Products (line items). */
export function buildProductsRows(products: readonly Contract360ProductBody[]): FactRow[] {
  return products.map((p) => ({
    key: p.lineItemId,
    term: p.description !== "" ? p.description : (p.sku ?? "Line item"),
    value: formatProductValue(p),
    source: formatSource(p),
    confidencePct: toConfidencePercent(p.confidence),
  }));
}

function formatObligationValue(o: Contract360ObligationBody): string {
  const parts = [o.description];
  if (o.dueDate !== null) parts.push(`due ${formatDateOnly(o.dueDate)}`);
  if (o.criticality !== null) parts.push(o.criticality);
  return parts.join(" · ");
}

/** Obligations. */
export function buildObligationsRows(obligations: readonly Contract360ObligationBody[]): FactRow[] {
  return obligations.map((o) => ({
    key: o.obligationId,
    term: o.obligationType,
    value: formatObligationValue(o),
    source: formatSource(o),
    confidencePct: toConfidencePercent(o.confidence),
  }));
}

/** Risks. Severity is always textual (ADR-019 "no colour-only semantics"). */
export function buildRisksRows(risks: readonly Contract360RiskBody[]): FactRow[] {
  return risks.map((r) => ({
    key: r.riskId,
    term: r.riskType,
    value: `${r.description} (${r.severity} risk)`,
    source: formatSource(r),
    confidencePct: toConfidencePercent(r.confidence),
  }));
}

export interface DocumentRow {
  documentId: string;
  type: string;
  fileName: string;
  status: SemanticTag;
}

/** "Documents" (`app.jsx` `family`: type · file · status tag). */
export function buildDocumentRows(documents: readonly Contract360DocumentBody[]): DocumentRow[] {
  return documents.map((d) => ({
    documentId: d.documentId,
    type: getContractTypeLabel(d.documentType),
    fileName: d.fileName,
    status: getPortfolioStatusTag(d.processingStatus),
  }));
}

export interface PriorityComponentRow {
  key: string;
  label: string;
  score: number;
  explanation: string;
}

/** Order + labels quoted from `PriorityScoreCalculator`'s own product-spec §9.2 term order. */
const PRIORITY_COMPONENT_LABELS: ReadonlyArray<{ key: keyof RenewalPriorityBody["components"]; label: string }> = [
  { key: "spendWeight", label: "Spend weight" },
  { key: "timeUrgency", label: "Time urgency" },
  { key: "benchmarkOpportunity", label: "Benchmark opportunity" },
  { key: "priceIncreaseRisk", label: "Price-increase risk" },
  { key: "contractRisk", label: "Contract risk" },
];

/** The explainable priority-score breakdown. `null` renders as an empty list -- never a fabricated all-zero table. */
export function buildPriorityComponentRows(priority: RenewalPriorityBody | null): PriorityComponentRow[] {
  if (priority === null) return [];
  return PRIORITY_COMPONENT_LABELS.map(({ key, label }) => ({
    key,
    label,
    score: priority.components[key].score,
    explanation: priority.components[key].explanation,
  }));
}

/** "priority 72/100", or an honest gap while the score has not resolved. */
export function formatPriorityFact(priority: RenewalPriorityBody | null): string {
  return priority !== null ? `priority ${Math.round(priority.totalScore)}/100` : "priority not yet available";
}

export interface AttentionTerm {
  key: string;
  term: string;
  value: string;
  confidencePct: number;
  tag: SemanticTag;
}

/**
 * "Facts you still need to decide" (`app.jsx` `attnTerms`: every key term whose confidence is not
 * the >95% band). Applied to every REAL per-field confidence this aggregate carries -- Products,
 * Clauses, Obligations (contract-level aggregates have none) -- lowest confidence first. A field
 * with `confidence: null` is excluded rather than treated as "needs attention": there is no number
 * to grade it against.
 */
export function computeNeedsAttention(tabs: Contract360Body["tabs"]): AttentionTerm[] {
  const candidates: AttentionTerm[] = [];

  const consider = (key: string, term: string, value: string, confidence: number | null) => {
    if (confidence === null) return;
    const confidencePct = toConfidencePercent(confidence) as number;
    const tag = getConfidenceTag(confidencePct);
    if (tag.variant !== "neutral") candidates.push({ key, term, value, confidencePct, tag });
  };

  for (const p of tabs.products) consider(`product-${p.lineItemId}`, p.description !== "" ? p.description : (p.sku ?? "Line item"), formatProductValue(p), p.confidence);
  for (const c of tabs.clauses) consider(`clause-${c.clauseId}`, c.clauseType, c.normalizedValue ?? c.rawText, c.confidence);
  for (const o of tabs.obligations) consider(`obligation-${o.obligationId}`, o.obligationType, o.description, o.confidence);

  return candidates.sort((a, b) => a.confidencePct - b.confidencePct);
}

/** `markup.html` `noAttn`, verbatim. */
export const NO_ATTENTION_MESSAGE = "None — every fact is above 95% or signed off by you.";
