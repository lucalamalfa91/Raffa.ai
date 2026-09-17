import { getContractTypeLabel, getPortfolioRiskTag, getPortfolioStatusTag, formatAnnualSpend, formatAutoRenewal, formatDateOnly } from "./portfolioTableFormatters";
import type { PortfolioRow } from "./portfolioViewModel";

/**
 * Column filters for the Portfolio table. Presentation-only over the already-loaded page
 * (same shape as Savings' supplier/status/currency filters): nothing here re-fetches or writes
 * to storage. Empty string / `null` means "no restriction on this column".
 */
export interface PortfolioColumnFilters {
  supplier: string;
  contract: string;
  spend: string;
  ends: string;
  notice: string;
  start: string;
  auto: string | null;
  risk: string | null;
  status: string | null;
}

export const EMPTY_PORTFOLIO_COLUMN_FILTERS: PortfolioColumnFilters = {
  supplier: "",
  contract: "",
  spend: "",
  ends: "",
  notice: "",
  start: "",
  auto: null,
  risk: null,
  status: null,
};

export function isPortfolioColumnFilterActive(filters: PortfolioColumnFilters): boolean {
  return (
    filters.supplier.trim() !== "" ||
    filters.contract.trim() !== "" ||
    filters.spend.trim() !== "" ||
    filters.ends.trim() !== "" ||
    filters.notice.trim() !== "" ||
    filters.start.trim() !== "" ||
    filters.auto !== null ||
    filters.risk !== null ||
    filters.status !== null
  );
}

function contains(haystack: string, needle: string): boolean {
  const trimmed = needle.trim();
  if (trimmed === "") return true;
  return haystack.toLowerCase().includes(trimmed.toLowerCase());
}

/** Same identifying label the Contract column paints (filename while pending, type otherwise). */
export function getPortfolioContractFilterLabel(row: PortfolioRow): string {
  if (row.isPending && row.item.fileName) return row.item.fileName;
  return getContractTypeLabel(row.item.type);
}

export function matchesPortfolioColumnFilters(row: PortfolioRow, filters: PortfolioColumnFilters): boolean {
  if (!contains(row.item.supplierName ?? "—", filters.supplier)) return false;
  if (!contains(getPortfolioContractFilterLabel(row), filters.contract)) return false;
  if (!contains(formatAnnualSpend(row.item.annualSpend), filters.spend)) return false;
  if (!contains(formatDateOnly(row.item.endDate), filters.ends)) return false;
  if (!contains(formatDateOnly(row.item.cancellationDeadline), filters.notice)) return false;
  if (!contains(formatDateOnly(row.item.startDate), filters.start)) return false;
  if (filters.auto !== null && formatAutoRenewal(row.item.autoRenewal) !== filters.auto) return false;
  if (filters.risk !== null && getPortfolioRiskTag(row.item.risk).label !== filters.risk) return false;
  if (filters.status !== null && getPortfolioStatusTag(row.item.status).label !== filters.status) return false;
  return true;
}

export function filterPortfolioRows(rows: readonly PortfolioRow[], filters: PortfolioColumnFilters): readonly PortfolioRow[] {
  if (!isPortfolioColumnFilterActive(filters)) return rows;
  return rows.filter((row) => matchesPortfolioColumnFilters(row, filters));
}

function distinctInOrder(values: readonly string[]): readonly string[] {
  return Array.from(new Set(values));
}

export function getPortfolioStatusFilterOptions(rows: readonly PortfolioRow[]): readonly string[] {
  return distinctInOrder(rows.map((row) => getPortfolioStatusTag(row.item.status).label));
}

export function getPortfolioRiskFilterOptions(rows: readonly PortfolioRow[]): readonly string[] {
  return distinctInOrder(rows.map((row) => getPortfolioRiskTag(row.item.risk).label));
}

export const PORTFOLIO_AUTO_FILTER_OPTIONS = ["Yes", "No"] as const;
