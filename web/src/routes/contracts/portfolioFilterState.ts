import type { PortfolioRiskSeverity } from "../../api/client";
import { ATTENTION_BUCKETS, daysUntil, type AttentionBucketKey, type AttentionRow } from "./portfolioAttention";

/**
 * Filter-chip state + predicate for the Portfolio screen (AC-1: "Filter chips (Supplier, Category,
 * Renewal period, Spend, Status, Risk, Auto-renewal)" -- the exact seven, in the exact order, quoted
 * from both the parent story's own AC-1 list and day1-demo.html's own `filters` array). See
 * `index.tsx`'s header comment for why filtering runs client-side over an already-fetched, unfiltered
 * portfolio rather than round-tripping each chip to `GET /api/contracts`'s own server-side filters.
 *
 * Two chips are honest placeholders, not full-strength filters, because the data they would need does
 * not exist yet anywhere in this codebase (flagged here, not silently dropped -- PortfolioFilters.tsx
 * disables/labels them accordingly):
 *  - **Category**: `PortfolioFilter.cs`'s own doc comment: "No Category concept exists anywhere in the
 *    currently-implemented schema" (Suppliers/Products is still an empty scaffold). This filter is
 *    disabled, not removed, so a later task can wire it in without re-deriving AC-1's chip layout.
 *  - **Supplier**: no supplier-name-resolution endpoint exists (see `portfolioTableFormatters.ts#formatSupplier`'s
 *    own comment) -- this filters by the same raw id `GET /api/contracts` itself returns and accepts
 *    as a query parameter, entered as free text rather than picked from a name list.
 */

export type AutoRenewalFilterValue = "any" | "yes" | "no";

export interface PortfolioFilterState {
  /** Free-text Contract.SupplierId (a GUID) -- see this file's header comment; "" = not filtered. */
  supplierId: string;
  /** Free-text, case-insensitive match against the free-text Contract.Status; "" = not filtered. */
  status: string;
  /** "" = not filtered (the backend's own PortfolioFilter.Risk is likewise optional). */
  risk: PortfolioRiskSeverity | "";
  autoRenewal: AutoRenewalFilterValue;
  /** Free-text number input; "" = not filtered. */
  minAnnualSpend: string;
  /** Free-text number input; "" = not filtered. */
  maxAnnualSpend: string;
  /** Whole days from today; "" = Any (the backend's own PortfolioFilter.RenewalFrom/To are likewise optional). */
  renewalWithinDays: number | "";
  /** Set by clicking an AttentionStrip cell (AC-2 "click = filter"); null = no bucket selected. */
  attentionBucket: AttentionBucketKey | null;
}

export const EMPTY_PORTFOLIO_FILTERS: PortfolioFilterState = {
  supplierId: "",
  status: "",
  risk: "",
  autoRenewal: "any",
  minAnnualSpend: "",
  maxAnnualSpend: "",
  renewalWithinDays: "",
  attentionBucket: null,
};

/**
 * Renewal-period presets (AC-1 "Renewal period"). 120 days is quoted verbatim from spec §9.1's own
 * example question ("Which contracts renew in the next 120 days?"), already cited in
 * `PortfolioListItem.cs`'s own doc comment; the shorter windows are this screen's own reasonable
 * subdivisions of it, not a separate spec citation.
 */
export const RENEWAL_WINDOW_PRESETS: readonly { label: string; days: number | "" }[] = [
  { label: "Any", days: "" },
  { label: "Next 30 days", days: 30 },
  { label: "Next 60 days", days: 60 },
  { label: "Next 90 days", days: 90 },
  { label: "Next 120 days", days: 120 },
];

export function isAnyPortfolioFilterActive(filters: PortfolioFilterState): boolean {
  return (
    filters.supplierId !== "" ||
    filters.status !== "" ||
    filters.risk !== "" ||
    filters.autoRenewal !== "any" ||
    filters.minAnnualSpend !== "" ||
    filters.maxAnnualSpend !== "" ||
    filters.renewalWithinDays !== "" ||
    filters.attentionBucket !== null
  );
}

/**
 * AC-1 + AC-2 combined: every chip is an AND condition over the already-computed attention rows (see
 * `portfolioAttention.ts`), so the attention-strip toggle and the seven chips compose freely (e.g.
 * "High risk" strip + a minimum spend). `now` is threaded through for the renewal-period window, the
 * same explicit-clock convention `portfolioAttention.ts#daysUntil` already uses.
 */
export function applyPortfolioFilters(
  rows: readonly AttentionRow[],
  filters: PortfolioFilterState,
  now: Date = new Date(),
): AttentionRow[] {
  const minSpend = filters.minAnnualSpend === "" ? null : Number(filters.minAnnualSpend);
  const maxSpend = filters.maxAnnualSpend === "" ? null : Number(filters.maxAnnualSpend);
  const activeBucket =
    filters.attentionBucket === null ? null : ATTENTION_BUCKETS.find((bucket) => bucket.key === filters.attentionBucket);

  return rows.filter((row) => {
    const item = row.item;

    if (filters.supplierId !== "" && item.supplierId !== filters.supplierId) return false;
    if (filters.status !== "" && item.status.trim().toLowerCase() !== filters.status.trim().toLowerCase()) return false;
    if (filters.risk !== "" && item.risk !== filters.risk) return false;
    if (filters.autoRenewal === "yes" && !item.autoRenewal) return false;
    if (filters.autoRenewal === "no" && item.autoRenewal) return false;
    if (minSpend !== null && (item.annualSpend === null || item.annualSpend < minSpend)) return false;
    if (maxSpend !== null && (item.annualSpend === null || item.annualSpend > maxSpend)) return false;

    if (filters.renewalWithinDays !== "") {
      // Mirrors the backend's own RenewalFrom/RenewalTo semantics (PortfolioQueryService.cs): only an
      // auto-renewing contract's EndDate answers "renews within N days" at all.
      if (!item.autoRenewal || item.endDate === null) return false;
      const days = daysUntil(item.endDate, now);
      if (days === null || days < 0 || days > filters.renewalWithinDays) return false;
    }

    if (activeBucket !== null && activeBucket !== undefined && !activeBucket.test(row)) return false;

    return true;
  });
}
