import type { PortfolioPageBody, SavingsKpiSummaryBody, SavingsOpportunityBody } from "../../api/client";
import type { SemanticTag, TagVariant } from "../../styles/semantics";
import { formatSupplier } from "../contracts/portfolioTableFormatters";
import { matchesSavingsFilters, type SavingsFilterState } from "./savingsFilters";

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

// ---------------------------------------------------------------------------------------------
// KPI band + the "benchmark-provider-unreachable -> KPIs stale-labelled" state
// ---------------------------------------------------------------------------------------------

/** ADR-001 w17 clause 10: the verified-money cell is labelled "Savings verified"; the domain word Realized never labels it. */

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
  key: "contracts-analyzed" | "upcoming-renewals" | "savings-identified" | "savings-verified";
  label: string;
  /**
   * One formatted line per currency bucket (or a single plain count). Empty when `kpis` is `null`
   * (not loaded / failed fetch: `meta === null`) **or** when the tenant has no figure for that
   * cell (loaded and empty: `meta !== null`, rendered as "—" by `KpiRow.tsx`). Never a fabricated `0`.
   */
  lines: readonly string[];
  meta: string | null;
}

function identifiedLines(kpis: SavingsKpiSummaryBody): string[] {
  return kpis.savingsIdentified.map((bucket) => formatCurrencyRange(bucket.currency, bucket.low, bucket.high));
}

function verifiedLines(kpis: SavingsKpiSummaryBody): string[] {
  return kpis.savingsRealized.map((bucket: SavingsKpiSummaryBody["savingsRealized"][number]) =>
    formatCurrencyAmount(bucket.currency, bucket.amount),
  );
}

function countOf(buckets: ReadonlyArray<{ count: number }>): number {
  return buckets.reduce((total, bucket) => total + bucket.count, 0);
}

function verifiedMeta(kpis: SavingsKpiSummaryBody): string {
  const recorded = countOf(kpis.savingsRealized);
  if (recorded === 0) return "no verified savings recorded yet";
  return `from ${recorded} recorded outcome${recorded === 1 ? "" : "s"}`;
}

/**
 * The four V2 cells, in band order. Every meta line is a real figure from the same
 * response: the prototype's "still processing or in review" / "within 180 days" metas need counts
 * this endpoint does not return, so the metas here name what it *does* return -- annual spend
 * analyzed, the pipeline scope, identified/in-progress counts, and verified-money provenance.
 */
export function buildKpiCells(kpis: SavingsKpiSummaryBody | null): readonly KpiCellView[] {
  if (kpis === null) {
    return [
      { key: "contracts-analyzed", label: "Contracts analyzed", lines: [], meta: null },
      { key: "upcoming-renewals", label: "Upcoming renewals", lines: [], meta: null },
      { key: "savings-identified", label: "Savings identified", lines: [], meta: null },
      { key: "savings-verified", label: "Savings verified", lines: [], meta: null },
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
      meta: `${countOf(kpis.savingsIdentified)} identified · ${countOf(kpis.savingsInProgress)} in progress`,
    },
    {
      key: "savings-verified",
      label: "Savings verified",
      lines: verifiedLines(kpis),
      meta: verifiedMeta(kpis),
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
  /** Stable id for React keys -- `SavingsOpportunityBody.id`. */
  key: string;
  supplierLabel: string;
  supplierTitle: string | undefined;
  /** "Action" column: the opportunity's type (Renewal, Benchmark, …) -- the backend records no free-text action per opportunity. */
  action: string;
  estimate: string;
  /** The wire's own ISO 4217 code (e.g. "CHF"), kept alongside the formatted `estimate` string
   * purely so task-01-savings-filters' currency filter (`savingsFilters.ts`) can match on it --
   * `estimate` embeds this same code as a display prefix, but parsing a formatted range string back
   * into a filter key would be fragile. */
  currency: string;
  confidence: SemanticTag | null;
  status: SemanticTag;
  /** The wire's own closed status enum, kept alongside the presentation `status` tag purely so
   * task-01-savings-filters' status filter (`savingsFilters.ts`) can match the real value rather
   * than re-deriving it from a display label. */
  statusValue: SavingsOpportunityBody["status"];
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
    currency: item.currency,
    confidence: getSavingsConfidenceTag(item.confidence, item.confidenceLevel),
    status: getSavingsStatusTag(item.status),
    statusValue: item.status,
    navigation: getOpportunityNavigation(item.contractId),
  };
}

export function buildOpportunityRows(
  opportunities: readonly SavingsOpportunityBody[],
  supplierNames: ReadonlyMap<string, string> = new Map(),
): readonly OpportunityRowView[] {
  return opportunities.map((item) => buildRealOpportunityRow(item, supplierNames));
}

// ---------------------------------------------------------------------------------------------
// Filtering (task-01-savings-filters, ADR-020) -- pure presentation-layer restriction over rows
// already on screen; never a re-fetch, never persisted.
// ---------------------------------------------------------------------------------------------

/**
 * Applies the council's supplier/status/currency filter set (`savingsFilters.ts`) to the
 * already-built rows (AC-1). An all-`null` `filters` -- `EMPTY_SAVINGS_FILTERS`, the "Clear
 * filters" state -- matches every row, restoring the full list (AC-3).
 */
export function filterOpportunityRows(rows: readonly OpportunityRowView[], filters: SavingsFilterState): readonly OpportunityRowView[] {
  return rows.filter((row) => matchesSavingsFilters(row, filters));
}
