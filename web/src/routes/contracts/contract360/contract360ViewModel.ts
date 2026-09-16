import type {
  Contract360Body,
  Contract360ClauseBody,
  Contract360DocumentBody,
  Contract360HeaderBody,
  Contract360ObligationBody,
  Contract360ProductBody,
  Contract360RiskBody,
  ContractFieldEvidenceBody,
  ContractStrategyBody,
  RenewalPipelineItemBody,
  RenewalPriorityBody,
} from "../../../api/client";
import { getStatusTag, isDeadlineCritical, type DocumentStatus, type SemanticTag } from "../../../styles/semantics";
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
 * drawer holding what the tabs used to hold. Details and Why render only values the server has
 * officialized (auto-accepted or accepted by a human); unofficialized cells keep their row and
 * show the em-dash already in this file's vocabulary. Confidence tags never appear here.
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
        : "Raffa.ai tracks renewal recommendations only for auto-renewing contracts; this contract ends on its end date with no renewal to act on.",
    };
  }

  return { hasRecommendation: true, statement: item.action, rationale: item.insightCard.recommendations.explanation };
}

export interface SaveAnswer {
  /** A figure, a representative band, or an honest "Not yet available" after a real strategy call. */
  estimate: string;
  /** The lever sentence, never-bare representative provenance, or why the estimate is not yet there. */
  lever: string;
}

export interface MoveAnswer {
  /** The notice deadline as a date, "Add the end date" on an extraction miss, or an honest gap. */
  deadline: string;
  /** Review href when `deadline` is the missing-fact recovery copy; otherwise `null`. */
  deadlineHref: string | null;
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
export const ADD_THE_END_DATE = "Add the end date";

/**
 * Whether `GET /api/contracts/{id}/strategy` ran for this paint (ADR-012 w17 clause 35).
 * `called: false` is only a test seam — the route always calls the source. The two "not yet"
 * constants are reachable only after a real call that returned nothing or failed.
 */
export type StrategySource = { called: false } | { called: true; pack: ContractStrategyBody | null };

const EMPTY_SAVE: SaveAnswer = { estimate: "", lever: "" };
const EMPTY_MOVE: MoveAnswer = { deadline: "", deadlineHref: null, cancelDays: null, isUrgent: false, detail: "" };
const FAILED_MOVE: MoveAnswer = {
  deadline: SAVINGS_NOT_YET_AVAILABLE,
  deadlineHref: null,
  cancelDays: null,
  isUrgent: false,
  detail: "The renewal strategy could not be loaded. Retry the page to try again.",
};

/**
 * The three answers (`markup.html` "CONTRACT 360 — three answers"). `Where you can save` and
 * `When you must move` **map** `GET /api/contracts/{id}/strategy` (ADR-012 w17 clause 35) — they
 * never compute a second figure from the pipeline. Three states per cell (ADR-020 w17 §14(f)):
 * a figure and its lever; a representative band with Ask provenance (`adapter A, n = 214`) on the
 * detail line; or no answer yet, naming what is missing and the way to get it.
 */
export function buildAnswers(
  header: Contract360HeaderBody,
  renewal: Contract360Body["tabs"]["renewal"],
  renewals: readonly RenewalPipelineItemBody[],
  strategy: StrategySource,
): AnswersBand {
  const act = buildRecommendation(header, renewals);

  if (!strategy.called) {
    return { save: EMPTY_SAVE, move: EMPTY_MOVE, act };
  }

  if (strategy.pack === null) {
    return {
      save: { estimate: SAVINGS_NOT_YET_AVAILABLE, lever: LEVER_NOT_YET_AVAILABLE },
      move: FAILED_MOVE,
      act,
    };
  }

  return {
    save: mapSave(strategy.pack),
    move: mapMove(header, renewal, strategy.pack),
    act,
  };
}

function mapSave(pack: ContractStrategyBody): SaveAnswer {
  const target = pack.targets[0];
  const lever = pack.whereYouCanPush[0];

  if (
    target !== undefined &&
    target.explanation.toLowerCase().includes("representative") &&
    target.acceptableRangeLow !== null &&
    target.acceptableRangeHigh !== null
  ) {
    return {
      estimate: `${formatPlainNumber(target.acceptableRangeLow)}–${formatPlainNumber(target.acceptableRangeHigh)}`,
      lever: formatRepresentativeProvenance(target.explanation),
    };
  }

  if (target !== undefined && target.openingTarget !== null && lever !== undefined) {
    return { estimate: formatPlainNumber(target.openingTarget), lever: lever.rationale };
  }

  return { estimate: SAVINGS_NOT_YET_AVAILABLE, lever: LEVER_NOT_YET_AVAILABLE };
}

/** Ask vocabulary (`ia-v2.md:110`): `adapter A, n = 214` — never a second phrase for the same idea. */
function formatRepresentativeProvenance(explanation: string): string {
  const match = explanation.match(/representative \(source: ([^;]+)(?:; n=(\d+))?(?:; as of ([^)]+))?\)/i);
  if (match === null) {
    return explanation.toLowerCase().includes("representative") ? explanation : `representative · ${explanation}`;
  }
  const adapter = match[1].trim();
  const sample = match[2];
  const asOf = match[3]?.trim();
  const adapterPart = sample !== undefined ? `adapter ${adapter}, n = ${sample}` : `adapter ${adapter}`;
  return asOf !== undefined && asOf !== ""
    ? `representative · ${adapterPart} · as of ${asOf}`
    : `representative · ${adapterPart}`;
}

function mapMove(
  header: Contract360HeaderBody,
  renewal: Contract360Body["tabs"]["renewal"],
  pack: ContractStrategyBody,
): MoveAnswer {
  const move = pack.whenYouMustMove;
  const determinedNoRenewal = move.explanation.toLowerCase().includes("does not auto-renew");
  const hasDate = move.cancellationDeadline !== null || move.renewalDate !== null;

  if (!hasDate && !determinedNoRenewal) {
    return {
      deadline: ADD_THE_END_DATE,
      deadlineHref: `/contracts/${header.contractId}/review`,
      cancelDays: null,
      isUrgent: false,
      detail: "Extraction missed the end date. Add it on Review to get the notice deadline.",
    };
  }

  const date = move.cancellationDeadline ?? move.renewalDate;
  const deadline = date !== null ? formatDateOnly(date) : "No auto-renewal";
  const cancelDays = move.daysLeft;
  const isUrgent = cancelDays !== null && !move.passedDeadline && isDeadlineCritical(cancelDays);
  const autoText =
    header.autoRenewal && renewal.renewalTermMonths !== null
      ? ` and auto-renews for ${renewal.renewalTermMonths} months`
      : header.autoRenewal
        ? " and auto-renews"
        : "";
  const termEnd = move.renewalDate !== null ? `Term ends ${formatDateOnly(move.renewalDate)}` : "";

  let detail: string;
  if (determinedNoRenewal) {
    detail = move.explanation;
  } else if (cancelDays === null) {
    detail = [move.explanation, termEnd !== "" ? `${termEnd}${autoText}.` : null].filter((part) => part !== null).join(" ");
  } else if (cancelDays < 0 || move.passedDeadline) {
    const ago = Math.abs(cancelDays);
    const closed = `${ago} day${ago === 1 ? "" : "s"} ago — the notice window has closed.`;
    detail = termEnd !== "" ? `${closed} ${termEnd}${autoText}.` : closed;
  } else {
    const prefix = `in ${cancelDays} day${cancelDays === 1 ? "" : "s"} — last day to give notice`;
    detail = termEnd !== "" ? `${prefix}. ${termEnd}${autoText}.` : `${prefix}${autoText}.`;
  }

  return { deadline, deadlineHref: null, cancelDays, isUrgent, detail };
}

// ---------------------------------------------------------------------------------------------
// Negotiation tracker (after "Start negotiation")
// ---------------------------------------------------------------------------------------------

export interface NegotiationStep {
  key: string;
  label: string;
  due: string;
}

export const NEGOTIATION_STEP_KEYS = [
  "Notify",
  "RequestRevisedPricing",
  "CounterWithMarketBenchmark",
  "SignOrSendNonRenewalNotice",
] as const;

const KNOWN_STEP_KEYS = new Set<string>(NEGOTIATION_STEP_KEYS);

/** Wire keys the server returned, minus unknown names (never rendered as a fifth tick). A missing key is unticked. */
export function ticksFromServer(keys: readonly string[]): ReadonlySet<string> {
  return new Set(keys.filter((key) => KNOWN_STEP_KEYS.has(key)));
}

/** `app.jsx` `stepDefs`, with the real supplier and deadline: 4 steps, due "this week" · "+10 days" · "+20 days" · "by {cancel}". */
export function buildNegotiationSteps(supplierLabel: string, deadlineLabel: string): NegotiationStep[] {
  return [
    { key: "Notify", label: `Notify ${supplierLabel} of intent to renegotiate`, due: "this week" },
    { key: "RequestRevisedPricing", label: "Request revised pricing and licence mix", due: "+10 days" },
    { key: "CounterWithMarketBenchmark", label: "Counter with the market benchmark", due: "+20 days" },
    { key: "SignOrSendNonRenewalNotice", label: "Sign, or send non-renewal notice", due: `by ${deadlineLabel}` },
  ];
}

/** `markup.html`: "target {{ cur.saving }} · close by {{ cur.cancel }}". */
export function formatTrackerMeta(save: SaveAnswer, move: MoveAnswer): string {
  return `target ${save.estimate} · close by ${move.deadline}`;
}

// ---------------------------------------------------------------------------------------------
// Officialized values (ADR-012 w17 clause 36; ADR-020 w17 §14)
// ---------------------------------------------------------------------------------------------

/** The em-dash already in this view model's vocabulary (`formatMoney`, `formatPlainNumber`, empty product). */
export const UNOFFICIALIZED_PLACEHOLDER = "—";

/** Same bar `ExtractionConfidencePolicy.AutoAcceptThreshold` publishes; used only when evidence did not carry one. */
export const AUTO_ACCEPT_THRESHOLD = 0.9;

export function isOfficializedDecision(decision: ContractFieldEvidenceBody["decision"] | null | undefined): boolean {
  return decision === "auto_accepted" || decision === "human_accepted";
}

/** Entity rows carry confidence, not a persisted decision. Same compare the server uses (`>=` the published bar). */
export function isScoredFactOfficialized(confidence: number | null, autoAcceptThreshold: number): boolean {
  if (confidence === null) return true;
  return confidence >= autoAcceptThreshold;
}

export function officializedOrDash(value: string, officialized: boolean): string {
  return officialized ? value : UNOFFICIALIZED_PLACEHOLDER;
}

export function indexEvidenceByField(
  evidence: readonly ContractFieldEvidenceBody[],
): ReadonlyMap<string, ContractFieldEvidenceBody> {
  const byField = new Map<string, ContractFieldEvidenceBody>();
  for (const entry of evidence) {
    byField.set(entry.fieldName.toLowerCase(), entry);
  }
  return byField;
}

function decisionForField(
  evidenceByField: ReadonlyMap<string, ContractFieldEvidenceBody>,
  fieldName: string,
): ContractFieldEvidenceBody["decision"] | null {
  return evidenceByField.get(fieldName.toLowerCase())?.decision ?? null;
}

function officializedFieldValue(
  value: string,
  evidenceByField: ReadonlyMap<string, ContractFieldEvidenceBody>,
  fieldName: string,
): string {
  return officializedOrDash(value, isOfficializedDecision(decisionForField(evidenceByField, fieldName)));
}

// ---------------------------------------------------------------------------------------------
// Why — the clauses behind it
// ---------------------------------------------------------------------------------------------

export interface ClauseRow {
  clauseId: string;
  type: string;
  /** Officialized normalised value; unofficialized clauses keep the row and show "—". */
  normalized: string;
  risk: SemanticTag | null;
  /** One-line why for the leverage tag; `null` when the clause has no risk level. */
  why: string | null;
  /** `/documents/:id/viewer?page=&clause=`, or `null` when the clause has no resolvable document or page. */
  viewerHref: string | null;
}

export const LEVERAGE_PUSH_TO_CHANGE = "Push to change";
export const LEVERAGE_WORTH_RAISING = "Worth raising";
export const LEVERAGE_STANDARD_TERMS = "Standard terms";
export const LEVERAGE_LEGEND = `${LEVERAGE_PUSH_TO_CHANGE} · ${LEVERAGE_WORTH_RAISING} · ${LEVERAGE_STANDARD_TERMS}`;

/** High/Critical risk gets the accent tag, the rest stay neutral; `null` when the clause carries no risk level. Text first, colour only as emphasis. */
export function getClauseRiskTag(riskLevel: string | null): SemanticTag | null {
  if (riskLevel === null || riskLevel.trim() === "") return null;
  const emphasised = riskLevel === "High" || riskLevel === "Critical";
  const label =
    riskLevel === "High" || riskLevel === "Critical"
      ? LEVERAGE_PUSH_TO_CHANGE
      : riskLevel === "Medium"
        ? LEVERAGE_WORTH_RAISING
        : riskLevel === "Low"
          ? LEVERAGE_STANDARD_TERMS
          : null;
  if (label === null) return null;
  return { variant: emphasised ? "accent" : "neutral", label };
}

/** Product copy for the leverage tag — not an extracted fact. `null` when risk was never determined. */
export function leverageWhy(riskLevel: string | null): string | null {
  if (riskLevel === "High" || riskLevel === "Critical") return "This clause costs money if it stays.";
  if (riskLevel === "Medium") return "Worth raising in negotiation.";
  if (riskLevel === "Low") return "Usual language — keep unless you have a reason.";
  return null;
}

export function buildClauseRows(
  clauses: readonly Contract360ClauseBody[],
  documents: readonly Contract360DocumentBody[] = [],
  autoAcceptThreshold: number = AUTO_ACCEPT_THRESHOLD,
): ClauseRow[] {
  return clauses.map((c) => ({
    clauseId: c.clauseId,
    type: c.clauseType,
    normalized: officializedOrDash(c.normalizedValue ?? c.rawText, isScoredFactOfficialized(c.confidence, autoAcceptThreshold)),
    risk: getClauseRiskTag(c.riskLevel),
    why: leverageWhy(c.riskLevel),
    viewerHref: clauseViewerHref(c, documents),
  }));
}

/**
 * `/documents/:documentId/viewer?page=<sourcePage>&clause=<clauseId>` — the route E22/F03 registered,
 * reused verbatim. No document or no `sourcePage` → no link (never a dead href).
 */
export function clauseViewerHref(
  clause: Pick<Contract360ClauseBody, "clauseId" | "sourceDocumentId" | "sourcePage">,
  documents: readonly Contract360DocumentBody[],
): string | null {
  if (clause.sourceDocumentId === null || clause.sourcePage === null) return null;
  const document = documents.find((d) => d.documentId === clause.sourceDocumentId);
  if (document === undefined) return null;
  return `/documents/${document.documentId}/viewer?page=${clause.sourcePage}&clause=${clause.clauseId}`;
}

export interface ClauseEvidence {
  /** Short `p.N · §` reference, span capped at Review's 60 characters; file name when known. */
  citation: string;
  before: string;
  quote: string;
  after: string;
}

/** Review's existing span cap (`ReviewFieldList.tsx`); not a second truncation rule. */
const MAX_SPAN_PREVIEW = 60;

function truncateSpan(text: string): string {
  return text.length <= MAX_SPAN_PREVIEW ? text : `${text.slice(0, MAX_SPAN_PREVIEW - 1)}…`;
}

/** Short `p.N · §` reference for the highlight header. `null` when there is no page and no span. */
export function formatShortReference(row: { sourcePage: number | null; sourceSpan: string | null }): string | null {
  const spanRaw = row.sourceSpan === null ? null : row.sourceSpan.replace(/\s+/g, " ").trim();
  const span = spanRaw === null || spanRaw === "" ? null : truncateSpan(spanRaw);
  const page = row.sourcePage !== null ? `p.${row.sourcePage}` : null;
  const section = span === null ? null : span.startsWith("§") ? span : `§${span}`;
  if (page !== null && section !== null) return `${page} · ${section}`;
  if (page !== null) return page;
  if (section !== null) return section;
  return null;
}

/**
 * The evidence card (`markup.html` `hl`: "{{ hl.doc }} · page {{ hl.page }} · §{{ hl.sec }}" over
 * `{{ hl.before }}<mark>{{ hl.quote }}</mark>{{ hl.after }}`). The backend `Clause` carries one
 * `rawText`, not separate before/quote/after strings: when the normalised value is a literal
 * substring of the raw text that substring is the highlighted quote, otherwise the whole original
 * wording is -- never a synthesised excerpt. The header citation uses the short capped `p.N · §`
 * reference; the untruncated quote lives only in this card.
 */
export function buildClauseEvidence(clause: Contract360ClauseBody, documents: readonly Contract360DocumentBody[]): ClauseEvidence {
  const document = documents.find((d) => d.documentId === clause.sourceDocumentId) ?? null;
  const shortRef = formatShortReference(clause);
  const parts: string[] = [];
  if (document !== null) parts.push(document.fileName);
  if (shortRef !== null) parts.push(shortRef);
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
// Details ▾ — key terms, documents, review count, priority score, extracted lists
// ---------------------------------------------------------------------------------------------


export const DETAILS_LABEL_CLOSED = "All terms, documents and open facts ▾";
export const DETAILS_LABEL_OPEN = "Hide details";

/**
 * One row of the Term / Value / Source template used by the drawer's extracted lists
 * (Products / Obligations / Risks) and by the key-terms list. Unofficialized values are already
 * the em-dash; no confidence travels with the row.
 */
export interface FactRow {
  key: string;
  term: string;
  value: string;
  /** `null` renders as "—" -- a contract-level field has no source span. */
  source: string | null;
}

/** Wire confidences are 0-1 fractions. `null` in, `null` out. Kept as the one conversion point the rest of this file uses when a percentage is needed off-screen. */
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
 * overview. Every row stays; a value the server has not accepted (`review_required`, or no
 * evidence row) renders as "—". Contract-level fields carry no per-field source (they are
 * aggregates, not span extractions), so those cells stay empty rather than borrowing a clause's.
 */
export function buildKeyTerms(
  contract: Contract360Body,
  evidence: readonly ContractFieldEvidenceBody[] = [],
): FactRow[] {
  const { header, tabs } = contract;
  const currency = tabs.commercials.currency;
  const byField = indexEvidenceByField(evidence);
  const start = officializedFieldValue(formatDateOnly(header.startDate), byField, "startDate");
  const end = officializedFieldValue(formatDateOnly(header.endDate), byField, "endDate");
  const renewalTerm =
    tabs.renewal.renewalTermMonths !== null ? `${tabs.renewal.renewalTermMonths} months` : UNOFFICIALIZED_PLACEHOLDER;
  const rows: FactRow[] = [
    { key: "annualSpend", term: "Annual spend", value: officializedFieldValue(formatMoney(header.annualSpend, currency), byField, "annualSpend"), source: null },
    { key: "totalContractValue", term: "Total contract value", value: officializedFieldValue(formatMoney(header.totalContractValue, currency), byField, "totalContractValue"), source: null },
    { key: "term", term: "Start → end", value: `${start} → ${end}`, source: null },
    { key: "cancellationDeadline", term: "Notice deadline", value: officializedFieldValue(formatDateOnly(header.cancellationDeadline), byField, "cancellationDeadline"), source: null },
    { key: "autoRenewal", term: "Auto-renewal", value: officializedFieldValue(header.autoRenewal ? "Yes" : "No", byField, "autoRenewal"), source: null },
    { key: "renewalTermMonths", term: "Renewal term", value: officializedFieldValue(renewalTerm, byField, "renewalTermMonths"), source: null },
    { key: "paymentTerms", term: "Payment terms", value: officializedFieldValue(tabs.commercials.paymentTerms ?? UNOFFICIALIZED_PLACEHOLDER, byField, "paymentTerms"), source: null },
    { key: "governingLaw", term: "Governing law", value: officializedFieldValue(tabs.overview.governingLaw ?? UNOFFICIALIZED_PLACEHOLDER, byField, "governingLaw"), source: null },
    { key: "effectiveDate", term: "Effective date", value: officializedFieldValue(formatDateOnly(tabs.overview.effectiveDate), byField, "effectiveDate"), source: null },
    { key: "lineItemCount", term: "Line items", value: formatPlainNumber(tabs.commercials.lineItemCount), source: null },
  ];
  if (tabs.overview.parentContractId !== null) {
    rows.push({ key: "parentContractId", term: "Parent contract", value: tabs.overview.parentContractId, source: null });
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

/** Products (line items). Every row stays; a value below the published auto-accept bar is "—". */
export function buildProductsRows(
  products: readonly Contract360ProductBody[],
  autoAcceptThreshold: number = AUTO_ACCEPT_THRESHOLD,
): FactRow[] {
  return products.map((p) => ({
    key: p.lineItemId,
    term: p.description !== "" ? p.description : (p.sku ?? "Line item"),
    value: officializedOrDash(formatProductValue(p), isScoredFactOfficialized(p.confidence, autoAcceptThreshold)),
    source: formatSource(p),
  }));
}

function formatObligationValue(o: Contract360ObligationBody): string {
  const parts = [o.description];
  if (o.dueDate !== null) parts.push(`due ${formatDateOnly(o.dueDate)}`);
  if (o.criticality !== null) parts.push(o.criticality);
  return parts.join(" · ");
}

/** Obligations. Every row stays; a value below the published auto-accept bar is "—". */
export function buildObligationsRows(
  obligations: readonly Contract360ObligationBody[],
  autoAcceptThreshold: number = AUTO_ACCEPT_THRESHOLD,
): FactRow[] {
  return obligations.map((o) => ({
    key: o.obligationId,
    term: o.obligationType,
    value: officializedOrDash(formatObligationValue(o), isScoredFactOfficialized(o.confidence, autoAcceptThreshold)),
    source: formatSource(o),
  }));
}

/** Risks. Severity is always textual (ADR-019 "no colour-only semantics"). Every row stays. */
export function buildRisksRows(
  risks: readonly Contract360RiskBody[],
  autoAcceptThreshold: number = AUTO_ACCEPT_THRESHOLD,
): FactRow[] {
  return risks.map((r) => ({
    key: r.riskId,
    term: r.riskType,
    value: officializedOrDash(`${r.description} (${r.severity} risk)`, isScoredFactOfficialized(r.confidence, autoAcceptThreshold)),
    source: formatSource(r),
  }));
}

export interface DocumentRow {
  documentId: string;
  type: string;
  fileName: string;
  status: SemanticTag;
}

/** `Document.ProcessingStatus` is the closed enum ADR-019's locked status mapping was written
 * against, so a family row's tag is `styles/semantics.ts#getStatusTag`'s own (task E16/F03/US01/T01:
 * a not-yet-validated document renders *with its row status tag* rather than being omitted --
 * ADR-020 w15 §2.3 -- and a refused one reads "Not added", never a capitalised wire value). */
function toDocumentStatus(processingStatus: Contract360DocumentBody["processingStatus"]): DocumentStatus {
  switch (processingStatus) {
    case "Uploaded":
    case "Processing":
      return "processing";
    case "NeedsReview":
      return "needs_review";
    case "Completed":
      return "completed";
    case "Failed":
      return "failed";
    case "Rejected":
      return "rejected";
  }
}

/** "Documents" (`app.jsx` `family`: type · file · status tag). */
export function buildDocumentRows(documents: readonly Contract360DocumentBody[]): DocumentRow[] {
  return documents.map((d) => ({
    documentId: d.documentId,
    type: getContractTypeLabel(d.documentType),
    fileName: d.fileName,
    status: getStatusTag(toDocumentStatus(d.processingStatus)),
  }));
}

// ---------------------------------------------------------------------------------------------
// Readiness (ADR-027 §D9; ADR-020 w15 §2.3 -- the fifth state, task E16/F03/US01/T01)
// ---------------------------------------------------------------------------------------------

export interface ReadinessCopy {
  heading: string;
  sentence: string;
}

/**
 * The two readings of `readiness.state` that keep the screen from rendering an *empty contract*:
 * once NW-27 creates a `contractId` before extraction finishes, `ready` would otherwise show an
 * aggregate with no clauses, no spend and no dates, indistinguishable from a contract whose
 * extraction genuinely found nothing. Driven by the server's `readiness` and never inferred from
 * an empty clause array (ADR-012 w15 §4). `null` for `ready`: the page renders as before.
 */
export function resolveReadinessCopy(readiness: Contract360Body["readiness"]): ReadinessCopy | null {
  switch (readiness.state) {
    case "ready":
      return null;
    case "processing":
      return {
        heading: "This contract is still being prepared.",
        sentence: "Raffa.ai is still extracting the facts. It will open here once they pass validation.",
      };
    case "unavailable":
      return {
        heading: "This contract has no validated facts yet.",
        sentence: "Raffa.ai could not finish processing its documents. Open Documents to see what happened to each one.",
      };
  }
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

/**
 * Count of facts that still need a human on Review. Reads the server's persisted `decision`
 * (`review_required`) — never a tag variant and never a percentage (OQ-w17-cl-02). Collapsing
 * confidence bands therefore cannot change this number.
 */
export function computeNeedsAttention(evidence: readonly Pick<ContractFieldEvidenceBody, "decision">[]): number {
  return evidence.filter((row) => row.decision === "review_required").length;
}

export function formatReviewCountLine(count: number): string {
  return `${count} facts still need you — Review all →`;
}
