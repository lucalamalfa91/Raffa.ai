import type { PortfolioListItem } from "../../api/client";
import { isDeadlineCritical } from "../../styles/semantics";
import { isValidatedContractStatus } from "./contractStatus";
import { daysUntil } from "./portfolioAttention";

/**
 * Pure view-model for the V2 Portfolio screen (route `/contracts`; ADR-024 V2 IA; screens-v2.md #6;
 * `contigo-v2/app.jsx` `kbContracts` / `pfSummary` / `moreCols`). No React here -- every rule is
 * unit-testable without rendering (`portfolioViewModel.test.ts`).
 *
 * **Validated contracts only, sorted by the notice deadline.** `app.jsx`: `kbContracts =
 * completedCids.map(...).sort((a,b)=>a.cancelDays-b.cancelDays)` -- the Portfolio "lights up from
 * validated contracts" (V2 principle: only `completed` documents feed Ask, Portfolio and Renewals);
 * a contract still processing or waiting for review lives on the Documents screen, not here. Rows
 * are ordered by how soon notice must be given, soonest first; a row with no deadline sorts after
 * every row that has one, then by end date, then by id for a deterministic tiebreak.
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

export interface PortfolioRow {
  item: PortfolioListItem;
  /** Days until the cancellation deadline (negative once past), `null` without a deadline. */
  cancelDays: number | null;
  /** Notice due within the locked 45-day window (`isDeadlineCritical`): accent tint + bar + bold date. */
  isUrgent: boolean;
}

export function buildPortfolioRows(items: readonly PortfolioListItem[], now: Date = new Date()): PortfolioRow[] {
  return items
    .filter((item) => isValidatedContractStatus(item.status))
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
  const totals = new Map<string | null, number>();
  for (const { item } of rows) {
    if (item.annualSpend === null) continue;
    const key = item.currency ?? null;
    totals.set(key, (totals.get(key) ?? 0) + item.annualSpend);
  }
  const annualSpend = [...totals.entries()]
    .map(([currency, total]) => ({ currency, total }))
    .sort((a, b) => b.total - a.total);

  return {
    validatedCount: rows.length,
    annualSpend,
    urgentCount: rows.filter((row) => row.isUrgent).length,
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
