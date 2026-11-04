import { describe, expect, it } from "vitest";
import type { ContractListRowV4Body } from "../../../src/api/client";
import { displayDateKey, filterContracts, nextSort, NO_FILTERS, sortContracts } from "../../../src/routes/contracts/contractsListView";

const row = (o: Partial<ContractListRowV4Body>): ContractListRowV4Body => ({
  contractId: "id", supplier: "S", name: "N", noticeDeadline: "01 Jan 2027", noticeDays: 10, end: "01 Jan 2028",
  annualValue: 100000, currency: "CHF", status: "validated", caseId: null, ...o,
});
const rows = [
  row({ contractId: "a", supplier: "Zscaler", noticeDays: 133, end: "31 Mar 2027", annualValue: 258000 }),
  row({ contractId: "b", supplier: "Coupa", noticeDays: 72, end: "15 Apr 2027", annualValue: 281000, status: "caseOpen" }),
  row({ contractId: "c", supplier: "Notion", noticeDeadline: "", noticeDays: 0, end: "23 Oct 2027", annualValue: 82000, currency: "USD" }),
  row({ contractId: "d", supplier: "Elastic", noticeDays: 314, end: "12 Jan 2028", annualValue: 241000, currency: "GBP" }),
];
const ids = (r: ContractListRowV4Body[]) => r.map((x) => x.contractId).join("");

describe("contractsListView", () => {
  it("parses displayed dates", () => {
    expect(displayDateKey("15 Apr 2027")).toBeLessThan(displayDateKey("12 Jan 2028"));
    expect(displayDateKey("—")).toBeNaN();
  });
  it("sorts by notice with unstated last in both directions", () => {
    expect(ids(sortContracts(rows, { key: "notice", dir: "asc" }))).toBe("badc");
    expect(ids(sortContracts(rows, { key: "notice", dir: "desc" }))).toBe("dabc");
  });
  it("sorts by supplier and by contract end", () => {
    expect(ids(sortContracts(rows, { key: "supplier", dir: "asc" }))).toBe("bdca");
    expect(ids(sortContracts(rows, { key: "end", dir: "asc" }))).toBe("abcd");
  });
  it("sorts value inside each currency, never across them", () => {
    expect(ids(sortContracts(rows, { key: "value", dir: "desc" }))).toBe("badc");
  });
  it("filters by text, status and currency", () => {
    expect(ids(filterContracts(rows, { ...NO_FILTERS, query: "zsc" }))).toBe("a");
    expect(ids(filterContracts(rows, { ...NO_FILTERS, status: "caseOpen" }))).toBe("b");
    expect(ids(filterContracts(rows, { ...NO_FILTERS, currency: "USD" }))).toBe("c");
  });
  it("flips direction on the same column", () => {
    expect(nextSort({ key: "notice", dir: "asc" }, "notice").dir).toBe("desc");
    expect(nextSort({ key: "notice", dir: "asc" }, "value")).toEqual({ key: "value", dir: "desc" });
  });
});
