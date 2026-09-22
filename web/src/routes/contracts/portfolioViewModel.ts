import type { PortfolioListItem } from "../../api/client";
import { isDeadlineCritical } from "../../styles/semantics";
import { isContractReadyToUse } from "./contractStatus";
import { daysUntil } from "./portfolioAttention";

/**
 * Pure view-model for the V2 Portfolio screen (route `/contracts`; ADR-024 V2 IA; `Raffa.ai V2.dc.html`
 * PORTFOLIO block, `kbContracts` / `pfSummary` / `moreCols` in its `text/x-dc` logic). No React here --
 * every rule is unit-testable without rendering (`portfolioViewModel.test.ts`).
 *
 * **Validated contracts only.** `kbContracts = completedCids.map(...)`: the prototype's table lists
 * the contracts whose document is `completed`, nothing else -- a still-processing or needs-review
 * upload belongs to Documents (the summary line and the rail badge count the same set). "Validated"
 * is `contractStatus.ts#isContractReadyToUse`, the one shared predicate, so this screen can never
 * show a contract the rail's own "From your contracts" count excludes.
 *
 * **Sorted by how soon notice must be given.** `.sort((a,b)=>a.cancelDays-b.cancelDays)`; rows with
 * no deadline at all go last, ties break on end date then id so the order is stable across
 * re-reads.
 *
 * **Urgent = notice due within 45 days.** `hot = c.cancelDays<=45`: `rowBg` accent-100, `bar`
 * accent, `cancelFg` accent-700, `cancelW` 600 -- the same locked ADR-019 threshold
 * `styles/semantics.ts#isDeadlineCritical` already encodes (never re-derived here).
 *
 * **The summary is per currency.** `pfSummary`: "N validated contracts · CHF 4.2M annual · K notice
 * deadline(s) within 45 days" -- the prototype sums a single-currency fixture; real rows carry their
 * own `currency` (added to `GET /api/contracts` for exactly this line), so the total is summed per
 * currency and every currency present is named. A row with no annual spend contributes nothing.
 */

/**
 * The category filter's URL query key (task E24/F01/US02/T01, story
 * us-02-portfolio-category-web; closes NW-23). This filter's only state is the route's own search
 * params -- ADR-012 "a client store never stands in for a missing GET" -- read fresh on every
 * render, never cached in a component store or `sessionStorage`/`localStorage`. Named to match the
 * backend's own query parameter verbatim (`PortfolioEndpointExtensions.TryParseFilter`,
 * `PortfolioQueryParams.category` in `api/client.ts`).
 */
export const PORTFOLIO_CATEGORY_PARAM = "category";

/**
 * Reads the category filter from the route's search params. Blank and absent both normalize to
 * `""` -- the backend's own "blank or absent leaves the full portfolio" contract
 * (us-01-portfolio-category-backend AC-2) -- so callers never need a second branch for the
 * empty-string case, and trims the same way `PortfolioEndpointExtensions.TryParseFilter` trims the
 * wire value.
 */
export function readCategoryFilter(searchParams: URLSearchParams): string {
  return searchParams.get(PORTFOLIO_CATEGORY_PARAM)?.trim() ?? "";
}

/**
 * Returns the search params that follow from applying `category` (or clearing it, via `""` or a
 * whitespace-only string) -- every other query parameter this screen does not own is carried over
 * untouched, so the result can be handed straight to `useSearchParams`'s setter.
 */
export function withCategoryFilter(searchParams: URLSearchParams, category: string): URLSearchParams {
  const next = new URLSearchParams(searchParams);
  const trimmed = category.trim();
  if (trimmed.length > 0) {
    next.set(PORTFOLIO_CATEGORY_PARAM, trimmed);
  } else {
    next.delete(PORTFOLIO_CATEGORY_PARAM);
  }
  return next;
}

/**
 * `?ids=<id>,<id>` -- the contracts an Ask answer highlighted (`routes/ask/reply/evidenceGrouping.ts
 * #buildEvidenceActions` builds the link from the citations' own `contractId`s, never from the
 * model). Read from the URL on every render like `category`, so the link survives a reload and a
 * share. Unknown or deleted ids simply match nothing; the notice on the screen says so.
 */
export const PORTFOLIO_IDS_PARAM = "ids";

export function readContractIdsFilter(searchParams: URLSearchParams): readonly string[] {
  const raw = searchParams.get(PORTFOLIO_IDS_PARAM);
  if (raw === null) return [];
  const seen = new Set<string>();
  const ids: string[] = [];
  for (const part of raw.split(",")) {
    const id = part.trim();
    if (id === "" || seen.has(id)) continue;
    seen.add(id);
    ids.push(id);
  }
  return ids;
}

export function buildPortfolioHighlightHref(contractIds: readonly string[]): string {
  const ids = readContractIdsFilter(new URLSearchParams({ [PORTFOLIO_IDS_PARAM]: contractIds.join(",") }));
  if (ids.length === 0) return "/contracts";
  return `/contracts?${PORTFOLIO_IDS_PARAM}=${ids.map(encodeURIComponent).join(",")}`;
}

export function filterRowsByContractIds(rows: readonly PortfolioRow[], contractIds: readonly string[]): PortfolioRow[] {
  if (contractIds.length === 0) return [...rows];
  const wanted = new Set(contractIds);
  return rows.filter((row) => wanted.has(row.item.contractId));
}

/** The notice above a filtered table: how many of the highlighted contracts are still here. */
export function formatHighlightNotice(shown: number, total: number): string {
  if (shown === 0) {
    return "None of the contracts highlighted in Ask is in the portfolio any more.";
  }
  const contracts = total === 1 ? "contract" : "contracts";
  return `Showing ${shown} of ${total} ${contracts} — the ones highlighted in Ask.`;
}

export interface PortfolioRow {
  item: PortfolioListItem;
  /** Days until the cancellation deadline (negative once past), `null` without a deadline. */
  cancelDays: number | null;
  /** Notice due within the locked 45-day window (`isDeadlineCritical`): accent tint + bar + bold date. */
  isUrgent: boolean;
}

/**
 * Validated / OK to use -- the only rows this screen lists (`completedCids`). Same rule Renewals
 * uses (`isContractReadyToUse`): not "has dates".
 */
export function isPortfolioItemReady(item: PortfolioListItem): boolean {
  return isContractReadyToUse(item.status, item.documentProcessingStatus);
}

export function buildPortfolioRows(items: readonly PortfolioListItem[], now: Date = new Date()): PortfolioRow[] {
  return items
    .filter(isPortfolioItemReady)
    .map((item) => {
      const cancelDays = daysUntil(item.cancellationDeadline, now);
      return { item, cancelDays, isUrgent: cancelDays !== null && isDeadlineCritical(cancelDays) };
    })
    .sort(compareByNoticeDeadline);
}

function compareByNoticeDeadline(a: PortfolioRow, b: PortfolioRow): number {
  if (a.cancelDays !== b.cancelDays) {
    if (a.cancelDays === null) return 1;
    if (b.cancelDays === null) return -1;
    return a.cancelDays - b.cancelDays;
  }
  const endA = a.item.endDate ?? "";
  const endB = b.item.endDate ?? "";
  if (endA !== endB) {
    if (endA === "") return 1;
    if (endB === "") return -1;
    return endA < endB ? -1 : 1;
  }
  return a.item.contractId < b.item.contractId ? -1 : a.item.contractId > b.item.contractId ? 1 : 0;
}

export interface AnnualSpendTotal {
  /** ISO code, or `null` for rows the API reported without one (never a guessed currency). */
  currency: string | null;
  total: number;
}

export interface PortfolioSummary {
  validatedCount: number;
  /** One entry per currency present, largest total first. */
  annualSpend: readonly AnnualSpendTotal[];
  urgentCount: number;
}

export function buildPortfolioSummary(rows: readonly PortfolioRow[]): PortfolioSummary {
  // Every row is already a validated contract (`buildPortfolioRows`), so the headline
  // "N validated contracts · CHF X annual · K notice deadlines" counts the whole list.
  const validatedRows = rows;

  const totals = new Map<string | null, number>();
  for (const { item } of validatedRows) {
    if (item.annualSpend === null) continue;
    const key = item.currency ?? null;
    totals.set(key, (totals.get(key) ?? 0) + item.annualSpend);
  }
  const annualSpend = [...totals.entries()]
    .map(([currency, total]) => ({ currency, total }))
    .sort((a, b) => b.total - a.total);

  return {
    validatedCount: validatedRows.length,
    annualSpend,
    urgentCount: validatedRows.filter((row) => row.isUrgent).length,
  };
}

/**
 * `app.jsx#chf`, generalised to any currency: `n>=1e6 ? 'CHF '+(n/1e6).toFixed(1)+'M' : 'CHF '+
 * Math.round(n/1000)+'k'` -- plus a plain figure under 1,000 (the prototype's fixtures never go that
 * low) and no code at all when the row carried none.
 */
export function formatCompactAmount(amount: number, currency: string | null): string {
  const figure =
    amount >= 1_000_000
      ? `${(amount / 1_000_000).toFixed(1)}M`
      : amount >= 1_000
        ? `${Math.round(amount / 1_000)}k`
        : new Intl.NumberFormat("en-GB").format(Math.round(amount));
  return currency === null ? figure : `${currency} ${figure}`;
}

/** `pfSummary` when the tier is off (`kbOff`): the prototype's own fixed line. */
export const PORTFOLIO_SUMMARY_OFF = "Lights up from validated contracts";

/**
 * `pfSummary` when lit: "N validated contract(s) · <spend> annual · K notice deadline(s) within 45
 * days" (the deadline clause only when K > 0, exactly as `app.jsx` appends it).
 */
export function formatPortfolioSummary(summary: PortfolioSummary): string {
  if (summary.validatedCount === 0) return PORTFOLIO_SUMMARY_OFF;

  const parts = [`${summary.validatedCount} validated contract${summary.validatedCount === 1 ? "" : "s"}`];
  if (summary.annualSpend.length > 0) {
    parts.push(`${summary.annualSpend.map((entry) => formatCompactAmount(entry.total, entry.currency)).join(" + ")} annual`);
  }
  if (summary.urgentCount > 0) {
    parts.push(`${summary.urgentCount} notice deadline${summary.urgentCount === 1 ? "" : "s"} within 45 days`);
  }
  return parts.join(" · ");
}

/** `colsLabel`: the "More columns" toggle's own label, quoted from `app.jsx`. */
export function moreColumnsLabel(expanded: boolean): string {
  return expanded ? "Fewer columns" : "More columns";
}
