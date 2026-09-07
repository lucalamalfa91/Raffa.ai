import { describe, expect, it } from "vitest";
import type { PortfolioListItem } from "../../../src/api/client";
import { computeAttentionRow, type AttentionRow } from "../../../src/routes/contracts/portfolioAttention";
import {
  EMPTY_PORTFOLIO_FILTERS,
  applyPortfolioFilters,
  isAnyPortfolioFilterActive,
} from "../../../src/routes/contracts/portfolioFilterState";

// Task-01's own named "Tests required" row: "unit | filter + attention strip click, AC-1/AC-2".

function item(overrides: Partial<PortfolioListItem> = {}): PortfolioListItem {
  return {
    contractId: "11111111-1111-1111-1111-111111111111",
    supplierId: "22222222-2222-2222-2222-222222222222",
    type: "Msa",
    annualSpend: 100_000,
    startDate: "2025-01-01",
    endDate: "2026-01-01",
    renewalDate: null,
    cancellationDeadline: null,
    autoRenewal: false,
    status: "active",
    risk: null,
    ...overrides,
  };
}

const NOW = new Date("2026-01-01T00:00:00Z");

function row(overrides: Partial<PortfolioListItem> = {}): AttentionRow {
  return computeAttentionRow(item(overrides), NOW);
}

describe("isAnyPortfolioFilterActive", () => {
  it("is false for the empty filter state", () => {
    expect(isAnyPortfolioFilterActive(EMPTY_PORTFOLIO_FILTERS)).toBe(false);
  });

  it("is true once supplierId is set", () => {
    expect(isAnyPortfolioFilterActive({ ...EMPTY_PORTFOLIO_FILTERS, supplierId: "supplier-1" })).toBe(true);
  });

  it("is true once status is set", () => {
    expect(isAnyPortfolioFilterActive({ ...EMPTY_PORTFOLIO_FILTERS, status: "active" })).toBe(true);
  });

  it("is true once risk is set", () => {
    expect(isAnyPortfolioFilterActive({ ...EMPTY_PORTFOLIO_FILTERS, risk: "High" })).toBe(true);
  });

  it("is true once autoRenewal is narrowed away from 'any'", () => {
    expect(isAnyPortfolioFilterActive({ ...EMPTY_PORTFOLIO_FILTERS, autoRenewal: "yes" })).toBe(true);
  });

  it("is true once minAnnualSpend is set", () => {
    expect(isAnyPortfolioFilterActive({ ...EMPTY_PORTFOLIO_FILTERS, minAnnualSpend: "1000" })).toBe(true);
  });

  it("is true once maxAnnualSpend is set", () => {
    expect(isAnyPortfolioFilterActive({ ...EMPTY_PORTFOLIO_FILTERS, maxAnnualSpend: "1000" })).toBe(true);
  });

  it("is true once a renewal window is set", () => {
    expect(isAnyPortfolioFilterActive({ ...EMPTY_PORTFOLIO_FILTERS, renewalWithinDays: 30 })).toBe(true);
  });

  it("is true once an attention bucket is selected", () => {
    expect(isAnyPortfolioFilterActive({ ...EMPTY_PORTFOLIO_FILTERS, attentionBucket: "risk" })).toBe(true);
  });
});

describe("applyPortfolioFilters (AC-1 chips)", () => {
  it("returns every row unfiltered", () => {
    const rows = [row({ contractId: "a" }), row({ contractId: "b" })];
    expect(applyPortfolioFilters(rows, EMPTY_PORTFOLIO_FILTERS, NOW)).toHaveLength(2);
  });

  it("filters by exact supplierId", () => {
    const rows = [row({ contractId: "a", supplierId: "s-1" }), row({ contractId: "b", supplierId: "s-2" })];
    const result = applyPortfolioFilters(rows, { ...EMPTY_PORTFOLIO_FILTERS, supplierId: "s-1" }, NOW);
    expect(result.map((r) => r.item.contractId)).toEqual(["a"]);
  });

  it("filters by status, case-insensitively", () => {
    const rows = [row({ contractId: "a", status: "Active" }), row({ contractId: "b", status: "Expired" })];
    const result = applyPortfolioFilters(rows, { ...EMPTY_PORTFOLIO_FILTERS, status: "active" }, NOW);
    expect(result.map((r) => r.item.contractId)).toEqual(["a"]);
  });

  it("filters by exact risk", () => {
    const rows = [row({ contractId: "a", risk: "High" }), row({ contractId: "b", risk: "Low" })];
    const result = applyPortfolioFilters(rows, { ...EMPTY_PORTFOLIO_FILTERS, risk: "High" }, NOW);
    expect(result.map((r) => r.item.contractId)).toEqual(["a"]);
  });

  it.each<["yes" | "no", boolean]>([
    ["yes", true],
    ["no", false],
  ])("filters auto-renewal = %s", (filterValue, matchingAutoRenewal) => {
    const rows = [
      row({ contractId: "a", autoRenewal: matchingAutoRenewal }),
      row({ contractId: "b", autoRenewal: !matchingAutoRenewal }),
    ];
    const result = applyPortfolioFilters(rows, { ...EMPTY_PORTFOLIO_FILTERS, autoRenewal: filterValue }, NOW);
    expect(result.map((r) => r.item.contractId)).toEqual(["a"]);
  });

  it("filters by minimum annual spend, excluding contracts with no recorded spend", () => {
    const rows = [
      row({ contractId: "a", annualSpend: 500_000 }),
      row({ contractId: "b", annualSpend: 1_000 }),
      row({ contractId: "c", annualSpend: null }),
    ];
    const result = applyPortfolioFilters(rows, { ...EMPTY_PORTFOLIO_FILTERS, minAnnualSpend: "100000" }, NOW);
    expect(result.map((r) => r.item.contractId)).toEqual(["a"]);
  });

  it("filters by maximum annual spend, excluding contracts with no recorded spend", () => {
    const rows = [
      row({ contractId: "a", annualSpend: 500_000 }),
      row({ contractId: "b", annualSpend: 1_000 }),
      row({ contractId: "c", annualSpend: null }),
    ];
    const result = applyPortfolioFilters(rows, { ...EMPTY_PORTFOLIO_FILTERS, maxAnnualSpend: "100000" }, NOW);
    expect(result.map((r) => r.item.contractId)).toEqual(["b"]);
  });

  it("renewal-period window only matches an auto-renewing contract whose EndDate falls within it", () => {
    const rows = [
      row({ contractId: "a", autoRenewal: true, endDate: "2026-01-20" }), // 19 days out
      row({ contractId: "b", autoRenewal: true, endDate: "2026-06-01" }), // far out
      row({ contractId: "c", autoRenewal: false, endDate: "2026-01-20" }), // not auto-renewing
      row({ contractId: "d", autoRenewal: true, endDate: null }),
    ];
    const result = applyPortfolioFilters(rows, { ...EMPTY_PORTFOLIO_FILTERS, renewalWithinDays: 30 }, NOW);
    expect(result.map((r) => r.item.contractId)).toEqual(["a"]);
  });

  it("does not match a renewal window for an end date already in the past", () => {
    const rows = [row({ contractId: "a", autoRenewal: true, endDate: "2025-01-01" })];
    const result = applyPortfolioFilters(rows, { ...EMPTY_PORTFOLIO_FILTERS, renewalWithinDays: 30 }, NOW);
    expect(result).toHaveLength(0);
  });

  it("AC-2: an attentionBucket selection filters down to that bucket's rows", () => {
    const rows = [row({ contractId: "a", risk: "High" }), row({ contractId: "b", risk: "Low" })];
    const result = applyPortfolioFilters(rows, { ...EMPTY_PORTFOLIO_FILTERS, attentionBucket: "risk" }, NOW);
    expect(result.map((r) => r.item.contractId)).toEqual(["a"]);
  });

  it("composes the attention bucket with the chips as an AND (both must hold)", () => {
    const rows = [
      row({ contractId: "a", risk: "High", status: "active" }),
      row({ contractId: "b", risk: "High", status: "expired" }),
    ];
    const result = applyPortfolioFilters(
      rows,
      { ...EMPTY_PORTFOLIO_FILTERS, attentionBucket: "risk", status: "active" },
      NOW,
    );
    expect(result.map((r) => r.item.contractId)).toEqual(["a"]);
  });
});
