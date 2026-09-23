import type { SavingsOpportunityBody } from "../../api/client";

/**
 * Pure filter predicates for the Savings opportunities table (task-01-savings-filters, ADR-020 --
 * presentation only, no client store; screens-v2.md #8; parent story us-01-savings-filters). The
 * council's filter set is exactly three dimensions -- supplier, status, currency (AC-2); "Estimate"
 * stays a sort/numeric column and has no member here at all. Filtering is pure client-side view
 * state over the already-loaded `getSavingsOpportunities` rows: nothing here fetches or writes to
 * storage, and the all-`null` state a fresh page load and "Clear filters" both produce always
 * matches every row (AC-3).
 *
 * `SavingsFilterableOpportunity` is a minimal structural shape rather than an import of
 * `OpportunityRowView` from `savingsViewModel.ts`: this module has zero dependency on that file (or
 * on React), so its own unit tests build plain-object fixtures without calling
 * `buildOpportunityRows`, and `savingsViewModel.ts` is free to import from this module (for
 * `filterOpportunityRows`) without a cycle.
 */
export interface SavingsFilterableOpportunity {
  supplierLabel: string;
  currency: string;
  statusValue: SavingsOpportunityBody["status"];
  /** Optional in this structural shape so plain fixtures stay minimal; `OpportunityRowView` always carries it. */
  contractId?: string | null;
}

/** `null` in any field means "no restriction" -- the state a fresh page load and "Clear filters"
 * both produce. Never persisted to storage (AC-3). `contractId` is the one dimension with no
 * dropdown: it is set only by a deep link (`?contract=`, from Contract 360 or Renewals) and shown
 * as a removable focus notice above the table. */
export interface SavingsFilterState {
  supplier: string | null;
  status: SavingsOpportunityBody["status"] | null;
  currency: string | null;
  contractId: string | null;
}

/** The cleared state: every dimension unrestricted, matching every row (AC-3). */
export const EMPTY_SAVINGS_FILTERS: SavingsFilterState = { supplier: null, status: null, currency: null, contractId: null };

/** Query keys a link into Savings may carry. Read once, on mount -- the same convention Renewals' `?select=` follows. */
export const SAVINGS_CONTRACT_PARAM = "contract";
export const SAVINGS_STATUS_PARAM = "status";

/**
 * The filter state a deep link asks for: `?contract=<id>` focuses one contract, `?status=` one of
 * the three closed statuses. Anything else (blank, an unknown status) is ignored, never an error.
 */
export function readSavingsFiltersFromSearch(searchParams: URLSearchParams): SavingsFilterState {
  const contractId = searchParams.get(SAVINGS_CONTRACT_PARAM)?.trim() ?? "";
  const rawStatus = searchParams.get(SAVINGS_STATUS_PARAM)?.trim() ?? "";
  const status = SAVINGS_STATUS_FILTER_OPTIONS.find((option) => option === rawStatus) ?? null;
  return { ...EMPTY_SAVINGS_FILTERS, contractId: contractId === "" ? null : contractId, status };
}

/** Contract 360 / Renewals -> Savings with that contract's opportunities in focus. */
export function buildSavingsContractHref(contractId: string): string {
  return `/savings?${SAVINGS_CONTRACT_PARAM}=${encodeURIComponent(contractId)}`;
}

/** The council's closed three-value status set (AC-2), in the wire's own order -- the same three
 * values `savingsViewModel.ts#getSavingsStatusTag` switches on. The filter can never offer a
 * fourth, invented category. */
export const SAVINGS_STATUS_FILTER_OPTIONS: readonly SavingsOpportunityBody["status"][] = ["Identified", "InProgress", "Realized"];

/** True unless some active filter dimension disagrees with this row; an all-`null` `filters`
 * (`EMPTY_SAVINGS_FILTERS`) always returns true, which is what restores the full list on Clear. */
export function matchesSavingsFilters(row: SavingsFilterableOpportunity, filters: SavingsFilterState): boolean {
  if (filters.supplier !== null && row.supplierLabel !== filters.supplier) return false;
  if (filters.status !== null && row.statusValue !== filters.status) return false;
  if (filters.currency !== null && row.currency !== filters.currency) return false;
  if (filters.contractId !== null && (row.contractId ?? null) !== filters.contractId) return false;
  return true;
}

function distinctInOrder(values: readonly string[]): readonly string[] {
  return Array.from(new Set(values));
}

/** Distinct supplier labels actually present in the already-loaded rows, first-seen order --
 * resolved through the same `getPortfolio` name index the table itself renders (`resolveSupplier` in
 * savingsViewModel.ts), so the filter never offers a supplier absent from every current opportunity. */
export function getSupplierFilterOptions(rows: readonly SavingsFilterableOpportunity[]): readonly string[] {
  return distinctInOrder(rows.map((row) => row.supplierLabel));
}

/** Distinct currencies actually present in the already-loaded rows -- V1 has no fixed tenant
 * currency (the same "group by currency, never sum across them" discipline `savingsViewModel.ts`'s
 * own `formatCurrencyAmount` comment states), so this is never a hardcoded list either. */
export function getCurrencyFilterOptions(rows: readonly SavingsFilterableOpportunity[]): readonly string[] {
  return distinctInOrder(rows.map((row) => row.currency));
}
