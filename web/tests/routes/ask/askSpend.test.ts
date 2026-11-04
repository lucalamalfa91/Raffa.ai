import { describe, expect, it } from "vitest";
import { buildSpendArtifact, intentFilters, revisedIntent, spendIntent, spendLink } from "../../../src/routes/ask/askSpend";
import { spendReport } from "../savings/spendFixture";

const t = (id: string, vars?: Record<string, string | number>) => id.replace(/\{(\w+)\}/g, (_, k: string) => String(vars?.[k] ?? `{${k}}`));

describe("Ask on Spesa e risparmi", () => {
  it("reads what a request asks of the figures", () => {
    expect(spendIntent("visualizza il kpi per anno, voglio capire il trend")).toMatchObject({ view: "year", basis: "accrual", include: "all", year: null });
    expect(spendIntent("la dashboard per trimestre, per cassa, FY 2027")).toMatchObject({ view: "quarter", basis: "cash", year: 2027 });
    expect(spendIntent("KPI solo impegnato")).toMatchObject({ include: "committed" });
    expect(spendIntent("show the KPIs by quarter for 2026").year).toBe(2026);
  });

  it("keeps what the earlier versions asked unless the new request changes it", () => {
    const report = spendReport();
    const v3 = revisedIntent(["fammi i KPI della sicurezza", "fallo per trimestre, per cassa"], "solo impegnato");
    expect(v3).toMatchObject({ view: "quarter", basis: "cash", include: "committed" });
    expect(intentFilters(report, v3).category).toBe("Sicurezza");

    expect(revisedIntent(["KPI per trimestre, 2027"], "rifallo per anno, per competenza")).toMatchObject({ view: "year", basis: "accrual", year: 2027 });
    expect(revisedIntent(["KPI solo impegnato"], "includi anche quelli da decidere").include).toBe("all");
  });

  it("keeps a named year and category only when the report has them", () => {
    const report = spendReport();
    expect(intentFilters(report, spendIntent("i KPI della sicurezza per il 2031"))).toMatchObject({ category: "Sicurezza", year: null });
    expect(spendLink(intentFilters(report, spendIntent("KPI per trimestre 2027")))).toBe("/savings?view=quarter&year=2027");
  });

  it("builds KPIs with a year-by-year table in the reporting currency", () => {
    const a = buildSpendArtifact("kpi", spendReport(), spendIntent("fammi i KPI"), t);

    expect(a.title).toBe("Contract KPIs · by year");
    expect(a.tiles[0]).toEqual({ label: "Spend FY 2026 · accrual", value: "CHF 190k" });
    expect(a.tables[0]!.rows.map((r) => r[0])).toEqual(["FY 2026", "FY 2027"]);
    expect(a.tables[0]!.rows[0]).toEqual(["FY 2026", "CHF 190k", "CHF 190k", "CHF 12k", "2 / 2"]);
    expect(a.link).toEqual({ label: "Open in Spend and savings", to: "/savings" });
  });

  it("builds a dashboard by quarter of the named year, and a brief that names what is left out", () => {
    const d = buildSpendArtifact("dashboard", spendReport(), spendIntent("dashboard per trimestre 2027"), t, 2);
    expect(d.version).toBe(2);
    expect(d.charts[0]!.items.map((i) => i.label)).toEqual(["Q1 2027", "Q2 2027", "Q3 2027", "Q4 2027"]);
    expect(d.charts[1]!.items.map((i) => i.label)).toEqual(["Procurement", "Sicurezza"]);
    expect(d.tables[1]!.rows.map((r) => r[0])).toEqual(["Coupa", "Zscaler"]);

    const brief = buildSpendArtifact("document", spendReport(), spendIntent("scrivi un brief"), t);
    expect(brief.markdown).toContain("2 contracts are out of the totals, never estimated: Sage, Nodate");
    expect(brief.tiles).toEqual([]);
  });
});
