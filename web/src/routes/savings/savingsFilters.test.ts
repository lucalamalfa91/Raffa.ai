import { describe, expect, it } from "vitest";
import {
  buildSavingsContractHref,
  EMPTY_SAVINGS_FILTERS,
  getCurrencyFilterOptions,
  getSupplierFilterOptions,
  matchesSavingsFilters,
  readSavingsFiltersFromSearch,
  SAVINGS_STATUS_FILTER_OPTIONS,
  type SavingsFilterableOpportunity,
} from "./savingsFilters";

/**
 * task-01-savings-filters (ADR-020; parent story us-01-savings-filters AC-1/AC-2/AC-3). Proves the
 * three council-picked filter dimensions -- supplier, status, currency -- each restrict the
 * opportunities table's rows independently, that the three dimensions compose (AND, not OR), and
 * that the cleared state restores the full list.
 */

const IDENTIFIED_ACME_CHF: SavingsFilterableOpportunity = { supplierLabel: "Acme Corp", currency: "CHF", statusValue: "Identified" };
const IN_PROGRESS_ACME_EUR: SavingsFilterableOpportunity = { supplierLabel: "Acme Corp", currency: "EUR", statusValue: "InProgress" };
const REALIZED_GLOBEX_CHF: SavingsFilterableOpportunity = { supplierLabel: "Globex", currency: "CHF", statusValue: "Realized" };

const ALL_ROWS: readonly SavingsFilterableOpportunity[] = [IDENTIFIED_ACME_CHF, IN_PROGRESS_ACME_EUR, REALIZED_GLOBEX_CHF];

describe("matchesSavingsFilters", () => {
  it("matches every row when no filter is active -- a fresh page load", () => {
    expect(ALL_ROWS.filter((row) => matchesSavingsFilters(row, EMPTY_SAVINGS_FILTERS))).toEqual(ALL_ROWS);
  });

  it("restricts by supplier alone", () => {
    const filtered = ALL_ROWS.filter((row) => matchesSavingsFilters(row, { ...EMPTY_SAVINGS_FILTERS, supplier: "Acme Corp" }));
    expect(filtered).toEqual([IDENTIFIED_ACME_CHF, IN_PROGRESS_ACME_EUR]);
  });

  it("restricts by status alone", () => {
    const filtered = ALL_ROWS.filter((row) => matchesSavingsFilters(row, { ...EMPTY_SAVINGS_FILTERS, status: "Realized" }));
    expect(filtered).toEqual([REALIZED_GLOBEX_CHF]);
  });

  it("restricts by currency alone", () => {
    const filtered = ALL_ROWS.filter((row) => matchesSavingsFilters(row, { ...EMPTY_SAVINGS_FILTERS, currency: "EUR" }));
    expect(filtered).toEqual([IN_PROGRESS_ACME_EUR]);
  });

  it("composes all three active dimensions with AND, not OR", () => {
    const filtered = ALL_ROWS.filter((row) =>
      matchesSavingsFilters(row, { supplier: "Acme Corp", status: "Identified", currency: "CHF", contractId: null }),
    );
    expect(filtered).toEqual([IDENTIFIED_ACME_CHF]);
  });

  it("an active filter matching nothing empties the list rather than falling back to the full one", () => {
    const filtered = ALL_ROWS.filter((row) => matchesSavingsFilters(row, { ...EMPTY_SAVINGS_FILTERS, supplier: "Nobody" }));
    expect(filtered).toEqual([]);
  });

  it("clearing an active filter (returning to EMPTY_SAVINGS_FILTERS) restores the full list", () => {
    const narrowed = ALL_ROWS.filter((row) => matchesSavingsFilters(row, { ...EMPTY_SAVINGS_FILTERS, status: "Realized" }));
    expect(narrowed).not.toEqual(ALL_ROWS);

    const cleared = ALL_ROWS.filter((row) => matchesSavingsFilters(row, EMPTY_SAVINGS_FILTERS));
    expect(cleared).toEqual(ALL_ROWS);
  });
});

describe("SAVINGS_STATUS_FILTER_OPTIONS", () => {
  it("is exactly the council's closed three-value set -- no invented category", () => {
    expect(SAVINGS_STATUS_FILTER_OPTIONS).toEqual(["Identified", "InProgress", "Realized"]);
  });
});

describe("getSupplierFilterOptions", () => {
  it("lists distinct supplier labels actually present, first-seen order", () => {
    expect(getSupplierFilterOptions(ALL_ROWS)).toEqual(["Acme Corp", "Globex"]);
  });

  it("returns an empty list when there are no rows", () => {
    expect(getSupplierFilterOptions([])).toEqual([]);
  });
});

describe("getCurrencyFilterOptions", () => {
  it("lists distinct currencies actually present, first-seen order", () => {
    expect(getCurrencyFilterOptions(ALL_ROWS)).toEqual(["CHF", "EUR"]);
  });
});

describe("contract focus (?contract=, a deep link from Contract 360 or Renewals)", () => {
  const ON_CONTRACT: SavingsFilterableOpportunity = { supplierLabel: "Acme Corp", currency: "CHF", statusValue: "Identified", contractId: "c-1" };
  const ELSEWHERE: SavingsFilterableOpportunity = { supplierLabel: "Acme Corp", currency: "CHF", statusValue: "Identified", contractId: "c-2" };
  const QUOTE_SOURCED: SavingsFilterableOpportunity = { supplierLabel: "Acme Corp", currency: "CHF", statusValue: "Identified", contractId: null };

  it("restricts to the one contract, and composes with the other dimensions", () => {
    const rows = [ON_CONTRACT, ELSEWHERE, QUOTE_SOURCED];
    expect(rows.filter((row) => matchesSavingsFilters(row, { ...EMPTY_SAVINGS_FILTERS, contractId: "c-1" }))).toEqual([ON_CONTRACT]);
    expect(rows.filter((row) => matchesSavingsFilters(row, { ...EMPTY_SAVINGS_FILTERS, contractId: "c-1", status: "Realized" }))).toEqual([]);
  });

  it("reads ?contract= and a known ?status= once, ignoring blanks and unknown statuses", () => {
    expect(readSavingsFiltersFromSearch(new URLSearchParams("contract=c-1&status=InProgress"))).toEqual({
      ...EMPTY_SAVINGS_FILTERS,
      contractId: "c-1",
      status: "InProgress",
    });
    expect(readSavingsFiltersFromSearch(new URLSearchParams("contract=%20&status=Bogus"))).toEqual(EMPTY_SAVINGS_FILTERS);
    expect(readSavingsFiltersFromSearch(new URLSearchParams())).toEqual(EMPTY_SAVINGS_FILTERS);
  });

  it("builds the link other screens use", () => {
    expect(buildSavingsContractHref("c-1")).toBe("/savings?contract=c-1");
  });
});
