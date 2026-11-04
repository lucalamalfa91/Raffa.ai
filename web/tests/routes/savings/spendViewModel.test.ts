import { describe, expect, it } from "vitest";
import {
  budget,
  chartColumns,
  contractDetail,
  DEFAULT_FILTERS,
  kpis,
  pivot,
  pivotCsv,
  readFilters,
  writeFilters,
  type SpendFilters,
} from "../../../src/routes/savings/spendViewModel";
import { spendReport } from "./spendFixture";

const f = (over: Partial<SpendFilters> = {}): SpendFilters => ({ ...DEFAULT_FILTERS, ...over });

describe("Spesa e risparmi view model", () => {
  it("keeps the filters in the URL, defaults left out", () => {
    const filters = f({ view: "quarter", year: 2027, basis: "cash", include: "auto", category: "", sort: "decision", contract: "coupa" });
    const params = writeFilters(filters);

    expect(params.toString()).toBe("view=quarter&year=2027&basis=cash&include=auto&cat=&sort=decision&contract=coupa");
    expect(readFilters(params)).toEqual(filters);
    expect(writeFilters(DEFAULT_FILTERS).toString()).toBe("");
    expect(readFilters(new URLSearchParams("view=bogus&year=1800&include=x"))).toEqual(DEFAULT_FILTERS);
  });

  it("sums the bars per fiscal year, and per quarter around the selected year", () => {
    const years = chartColumns(spendReport(), f());
    expect(years.map((c) => [c.label, c.total, c.current, c.selected])).toEqual([
      ["FY 2026", 190_000, true, true],
      ["FY 2027", 47_500 + 75_000 + 67_500, false, false],
    ]);
    expect(years[0]!.proven).toBe(3_000);
    expect(years[0]!.signedOnly).toBe(9_000);

    const committedOnly = chartColumns(spendReport(), f({ include: "committed" }));
    expect(committedOnly[1]!.total).toBe(47_500);

    const quarters = chartColumns(spendReport(), f({ view: "quarter" }));
    expect(quarters).toHaveLength(8);
    expect(quarters.find((c) => c.current)?.label).toBe("Q4 2026");
  });

  it("reads the KPIs of the selected year and says when the earlier one is not comparable", () => {
    const k = kpis(spendReport(), f());
    expect(k.spend).toBe(190_000);
    expect(k.actualToDate).toBe(160_000);
    expect(k.savingsSigned).toBe(12_000);
    expect(k.savingsProven).toBe(3_000);
    expect(k.nextCommitted).toBe(47_500);
    expect(k.nextTotal).toBe(190_000);
    expect(k.decideSoonCount).toBe(2);
    expect(k.decideSoon).toBe(190_000); // the two current annual values
    expect(k.firstDecision).toEqual({ supplier: "Coupa", on: "2027-01-15" });

    const next = kpis(spendReport(), f({ year: 2027 }));
    expect(next.changePct).toBeCloseTo(0, 5); // 190k against 190k: FY 2026 documents more contracts, so it is comparable
    expect(next.nextYear).toBeNull();
  });

  it("groups contracts by category with incidence, savings and the next decision", () => {
    const p = pivot(spendReport(), f());
    expect(p.columns.map((c) => c.label)).toEqual(["FY 2026", "FY 2027"]);
    expect(p.groups.map((g) => g.category)).toEqual(["Procurement", "Sicurezza"]);
    const zscaler = p.groups[1]!.lines[0]!;
    expect(zscaler.values).toEqual([90_000, 90_000]);
    expect(zscaler.share).toBeCloseTo(90_000 / 190_000, 6);
    expect(zscaler.savings).toBe(12_000);
    expect(zscaler.decisionDay).toBe("2027-03-17");
    expect(p.total.values).toEqual([190_000, 190_000]);

    const byDecision = pivot(spendReport(), f({ sort: "decision" }));
    expect(byDecision.groups[0]!.category).toBe("Procurement"); // 15 Jan before 17 Mar

    const quarters = pivot(spendReport(), f({ view: "quarter", year: 2027 }));
    expect(quarters.columns.map((c) => c.label)).toEqual(["Q1 2027", "Q2 2027", "Q3 2027", "Q4 2027", "FY 2027", "FY 2026"]);
    expect(quarters.deltaColumn).toBe(true);
  });

  it("opens a contract's detail with its share, timeline and savings", () => {
    const d = contractDetail(spendReport(), f(), "zscaler")!;
    expect(d.share).toBeCloseTo(90_000 / 190_000, 6);
    expect(d.categoryShare).toBe(1);
    expect(d.currentAnnual).toBe(90_000);
    expect(d.savings).toHaveLength(1);
    expect(d.timeline[0]!.left).toBe(0); // the term started before the report
    expect(d.quarters.map((q) => q.label)).toEqual(["Q1", "Q2", "Q3", "Q4"]);
    expect(contractDetail(spendReport(), f(), "nobody")).toBeNull();
  });

  it("builds next year's budget and the contracts still to act on, by deadline", () => {
    const b = budget(spendReport(), f())!;
    expect([b.committed, b.autoRenewal, b.toDecide]).toEqual([47_500, 75_000, 67_500]);
    expect(b.actions.map((a) => [a.row.supplier, a.inPlay, a.objective?.annualTarget ?? null])).toEqual([
      ["Coupa", 75_000, 10_000],
      ["Zscaler", 67_500, null],
    ]);
    expect(budget(spendReport(), f({ year: 2027 }))).toBeNull();
  });

  it("exports the table as CSV with the currency in the headers", () => {
    const csv = pivotCsv(pivot(spendReport(), f()), "CHF", {
      category: "Categoria",
      supplier: "Fornitore",
      contract: "Contratto",
      share: "Incidenza",
      savings: "Risparmi",
      decideBy: "Decidi entro",
      noCategory: "Senza categoria",
    });
    const lines = csv.trim().split("\n");
    expect(lines[0]).toBe("Categoria,Fornitore,Contratto,FY 2026 (CHF),FY 2027 (CHF),Incidenza,Risparmi (CHF),Decidi entro");
    expect(lines[2]).toBe("Sicurezza,Zscaler,Zscaler MSA,90000.00,90000.00,47.4%,12000.00,2027-03-17");
  });
});
