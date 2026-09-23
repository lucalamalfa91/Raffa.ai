import type { PortfolioPageBody, SavingsKpiSummaryBody, SavingsOpportunityBody } from "../../api/client";
import type { SemanticTag, TagVariant } from "../../styles/semantics";
import { isDeadlineCritical } from "../../styles/semantics";
import { formatSupplier } from "../contracts/portfolioTableFormatters";
import { buildPortfolioRows, buildPortfolioSelectionHref } from "../contracts/portfolioViewModel";
import { ASK_PROMPTS } from "../../components/ask-bar/askLaunch";
import { matchesSavingsFilters, type SavingsFilterState } from "./savingsFilters";

/**
 * Pure view-model helpers for the Savings screen (route `/savings`; a rail destination under "From
 * your contracts", also reached from Ask actions, Renewals and Contract 360; screens-v2.md #8). No
 * React here, so every rule below is unit-testable without rendering anything
 * (`savingsViewModel.test.ts`).
 *
 * This file owns the headline band (`buildKpiCells`), the portfolio-context strip
 * (`buildContextCells`) and the opportunities table rows; the charts between them live in
 * `savingsDashboard.ts`. The same three real fetches feed all of it: `GET /api/savings/kpis`,
 * `GET /api/savings`, and the portfolio for supplier names and notice dates
 * (`SavingsOpportunityResult` carries a supplier *id* only).
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
  key: "savings-verified" | "savings-identified" | "savings-in-progress" | "savings-potential";
  label: string;
  /**
   * One formatted line per currency bucket. Empty when `kpis` is `null` (not loaded / failed fetch:
   * `meta === null`) **or** when the tenant has no figure for that cell (loaded and empty:
   * `meta !== null`, rendered as "—" by `KpiRow.tsx`). Never a fabricated `0`.
   */
  lines: readonly string[];
  meta: string | null;
  /** Extra per-currency context under the meta ("1.4% of CHF annual spend"); empty when there is nothing honest to say. */
  notes: readonly string[];
  /** The one figure the dashboard leads with (verified money): larger type, accent rule. */
  hero: boolean;
}

function identifiedLines(kpis: SavingsKpiSummaryBody): string[] {
  return kpis.savingsIdentified.map((bucket) => formatCurrencyRange(bucket.currency, bucket.low, bucket.high));
}

function inProgressLines(kpis: SavingsKpiSummaryBody): string[] {
  return kpis.savingsInProgress.map((bucket) => formatCurrencyRange(bucket.currency, bucket.low, bucket.high));
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

function spendIn(kpis: SavingsKpiSummaryBody, currency: string): number | null {
  const bucket = kpis.annualSpendAnalyzed.find((candidate) => candidate.currency === currency);
  return bucket !== undefined && bucket.amount > 0 ? bucket.amount : null;
}

function formatShare(ratio: number): string {
  const percent = ratio * 100;
  if (percent > 0 && percent < 10) return `${percent.toFixed(1)}%`;
  return `${Math.round(percent)}%`;
}

function formatShareRange(low: number, high: number): string {
  const lowText = formatShare(low);
  const highText = formatShare(high);
  return lowText === highText ? lowText : `${lowText.replace("%", "")}–${highText}`;
}

/** "1.4% of CHF annual spend" -- verified money against the same currency's analyzed spend; omitted for a currency with no spend recorded. */
function verifiedShareNotes(kpis: SavingsKpiSummaryBody): string[] {
  return kpis.savingsRealized.flatMap((bucket) => {
    const spend = spendIn(kpis, bucket.currency);
    return spend === null || bucket.amount <= 0 ? [] : [`${formatShare(bucket.amount / spend)} of ${bucket.currency} annual spend`];
  });
}

function averageConfidenceMeta(kpis: SavingsKpiSummaryBody): string {
  const count = countOf(kpis.savingsIdentified);
  if (count === 0) return "no open opportunities yet";
  const weighted = kpis.savingsIdentified.reduce((total, bucket) => total + bucket.averageConfidence * bucket.count, 0) / count;
  return `${count} opportunit${count === 1 ? "y" : "ies"} · avg. confidence ${Math.round(weighted * 100)}%`;
}

function inProgressMeta(kpis: SavingsKpiSummaryBody): string {
  const count = countOf(kpis.savingsInProgress);
  if (count === 0) return "nothing being negotiated yet";
  return `${count} being negotiated`;
}

/**
 * "Savings potential": open estimates (identified + in progress) as a share of the same currency's
 * annual spend analyzed -- "6.5–9.4%". One line per currency that has both an estimate and a spend;
 * a currency with no spend recorded is left out rather than divided by nothing.
 */
function potentialLines(kpis: SavingsKpiSummaryBody): string[] {
  const currencies = Array.from(new Set([...kpis.savingsIdentified, ...kpis.savingsInProgress].map((bucket) => bucket.currency)));
  return currencies.flatMap((currency) => {
    const spend = spendIn(kpis, currency);
    if (spend === null) return [];
    const open = [...kpis.savingsIdentified, ...kpis.savingsInProgress].filter((bucket) => bucket.currency === currency);
    const low = open.reduce((total, bucket) => total + bucket.low, 0);
    const high = open.reduce((total, bucket) => total + bucket.high, 0);
    if (high <= 0) return [];
    const range = formatShareRange(low / spend, high / spend);
    return [currencies.length > 1 ? `${currency} ${range}` : range];
  });
}

/**
 * The four headline cells, in band order: verified money first (the figure the dashboard leads
 * with), then the open estimates -- identified, in progress -- and what they are worth against the
 * spend analyzed. Every meta line is a figure from the same `GET /api/savings/kpis` response.
 * Contracts analyzed and upcoming renewals are portfolio context, not savings: they live in the
 * context strip below the band (`buildContextCells`).
 */
export function buildKpiCells(kpis: SavingsKpiSummaryBody | null): readonly KpiCellView[] {
  if (kpis === null) {
    return [
      { key: "savings-verified", label: "Savings verified", lines: [], meta: null, notes: [], hero: true },
      { key: "savings-identified", label: "Savings identified", lines: [], meta: null, notes: [], hero: false },
      { key: "savings-in-progress", label: "Savings in progress", lines: [], meta: null, notes: [], hero: false },
      { key: "savings-potential", label: "Savings potential", lines: [], meta: null, notes: [], hero: false },
    ];
  }

  const potential = potentialLines(kpis);
  return [
    {
      key: "savings-verified",
      label: "Savings verified",
      lines: verifiedLines(kpis),
      meta: verifiedMeta(kpis),
      notes: verifiedShareNotes(kpis),
      hero: true,
    },
    {
      key: "savings-identified",
      label: "Savings identified",
      lines: identifiedLines(kpis),
      meta: averageConfidenceMeta(kpis),
      notes: [],
      hero: false,
    },
    {
      key: "savings-in-progress",
      label: "Savings in progress",
      lines: inProgressLines(kpis),
      meta: inProgressMeta(kpis),
      notes: [],
      hero: false,
    },
    {
      key: "savings-potential",
      label: "Savings potential",
      lines: potential,
      meta: potential.length > 0 ? "of annual spend analyzed, from open estimates" : "needs annual spend on validated contracts",
      notes: [],
      hero: false,
    },
  ];
}

export interface ContextCellView {
  key: "contracts-analyzed" | "annual-spend" | "upcoming-renewals" | "notice-soon";
  label: string;
  value: string;
  meta: string;
  /** Where the cell leads -- the screen that owns the figure. */
  href: string;
  linkLabel: string;
  /** Accent number (paired with its label, never colour alone) -- a notice deadline inside the 45-day window. */
  urgent: boolean;
}

/**
 * The portfolio-context strip under the KPI band: what the savings are measured against and where
 * the work happens. Contracts analyzed and spend come from the KPI response; notice deadlines in
 * the next 45 days from the portfolio rows (`noticeSoonIds`, `null` until the portfolio loads), and
 * lead to exactly those contracts in Portfolio.
 */
export function buildContextCells(kpis: SavingsKpiSummaryBody | null, noticeSoonIds: readonly string[] | null): readonly ContextCellView[] {
  const spend = kpis === null ? [] : kpis.annualSpendAnalyzed.map((bucket) => formatCurrencyAmount(bucket.currency, bucket.amount));
  const noticeCount = noticeSoonIds?.length ?? null;
  return [
    {
      key: "contracts-analyzed",
      label: "Contracts analyzed",
      value: kpis === null ? "—" : INTEGER_FORMAT.format(kpis.contractsAnalyzedCount),
      meta: "validated contracts behind these figures",
      href: "/contracts",
      linkLabel: "Portfolio",
      urgent: false,
    },
    {
      key: "annual-spend",
      label: "Annual spend analyzed",
      value: spend.length === 0 ? "—" : spend.join(" · "),
      meta: spend.length === 0 ? "no annual spend recorded yet" : "what the savings are measured against",
      href: "/contracts",
      linkLabel: "Portfolio",
      urgent: false,
    },
    {
      key: "upcoming-renewals",
      label: "Upcoming renewals",
      value: kpis === null ? "—" : INTEGER_FORMAT.format(kpis.upcomingRenewalsCount),
      meta: "auto-renewing contracts in the pipeline",
      href: "/renewals",
      linkLabel: "Renewals",
      urgent: false,
    },
    {
      key: "notice-soon",
      label: "Notice due in 45 days",
      value: noticeCount === null ? "—" : INTEGER_FORMAT.format(noticeCount),
      meta: noticeCount === null ? "from the portfolio's notice dates" : noticeCount === 0 ? "no notice deadline this close" : "move before these dates or they auto-renew",
      href: noticeSoonIds !== null && noticeSoonIds.length > 0 ? buildPortfolioSelectionHref(noticeSoonIds, "savings") : "/renewals",
      linkLabel: noticeSoonIds !== null && noticeSoonIds.length > 0 ? "Show them" : "Renewals",
      urgent: noticeCount !== null && noticeCount > 0,
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
  /** The contract the saving sits on, or `null` (a quote-sourced opportunity) -- what `?contract=` filters on. */
  contractId: string | null;
  supplierLabel: string;
  supplierTitle: string | undefined;
  /** "Lever" column: the opportunity's type (Renewal, Benchmark, …) -- the backend records no free-text action per opportunity. */
  action: string;
  /** "CHF 640,000" -- the spend the estimate is a saving on. */
  currentSpend: string;
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
  /** "Notice in" column: days to the contract's cancellation deadline (today or later), `null` without one. */
  noticeDays: number | null;
  /** Inside the locked 45-day window -- `.deadline-critical`, paired with the number. */
  noticeUrgent: boolean;
  /** The question the row's "Ask" link asks, bound to the row's contract. */
  askQuestion: string;
  /** Renewals with this contract selected -- only while a notice date is still ahead (an open saving there is worked as a renewal). */
  renewalHref: string | null;
}

/** Real name when the portfolio knows the contract's supplier; else the id-fragment fallback the Portfolio table also uses; else an honest placeholder. */
export function resolveOpportunitySupplier(
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

function buildRealOpportunityRow(
  item: SavingsOpportunityBody,
  supplierNames: ReadonlyMap<string, string>,
  noticeIndex: ReadonlyMap<string, number>,
): OpportunityRowView {
  const supplier = resolveOpportunitySupplier(item.contractId, item.supplierId, supplierNames);
  const noticeDays = item.contractId !== null ? (noticeIndex.get(item.contractId) ?? null) : null;
  const named = supplierNames.has(item.contractId ?? "");
  return {
    key: item.id,
    contractId: item.contractId,
    supplierLabel: supplier.label,
    supplierTitle: supplier.title,
    action: item.type,
    currentSpend: formatCurrencyAmount(item.currency, item.currentSpend),
    estimate: formatCurrencyRange(item.currency, item.estimatedSavingsLow, item.estimatedSavingsHigh),
    currency: item.currency,
    confidence: getSavingsConfidenceTag(item.confidence, item.confidenceLevel),
    status: getSavingsStatusTag(item.status),
    statusValue: item.status,
    navigation: getOpportunityNavigation(item.contractId),
    noticeDays,
    noticeUrgent: noticeDays !== null && isDeadlineCritical(noticeDays),
    askQuestion: ASK_PROMPTS.saveWithSupplier(named ? supplier.label : null),
    renewalHref: item.contractId !== null && noticeDays !== null && item.status !== "Realized" ? `/renewals?select=${encodeURIComponent(item.contractId)}` : null,
  };
}

/**
 * `noticeIndex` (contractId -> days to notice, `savingsDashboard.ts#buildNoticeIndex`) is optional:
 * without the portfolio the rows still render, with an honest "—" in "Notice in".
 */
export function buildOpportunityRows(
  opportunities: readonly SavingsOpportunityBody[],
  supplierNames: ReadonlyMap<string, string> = new Map(),
  noticeIndex: ReadonlyMap<string, number> = new Map(),
): readonly OpportunityRowView[] {
  return opportunities.map((item) => buildRealOpportunityRow(item, supplierNames, noticeIndex));
}

/** "14 d", or "—" without a notice date ahead. */
export function formatNoticeDays(days: number | null): string {
  return days === null ? "—" : `${days} d`;
}

/** Validated contracts whose notice deadline is today or within the locked 45-day window -- the context strip's "Notice due in 45 days". */
export function findNoticeSoonContractIds(items: PortfolioPageBody["items"], now: Date = new Date()): readonly string[] {
  return buildPortfolioRows(items, now)
    .filter((row) => row.isUrgent && row.cancelDays !== null && row.cancelDays >= 0)
    .map((row) => row.item.contractId);
}

/** "Show in Portfolio" for the rows on screen: the distinct contracts behind them, narrowed in Portfolio with Savings named as the source. */
export function buildOpportunitiesPortfolioHref(rows: readonly OpportunityRowView[]): string | null {
  const ids = Array.from(new Set(rows.map((row) => row.contractId).filter((id): id is string => id !== null)));
  return ids.length === 0 ? null : buildPortfolioSelectionHref(ids, "savings");
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
