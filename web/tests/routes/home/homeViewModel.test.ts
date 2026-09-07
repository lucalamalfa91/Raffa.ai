import { describe, expect, it } from "vitest";
import type { SavingsKpiSummaryBody, SavingsOpportunityBody } from "../../../src/api/client";
import type { TrackedRenewalAction } from "../../../src/routes/renewals/renewalActionStore";
import {
  buildKpiCells,
  buildOpportunityRows,
  getOpportunityNavigation,
  getSavingsConfidenceTag,
  getSavingsStatusTag,
  reduceKpiFetch,
  type KpiFetchState,
} from "../../../src/routes/home/homeViewModel";

// Task-01's own named "Tests required" row: "unit | benchmark-unreachable -> KPIs stale-labelled" --
// the `reduceKpiFetch`/`buildKpiCells` suites below cover the pure logic half; the corresponding
// case in tests/routes/home/HomeRoute.test.tsx covers the rendered notice + Retry.

function kpis(overrides: Partial<SavingsKpiSummaryBody> = {}): SavingsKpiSummaryBody {
  return {
    annualSpendAnalyzed: [{ currency: "CHF", amount: 6_270_000, contractCount: 9 }],
    contractsAnalyzedCount: 9,
    savingsIdentified: [{ currency: "CHF", low: 410_000, high: 590_000, count: 6, averageConfidence: 0.82 }],
    savingsInProgress: [{ currency: "CHF", low: 240_000, high: 240_000, count: 2, averageConfidence: 0.75 }],
    savingsRealized: [{ currency: "CHF", low: 85_000, high: 85_000, count: 1, averageConfidence: 0.91 }],
    upcomingRenewalsCount: 4,
    ...overrides,
  };
}

function opportunity(overrides: Partial<SavingsOpportunityBody> = {}): SavingsOpportunityBody {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    supplierId: "22222222-2222-2222-2222-222222222222",
    contractId: "33333333-3333-3333-3333-333333333333",
    type: "Renewal",
    currentSpend: 640_000,
    currency: "CHF",
    estimatedSavingsLow: 80_000,
    estimatedSavingsHigh: 120_000,
    confidence: 0.92,
    confidenceLevel: "High",
    status: "Identified",
    owner: null,
    createdAt: "2026-08-01T00:00:00Z",
    updatedAt: "2026-08-01T00:00:00Z",
    realizedAmount: null,
    ...overrides,
  };
}

function tracked(overrides: Partial<TrackedRenewalAction> = {}): TrackedRenewalAction {
  return {
    contractId: "44444444-4444-4444-4444-444444444444",
    supplierId: "55555555-5555-5555-5555-555555555555",
    annualSpend: 500_000,
    owner: "user@example.test",
    status: "InProgress",
    action: "In negotiation",
    updatedAt: "2026-09-06T08:00:00Z",
    ...overrides,
  };
}

describe("reduceKpiFetch (AC-3 'benchmark-provider-unreachable -> KPIs stale-labelled')", () => {
  it("a successful fetch replaces the summary and clears stale", () => {
    const previous: KpiFetchState = { phase: "loading" };
    const next = reduceKpiFetch(previous, { ok: true, kpis: kpis() });
    expect(next).toEqual({ phase: "ready", kpis: kpis(), stale: false });
  });

  it("a first-load failure (nothing fetched yet) is ready with kpis:null and stale:true, never a fabricated summary", () => {
    const previous: KpiFetchState = { phase: "loading" };
    const next = reduceKpiFetch(previous, { ok: false });
    expect(next).toEqual({ phase: "ready", kpis: null, stale: true });
  });

  it("a later failure keeps the last successfully-fetched summary on screen, tagged stale", () => {
    const previous: KpiFetchState = { phase: "ready", kpis: kpis(), stale: false };
    const next = reduceKpiFetch(previous, { ok: false });
    expect(next).toEqual({ phase: "ready", kpis: kpis(), stale: true });
  });

  it("a subsequent success clears stale again and adopts the fresh summary", () => {
    const staleState: KpiFetchState = { phase: "ready", kpis: kpis(), stale: true };
    const fresher = kpis({ upcomingRenewalsCount: 7 });
    const next = reduceKpiFetch(staleState, { ok: true, kpis: fresher });
    expect(next).toEqual({ phase: "ready", kpis: fresher, stale: false });
  });
});

describe("buildKpiCells (AC-1 six cells)", () => {
  it("names the six cells, in AC-1's own order", () => {
    expect(buildKpiCells(null).map((cell) => cell.label)).toEqual([
      "Annual spend analyzed",
      "Savings identified",
      "Savings realized",
      "Savings in progress",
      "Contracts analyzed",
      "Upcoming renewals",
    ]);
  });

  it("kpis:null (never fetched, or a first-load failure) renders every cell with an honestly empty lines array, never a fabricated number", () => {
    const cells = buildKpiCells(null);
    expect(cells.every((cell) => cell.lines.length === 0)).toBe(true);
    expect(cells.every((cell) => cell.meta === null)).toBe(true);
  });

  it("formats a single-currency amount/range with the currency prefixed and grouped digits", () => {
    const cells = buildKpiCells(kpis());
    const spend = cells.find((cell) => cell.key === "annual-spend-analyzed")!;
    expect(spend.lines).toEqual(["CHF 6,270,000"]);
    expect(spend.meta).toBe("9 contracts");

    const identified = cells.find((cell) => cell.key === "savings-identified")!;
    expect(identified.lines).toEqual(["CHF 410,000–590,000"]);
    expect(identified.meta).toBe("6 identified");
  });

  it("a low === high range collapses to one figure instead of a zero-width range", () => {
    const cells = buildKpiCells(kpis());
    const inProgress = cells.find((cell) => cell.key === "savings-in-progress")!;
    expect(inProgress.lines).toEqual(["CHF 240,000"]);
  });

  it("multiple currency buckets render one line per currency, never summed across currencies", () => {
    const twoCurrencies = kpis({
      savingsRealized: [
        { currency: "CHF", low: 85_000, high: 85_000, count: 1, averageConfidence: 0.91 },
        { currency: "USD", low: 20_000, high: 20_000, count: 1, averageConfidence: 0.7 },
      ],
    });
    const realized = buildKpiCells(twoCurrencies).find((cell) => cell.key === "savings-realized")!;
    expect(realized.lines).toEqual(["CHF 85,000", "USD 20,000"]);
  });

  it("only 'Savings realized' is emphasized (day1-demo.html's own accent-700 highlight)", () => {
    const cells = buildKpiCells(kpis());
    const emphasized = cells.filter((cell) => cell.emphasize === true).map((cell) => cell.key);
    expect(emphasized).toEqual(["savings-realized"]);
  });

  it("Contracts analyzed / Upcoming renewals are plain grouped counts, no currency", () => {
    const cells = buildKpiCells(kpis({ contractsAnalyzedCount: 1234, upcomingRenewalsCount: 5 }));
    expect(cells.find((cell) => cell.key === "contracts-analyzed")!.lines).toEqual(["1,234"]);
    expect(cells.find((cell) => cell.key === "upcoming-renewals")!.lines).toEqual(["5"]);
  });
});

describe("getOpportunityNavigation (AC-3 'Rows open Contract 360 > Benchmark or Quote check')", () => {
  it("a contract-linked opportunity opens Contract 360's Benchmark tab", () => {
    expect(getOpportunityNavigation("contract-1")).toEqual({ kind: "contract", contractId: "contract-1" });
  });

  it("an opportunity with no contractId falls back to the Quote check landing route", () => {
    expect(getOpportunityNavigation(null)).toEqual({ kind: "quote" });
  });
});

describe("getSavingsStatusTag / getSavingsConfidenceTag (AC-2 tag columns)", () => {
  it("Identified is neutral; InProgress/Realized are accent, distinguished by their own label text", () => {
    expect(getSavingsStatusTag("Identified")).toEqual({ variant: "neutral", label: "Identified" });
    expect(getSavingsStatusTag("InProgress")).toEqual({ variant: "accent", label: "In progress" });
    expect(getSavingsStatusTag("Realized")).toEqual({ variant: "accent", label: "Realized" });
  });

  it("confidence tag pairs the qualitative tier with the raw score as a percentage, never a bare tier", () => {
    expect(getSavingsConfidenceTag(0.92, "High")).toEqual({ variant: "neutral", label: "High · 92%" });
    expect(getSavingsConfidenceTag(0.6, "Medium")).toEqual({ variant: "accent", label: "Medium · 60%" });
    expect(getSavingsConfidenceTag(0.3, "Low")).toEqual({ variant: "outline", label: "Low · 30%" });
  });
});

describe("buildOpportunityRows (AC-2 table + council 'action creates an opportunity visible on Home')", () => {
  it("an empty tenant (no real opportunities, nothing tracked this session) is an honestly empty list", () => {
    expect(buildOpportunityRows([], [])).toEqual([]);
  });

  it("tracked renewal actions render first (most-recently-acted first, matching loadTrackedRenewalActions' own order)", () => {
    const rows = buildOpportunityRows([opportunity()], [tracked()]);
    expect(rows).toHaveLength(2);
    expect(rows[0].key).toBe(`renewal-action-${tracked().contractId}`);
    expect(rows[1].key).toBe(opportunity().id);
  });

  it("a tracked row's honest gaps: no confidence, 'Not yet available' savings, no realized value, no currency on spend", () => {
    const [row] = buildOpportunityRows([], [tracked()]);
    expect(row.type).toBe("Renewal");
    expect(row.confidence).toBeNull();
    expect(row.estimatedSavings).toBe("Not yet available");
    expect(row.realized).toBe("—");
    expect(row.currentSpend).toBe("500,000"); // formatAnnualSpend -- no currency code on TrackedRenewalAction
    expect(row.status).toEqual({ variant: "accent", label: "In negotiation" });
    expect(row.navigation).toEqual({ kind: "contract", contractId: tracked().contractId });
  });

  it("a real opportunity row carries its own real confidence/savings/realized figures and status tag", () => {
    const [row] = buildOpportunityRows([opportunity({ realizedAmount: 85_000 })], []);
    expect(row.currentSpend).toBe("CHF 640,000");
    expect(row.estimatedSavings).toBe("CHF 80,000–120,000");
    expect(row.confidence).toEqual({ variant: "neutral", label: "High · 92%" });
    expect(row.realized).toBe("CHF 85,000");
    expect(row.status).toEqual({ variant: "neutral", label: "Identified" });
    expect(row.navigation).toEqual({ kind: "contract", contractId: opportunity().contractId });
  });

  it("owner falls back to 'Unassigned' when the backend has none on file yet", () => {
    const [row] = buildOpportunityRows([opportunity({ owner: null })], []);
    expect(row.owner).toBe("Unassigned");
  });

  it("primary identity prefers supplier, then contract, then the opportunity's own id -- never a blank cell", () => {
    const [supplierRow] = buildOpportunityRows([opportunity()], []);
    expect(supplierRow.primaryLabel).toContain("Supplier");

    const [contractRow] = buildOpportunityRows([opportunity({ supplierId: null })], []);
    expect(contractRow.primaryLabel).toContain("Contract");

    const [bareRow] = buildOpportunityRows([opportunity({ supplierId: null, contractId: null })], []);
    expect(bareRow.primaryLabel).toBe(`Opportunity ${opportunity().id.slice(0, 8)}`);
    expect(bareRow.navigation).toEqual({ kind: "quote" });
  });
});
