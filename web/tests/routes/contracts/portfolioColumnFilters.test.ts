import { describe, expect, it } from "vitest";
import type { PortfolioListItem } from "../../../src/api/client";
import { buildPortfolioRows } from "../../../src/routes/contracts/portfolioViewModel";
import {
  EMPTY_PORTFOLIO_COLUMN_FILTERS,
  filterPortfolioRows,
  getPortfolioRiskFilterOptions,
  getPortfolioStatusFilterOptions,
  isPortfolioColumnFilterActive,
  matchesPortfolioColumnFilters,
} from "../../../src/routes/contracts/portfolioColumnFilters";

const NOW = new Date(Date.UTC(2026, 8, 9));

function item(overrides: Partial<PortfolioListItem> = {}): PortfolioListItem {
  return {
    contractId: "contract-1",
    supplierId: null,
    supplierName: "Salesforce",
    type: "Msa",
    annualSpend: 640_000,
    currency: "CHF",
    startDate: "2024-03-01",
    endDate: "2027-01-15",
    renewalDate: "2027-01-15",
    cancellationDeadline: "2026-10-18",
    autoRenewal: true,
    status: "active",
    risk: "High",
    fileName: null,
    documentProcessingStatus: "Completed",
    ...overrides,
  };
}

const ROWS = buildPortfolioRows(
  [
    item({ contractId: "sf", supplierName: "Salesforce", status: "active", risk: "High", autoRenewal: true }),
    item({ contractId: "ms", supplierName: "Microsoft", status: "expired", risk: "Low", autoRenewal: false, type: "Sow" }),
  ],
  NOW,
);

describe("portfolioColumnFilters", () => {
  it("matches every row when no filter is active", () => {
    expect(filterPortfolioRows(ROWS, EMPTY_PORTFOLIO_COLUMN_FILTERS)).toEqual(ROWS);
    expect(isPortfolioColumnFilterActive(EMPTY_PORTFOLIO_COLUMN_FILTERS)).toBe(false);
  });

  it("restricts by supplier contains, case-insensitive", () => {
    const filtered = ROWS.filter((row) =>
      matchesPortfolioColumnFilters(row, { ...EMPTY_PORTFOLIO_COLUMN_FILTERS, supplier: "micro" }),
    );
    expect(filtered.map((row) => row.item.contractId)).toEqual(["ms"]);
  });

  it("restricts by contract type label", () => {
    const filtered = filterPortfolioRows(ROWS, { ...EMPTY_PORTFOLIO_COLUMN_FILTERS, contract: "SOW" });
    expect(filtered.map((row) => row.item.contractId)).toEqual(["ms"]);
  });

  it("restricts by displayed status label", () => {
    const filtered = filterPortfolioRows(ROWS, { ...EMPTY_PORTFOLIO_COLUMN_FILTERS, status: "Expired" });
    expect(filtered.map((row) => row.item.contractId)).toEqual(["ms"]);
  });

  it("restricts by auto-renewal and risk labels together", () => {
    const filtered = filterPortfolioRows(ROWS, { ...EMPTY_PORTFOLIO_COLUMN_FILTERS, auto: "Yes", risk: "High risk" });
    expect(filtered.map((row) => row.item.contractId)).toEqual(["sf"]);
  });

  it("an active filter matching nothing empties the list rather than falling back", () => {
    expect(filterPortfolioRows(ROWS, { ...EMPTY_PORTFOLIO_COLUMN_FILTERS, supplier: "Nobody" })).toEqual([]);
  });

  it("offers status and risk options from the loaded rows, first-seen order", () => {
    expect(getPortfolioStatusFilterOptions(ROWS)).toEqual(["Expired", "Active"]);
    expect(getPortfolioRiskFilterOptions(ROWS)).toEqual(["Low risk", "High risk"]);
  });
});
