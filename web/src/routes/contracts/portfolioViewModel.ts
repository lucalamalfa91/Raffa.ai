import type { PortfolioListItem } from "../../api/client";
import { isDeadlineCritical } from "../../styles/semantics";
import { isValidatedContractStatus } from "./contractStatus";
import { daysUntil } from "./portfolioAttention";

/**
 * Pure view-model for the V2 Portfolio screen (route `/contracts`; ADR-024 V2 IA; screens-v2.md #6;
 * `raffa-v2/app.jsx` `kbContracts` / `pfSummary` / `moreCols`). No React here -- every rule is
 * unit-testable without rendering (`portfolioViewModel.test.ts`).
 *
 * **All uploaded contracts, sorted by the notice deadline (pending rows last).** w17 change: a
 * contract is visible as soon as its document is uploaded -- no longer waiting for extraction to
 * complete. Pending rows (documentProcessingStatus = Uploaded or Processing) appear after all
 * validated rows (sorted by notice deadline), clearly labelled as still-processing via `isPending`.
 * The summary "validatedCount" and spend totals still count only validated (non-pending) rows so
 * the headline figure stays accurate.
 *
 * **Urgent = notice due within 45 days.** `app.jsx`: `rowBg: c.cancelDays<=45 ? accent-100 :
 * transparent`, `bar: accent`, `cancelFg: accent-700`, `cancelW: 600` -- the same locked ADR-019
 * threshold `styles/semantics.ts#isDeadlineCritical` already encodes (never re-derived here).
 *
 * **The summary is per currency.** `pfSummary`: "N validated contracts · CHF 4.2M annual · K notice
 * deadline(s) within 45 days" -- `app.jsx` sums a single-currency fixture; real rows carry their own
 * `currency` (added to `GET /api/contracts` for exactly this line), so the total is summed per
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

export interface PortfolioRow {
  item: PortfolioListItem;
  /** Days until the cancellation deadline (negative once past), `null` without a deadline. */
  cancelDays: number | null;
  /** Notice due within the locked 45-day window (`isDeadlineCritical`): accent tint + bar + bold date. */
  isUrgent: boolean;
  /**
   * w17: true when the linked document is still in `Uploaded` or `Processing` state, meaning
   * extraction has not completed yet. Pending rows show the filename as the primary identifier
   * instead of the contract-type label, and are sorted after all validated rows.
   */
  isPending: boolean;
}

/**
 * Returns true when the document linked to this portfolio item is still being processed.
 * The check is on `documentProcessingStatus` (the new w17 field) so the pending flag is
 * authoritative even if `Contract.Status` has already been updated by a partial extraction stage.
 */
function isDocumentPending(documentProcessingStatus: string | null | undefined): boolean {
  if (!documentProcessingStatus) return false;
  const s = documentProcessingStatus.toLowerCase();
  return s === "uploaded" || s === "processing";
}

export function buildPortfolioRows(items: readonly PortfolioListItem[], now: Date = new Date()): PortfolioRow[] {
  return items
    // w17: include both validated contracts AND freshly-uploaded (pending) ones.
    // - Pending (documentProcessingStatus = Uploaded|Processing): shown immediately after upload
    //   so the user sees their document appear at once.
    // - Validated (isValidatedContractStatus = true): shown with full extracted data.
    // - needs_review / failed / etc.: still excluded — they have their own Documents screen.
    .filter((item) => isValidatedContractStatus(item.status) || isDocumentPending(item.documentProcessingStatus))
    .map((item) => {
      const cancelDays = daysUntil(item.cancellationDeadline, now);
      const isPending = isDocumentPending(item.documentProcessingStatus);
      return { item, cancelDays, isUrgent: cancelDays !== null && isDeadlineCritical(cancelDays), isPending };
    })
    .sort(compareByNoticeDeadline);
}

function compareByNoticeDeadline(a: PortfolioRow, b: PortfolioRow): number {
  // w17: pending rows (still processing) sort after all validated rows regardless of deadline.
  if (a.isPending !== b.isPending) return a.isPending ? 1 : -1;

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
  // w17: only validated (non-pending) rows contribute to the summary figures so the headline
  // "N validated contracts · CHF X annual · K notice deadlines" stays accurate. Pending rows
  // are shown in the table but not counted here (they have no extracted spend or deadlines yet).
  const validatedRows = rows.filter((row) => !row.isPending);

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
