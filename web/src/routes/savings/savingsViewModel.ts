import type { PortfolioPageBody, SavingsKpiSummaryBody, SavingsOpportunityBody } from "../../api/client";
import type { TrackedRenewalAction } from "../renewals/renewalActionStore";
import type { SemanticTag, TagVariant } from "../../styles/semantics";
import { formatSupplier } from "../contracts/portfolioTableFormatters";

/**
 * Pure view-model helpers for the V2 Savings screen (route `/savings`; ADR-024 V2 IA "No Home
 * item" -- reached from Ask actions, Renewals and Contract 360, not the rail; screens-v2.md #8;
 * `app.jsx` `kpis` / `opps`). No React here, so every rule below is unit-testable without rendering
 * anything (`savingsViewModel.test.ts`).
 *
 * V2 shape, quoted from screens-v2.md #8: "KPIs: Contracts analyzed · Upcoming renewals · Savings
 * identified (with meta lines); opportunities table (Supplier · Action · Estimate · Status), rows
 * open Contract 360." The Day-1 six-cell row and eight-column table are gone; the same two real
 * fetches (`GET /api/savings/kpis`, `GET /api/savings`) feed it, plus the portfolio for supplier
 * names (`SavingsOpportunityResult` carries a supplier *id* only).
 */

const INTEGER_FORMAT = new Intl.NumberFormat("en-GB");

/** Same "no currency-conversion service, so group by currency, never sum across them" discipline
 * `SavingsRangeByCurrency`'s own backend doc comment states -- more than one currency bucket renders
 * one line per currency, never a silently-summed cross-currency figure. */
function formatCurrencyAmount(currency: string, amount: number): string {
  return `${currency} ${INTEGER_FORMAT.format(Math.round(amount))}`;
}

/** Low === high (e.g. a single realized value) collapses to one figure instead of a zero-width range. */
function formatCurrencyRange(currency: string, low: number, high: number): string {
  const lowText = INTEGER_FORMAT.format(Math.round(low));
  const highText = INTEGER_FORMAT.format(Math.round(high));
  return low === high ? `${currency} ${lowText}` : `${currency} ${lowText}–${highText}`;
}

const NOT_YET_AVAILABLE = "Not yet available";

// ---------------------------------------------------------------------------------------------
// KPI band + the "benchmark-provider-unreachable -> KPIs stale-labelled" state
// ---------------------------------------------------------------------------------------------

/**
 * The KPI band's own fetch state -- deliberately `loading | ready` only, never a blocking `error`:
 * the last successfully-fetched summary (or `null`, before the first successful fetch) stays on
 * screen, tagged stale, rather than being replaced by an error block.
 */
export type KpiFetchState =
  | { phase: "loading" }
  | { phase: "ready"; kpis: SavingsKpiSummaryBody | null; stale: boolean };

export type KpiFetchOutcome = { ok: true; kpis: SavingsKpiSummaryBody } | { ok: false };

/** A successful fetch always replaces the summary and clears `stale`; a failed fetch keeps whichever summary this reducer already had (or `null`) and sets `stale: true`. */
export function reduceKpiFetch(previous: KpiFetchState, outcome: KpiFetchOutcome): KpiFetchState {
  if (outcome.ok) {
    return { phase: "ready", kpis: outcome.kpis, stale: false };
  }
  const previousKpis = previous.phase === "ready" ? previous.kpis : null;
  return { phase: "ready", kpis: previousKpis, stale: true };
}

export interface KpiCellView {
  key: "contracts-analyzed" | "upcoming-renewals" | "savings-identified";
  label: string;
  /** One formatted line per currency bucket (or a single plain count) -- empty only when `kpis` is `null`, rendered as "—" by `KpiRow.tsx`, never a fabricated number. */
  lines: readonly string[];
  meta: string | null;
}

function identifiedLines(kpis: SavingsKpiSummaryBody): string[] {
  return kpis.savingsIdentified.map((bucket) => formatCurrencyRange(bucket.currency, bucket.low, bucket.high));
}

function countOf(buckets: ReadonlyArray<{ count: number }>): number {
  return buckets.reduce((total, bucket) => total + bucket.count, 0);
}

/**
 * The three V2 cells, in `app.jsx` `kpis` order. Every meta line is a real figure from the same
 * response: the prototype's "still processing or in review" / "within 180 days" metas need counts
 * this endpoint does not return, so the metas here name what it *does* return -- annual spend
 * analyzed, the pipeline scope, and the in-progress / realized counts.
 */
export function buildKpiCells(kpis: SavingsKpiSummaryBody | null): readonly KpiCellView[] {
  if (kpis === null) {
    return [
      { key: "contracts-analyzed", label: "Contracts analyzed", lines: [], meta: null },
      { key: "upcoming-renewals", label: "Upcoming renewals", lines: [], meta: null },
      { key: "savings-identified", label: "Savings identified", lines: [], meta: null },
    ];
  }

  const spendLines = kpis.annualSpendAnalyzed.map((bucket) => formatCurrencyAmount(bucket.currency, bucket.amount));
  return [
    {
      key: "contracts-analyzed",
      label: "Contracts analyzed",
      lines: [INTEGER_FORMAT.format(kpis.contractsAnalyzedCount)],
      meta: spendLines.length > 0 ? `${spendLines.join(" · ")} annual spend` : "no annual spend recorded yet",
    },
    {
      key: "upcoming-renewals",
      label: "Upcoming renewals",
      lines: [INTEGER_FORMAT.format(kpis.upcomingRenewalsCount)],
      meta: "auto-renewing contracts in the pipeline",
    },
    {
      key: "savings-identified",
      label: "Savings identified",
      lines: identifiedLines(kpis),
      meta: `${countOf(kpis.savingsIdentified)} identified · ${countOf(kpis.savingsInProgress)} in progress · ${countOf(kpis.savingsRealized)} realized`,
    },
  ];
}

/** Header summary while nothing feeds the screen yet (R-WEB-02 tier wording, same voice as Portfolio's "Lights up from validated contracts"). */
export const SAVINGS_SUMMARY_OFF = "Lights up from validated contracts and actioned renewals";

/** Header summary: "N opportunities · CHF 410,000–590,000 identified" -- counts from the list, ranges from the KPI response. */
export function formatSavingsSummary(kpis: SavingsKpiSummaryBody | null, rowCount: number): string {
  const identified = kpis === null ? [] : identifiedLines(kpis);
  const identifiedPart = identified.length > 0 ? `${identified.join(" + ")} identified` : null;
  if (rowCount === 0) return identifiedPart ?? SAVINGS_SUMMARY_OFF;
  const countPart = `${rowCount} opportunit${rowCount === 1 ? "y" : "ies"}`;
  return identifiedPart === null ? countPart : `${countPart} · ${identifiedPart}`;
}

// ---------------------------------------------------------------------------------------------
// Opportunities table + row navigation
// ---------------------------------------------------------------------------------------------

/** `app.jsx` `opps[].open` → Contract 360; an opportunity with no `contractId` (a future quote-sourced one -- `SavingsOpportunityResult` carries no `quoteId`) falls back to the Quote check landing. */
export type OpportunityNavigation = { kind: "contract"; contractId: string } | { kind: "quote" };

export function getOpportunityNavigation(contractId: string | null): OpportunityNavigation {
  return contractId !== null ? { kind: "contract", contractId } : { kind: "quote" };
}

/** contractId → supplier name, from the portfolio page (R-SUP-04: the wire's own resolved name, never an id). */
export function buildSupplierNameIndex(items: PortfolioPageBody["items"]): ReadonlyMap<string, string> {
  const index = new Map<string, string>();
  for (const item of items) {
    if (item.supplierName !== null && item.supplierName.trim() !== "") index.set(item.contractId, item.supplierName);
  }
  return index;
}

export interface OpportunityRowView {
  /** Stable id for React keys -- `SavingsOpportunityBody.id` for a real row, `renewal-action-<contractId>` for a tracked one. */
  key: string;
  supplierLabel: string;
  supplierTitle: string | undefined;
  /** "Action" column: the opportunity's type (Renewal, Benchmark, …) -- the backend records no free-text action per opportunity. */
  action: string;
  estimate: string;
  /** `null` -- rendered with no tag -- for a tracked renewal action, which has no confidence score of its own. */
  confidence: SemanticTag | null;
  status: SemanticTag;
  navigation: OpportunityNavigation;
}

/** Real name when the portfolio knows the contract's supplier; else the id-fragment fallback the Portfolio table also uses; else an honest placeholder. */
function resolveSupplier(
  contractId: string | null,
  supplierId: string | null,
  supplierNames: ReadonlyMap<string, string>,
): { label: string; title: string | undefined } {
  const known = contractId !== null ? supplierNames.get(contractId) : undefined;
  if (known !== undefined) return { label: known, title: supplierId ?? undefined };
  if (supplierId !== null) return formatSupplier(supplierId);
  return { label: "Supplier not resolved", title: undefined };
}

/** `Identified` (nothing actioned yet) stays neutral; `InProgress`/`Realized` get the accent emphasis, distinguished by their own text. */
export function getSavingsStatusTag(status: SavingsOpportunityBody["status"]): SemanticTag {
  switch (status) {
    case "Identified":
      return { variant: "neutral", label: "Identified" };
    case "InProgress":
      return { variant: "accent", label: "In progress" };
    case "Realized":
      return { variant: "accent", label: "Realized" };
  }
}

/** Confidence tier + raw score as a percentage ("High · 92%"), never a bare tier or colour alone. */
export function getSavingsConfidenceTag(confidence: number, level: SavingsOpportunityBody["confidenceLevel"]): SemanticTag {
  const variantByLevel: Record<SavingsOpportunityBody["confidenceLevel"], TagVariant> = {
    High: "neutral",
    Medium: "accent",
    Low: "outline",
  };
  const percent = Math.round(confidence * 100);
  return { variant: variantByLevel[level], label: `${level} · ${percent}%` };
}

function buildRealOpportunityRow(item: SavingsOpportunityBody, supplierNames: ReadonlyMap<string, string>): OpportunityRowView {
  const supplier = resolveSupplier(item.contractId, item.supplierId, supplierNames);
  return {
    key: item.id,
    supplierLabel: supplier.label,
    supplierTitle: supplier.title,
    action: item.type,
    estimate: formatCurrencyRange(item.currency, item.estimatedSavingsLow, item.estimatedSavingsHigh),
    confidence: getSavingsConfidenceTag(item.confidence, item.confidenceLevel),
    status: getSavingsStatusTag(item.status),
    navigation: getOpportunityNavigation(item.contractId),
  };
}

/**
 * A session-tracked renewal action rendered as its own row (`../renewals/renewalActionStore.ts`
 * names this screen as its consumer). It has no estimate or confidence of its own -- it only ever
 * recorded owner/status/action against a contract, never a `SavingsOpportunity` row -- so those
 * cells say so rather than borrowing a figure.
 */
function buildTrackedOpportunityRow(tracked: TrackedRenewalAction, supplierNames: ReadonlyMap<string, string>): OpportunityRowView {
  const supplier = resolveSupplier(tracked.contractId, tracked.supplierId, supplierNames);
  return {
    key: `renewal-action-${tracked.contractId}`,
    supplierLabel: supplier.label,
    supplierTitle: supplier.title,
    action: "Renewal",
    estimate: NOT_YET_AVAILABLE,
    confidence: null,
    status: { variant: "accent", label: tracked.action },
    navigation: getOpportunityNavigation(tracked.contractId),
  };
}

/**
 * The whole table: this session's tracked renewal actions first (most-recently-acted first), then
 * every real, persisted `SavingsOpportunity`. No de-duplication between the two: a tracked action
 * never creates a `SavingsOpportunity` row, so there is no shared id, and merging on `contractId`
 * alone would risk silently dropping a row.
 */
export function buildOpportunityRows(
  opportunities: readonly SavingsOpportunityBody[],
  trackedRenewalActions: readonly TrackedRenewalAction[],
  supplierNames: ReadonlyMap<string, string> = new Map(),
): readonly OpportunityRowView[] {
  return [
    ...trackedRenewalActions.map((tracked) => buildTrackedOpportunityRow(tracked, supplierNames)),
    ...opportunities.map((item) => buildRealOpportunityRow(item, supplierNames)),
  ];
}
