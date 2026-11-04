import type { ContractListRowV4Body } from "../../api/client";

/**
 * Pure view-model for the Contratti list: filter and sort the rows the API returned. No React here,
 * so every rule is unit-testable (`contractsListView.test.ts`).
 *
 * Default order = the API's own: nearest notice deadline first, rows without a stated deadline last.
 * Annual values are never converted between currencies (no rate source): sorting by value groups
 * rows by currency code first, then orders the amounts inside each currency.
 */

export type ContractSortKey = "supplier" | "notice" | "days" | "end" | "value" | "status";
export type SortDirection = "asc" | "desc";
export interface ContractSort {
  key: ContractSortKey;
  dir: SortDirection;
}
export interface ContractFilters {
  query: string;
  status: ContractListRowV4Body["status"] | "";
  currency: string;
}

export const DEFAULT_SORT: ContractSort = { key: "notice", dir: "asc" };
export const NO_FILTERS: ContractFilters = { query: "", status: "", currency: "" };

const MONTHS = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

/** "15 Apr 2027" → a sortable number; unparsable text → NaN (sorted last). */
export function displayDateKey(text: string): number {
  const m = /^\s*(\d{1,2})\s+([A-Za-z]{3})[A-Za-z]*\.?\s+(\d{4})\s*$/.exec(text);
  if (m) {
    const month = MONTHS.indexOf(m[2].toLowerCase());
    if (month >= 0) return Date.UTC(Number(m[3]), month, Number(m[1]));
  }
  const parsed = Date.parse(text);
  return Number.isNaN(parsed) ? Number.NaN : parsed;
}

export function isFiltered(f: ContractFilters): boolean {
  return f.query.trim() !== "" || f.status !== "" || f.currency !== "";
}

export function filterContracts(rows: readonly ContractListRowV4Body[], f: ContractFilters): ContractListRowV4Body[] {
  const q = f.query.trim().toLocaleLowerCase();
  return rows.filter(
    (r) =>
      (q === "" || r.supplier.toLocaleLowerCase().includes(q) || r.name.toLocaleLowerCase().includes(q)) &&
      (f.status === "" || r.status === f.status) &&
      (f.currency === "" || (r.currency || "CHF").toUpperCase() === f.currency),
  );
}

export function currenciesOf(rows: readonly ContractListRowV4Body[]): string[] {
  return [...new Set(rows.map((r) => (r.currency || "CHF").toUpperCase()))].sort();
}

/** Missing values (NaN) go last in both directions, so "empty" never leads a list. */
function compareNullable(a: number, b: number, dir: SortDirection): number {
  const an = Number.isNaN(a);
  const bn = Number.isNaN(b);
  if (an || bn) return an === bn ? 0 : an ? 1 : -1;
  return dir === "asc" ? a - b : b - a;
}

function compareText(a: string, b: string, dir: SortDirection): number {
  const c = a.localeCompare(b, undefined, { sensitivity: "base", numeric: true });
  return dir === "asc" ? c : -c;
}

const noticeKey = (r: ContractListRowV4Body): number => (r.noticeDeadline.trim() === "" ? Number.NaN : r.noticeDays);

export function sortContracts(
  rows: readonly ContractListRowV4Body[],
  sort: ContractSort,
  statusLabel: (s: ContractListRowV4Body["status"]) => string = (s) => s,
): ContractListRowV4Body[] {
  const { key, dir } = sort;
  const primary = (a: ContractListRowV4Body, b: ContractListRowV4Body): number => {
    switch (key) {
      case "supplier":
        return compareText(a.supplier, b.supplier, dir);
      case "notice":
      case "days":
        return compareNullable(noticeKey(a), noticeKey(b), dir);
      case "end":
        return compareNullable(displayDateKey(a.end), displayDateKey(b.end), dir);
      case "value": {
        const byCurrency = (a.currency || "CHF").toUpperCase().localeCompare((b.currency || "CHF").toUpperCase());
        return byCurrency !== 0 ? byCurrency : compareNullable(a.annualValue, b.annualValue, dir);
      }
      case "status":
        return compareText(statusLabel(a.status), statusLabel(b.status), dir);
    }
  };
  // Stable tie-breaks: notice deadline, then contract end, then id.
  return [...rows].sort(
    (a, b) =>
      primary(a, b) ||
      compareNullable(noticeKey(a), noticeKey(b), "asc") ||
      compareNullable(displayDateKey(a.end), displayDateKey(b.end), "asc") ||
      a.contractId.localeCompare(b.contractId),
  );
}

/** Click on a header: a new column sorts ascending (value: descending first); the same column flips. */
export function nextSort(current: ContractSort, key: ContractSortKey): ContractSort {
  if (current.key === key) return { key, dir: current.dir === "asc" ? "desc" : "asc" };
  return { key, dir: key === "value" ? "desc" : "asc" };
}
