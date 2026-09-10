import { describe, expect, it } from "vitest";
import type { SavingsKpiSummaryBody, SavingsOpportunityBody } from "../../../src/api/client";
import type { TrackedRenewalAction } from "../../../src/routes/renewals/renewalActionStore";
import {
  SAVINGS_SUMMARY_OFF,
  buildKpiCells,
  buildOpportunityRows,
  buildSupplierNameIndex,
  formatSavingsSummary,
  getOpportunityNavigation,
  getSavingsConfidenceTag,
  getSavingsStatusTag,
  reduceKpiFetch,
  type KpiFetchState,
} from "../../../src/routes/savings/savingsViewModel";

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

describe("reduceKpiFetch ('benchmark-provider-unreachable -> KPIs stale-labelled')", () => {
  it("a successful fetch replaces the summary and clears stale", () => {
    const previous: KpiFetchState = { phase: "loading" };
    expect(reduceKpiFetch(previous, { ok: true, kpis: kpis() })).toEqual({ phase: "ready", kpis: kpis(), stale: false });
  });

  it("a first-load failure is ready with kpis:null and stale:true, never a fabricated summary", () => {
    expect(reduceKpiFetch({ phase: "loading" }, { ok: false })).toEqual({ phase: "ready", kpis: null, stale: true });
  });

  it("a later failure keeps the last successfully-fetched summary on screen, tagged stale", () => {
    const previous: KpiFetchState = { phase: "ready", kpis: kpis(), stale: false };
    expect(reduceKpiFetch(previous, { ok: false })).toEqual({ phase: "ready", kpis: kpis(), stale: true });
  });

  it("a subsequent success clears stale again and adopts the fresh summary", () => {
    const fresher = kpis({ upcomingRenewalsCount: 7 });
    expect(reduceKpiFetch({ phase: "ready", kpis: kpis(), stale: true }, { ok: true, kpis: fresher })).toEqual({
      phase: "ready",
      kpis: fresher,
      stale: false,
    });
  });
});

describe("buildKpiCells (screens-v2.md #8: three cells with meta lines)", () => {
  it("names the three cells in the prototype's order", () => {
    expect(buildKpiCells(null).map((cell) => cell.label)).toEqual(["Contracts analyzed", "Upcoming renewals", "Savings identified"]);
  });

  it("kpis:null renders every cell with an honestly empty lines array and no meta", () => {
    const cells = buildKpiCells(null);
    expect(cells.every((cell) => cell.lines.length === 0 && cell.meta === null)).toBe(true);
  });

  it("carries real values and meta lines from the same response", () => {
    const [contracts, renewals, identified] = buildKpiCells(kpis());
    expect(contracts.lines).toEqual(["9"]);
    expect(contracts.meta).toBe("CHF 6,270,000 annual spend");
    expect(renewals.lines).toEqual(["4"]);
    expect(renewals.meta).toBe("auto-renewing contracts in the pipeline");
    expect(identified.lines).toEqual(["CHF 410,000–590,000"]);
    expect(identified.meta).toBe("6 identified · 2 in progress · 1 realized");
  });

  it("multiple currency buckets render one line per currency, never summed across currencies", () => {
    const twoCurrencies = kpis({
      savingsIdentified: [
        { currency: "CHF", low: 410_000, high: 590_000, count: 6, averageConfidence: 0.82 },
        { currency: "USD", low: 20_000, high: 20_000, count: 1, averageConfidence: 0.7 },
      ],
      annualSpendAnalyzed: [
        { currency: "CHF", amount: 6_270_000, contractCount: 9 },
        { currency: "USD", amount: 100_000, contractCount: 1 },
      ],
    });
    const [contracts, , identified] = buildKpiCells(twoCurrencies);
    expect(identified.lines).toEqual(["CHF 410,000–590,000", "USD 20,000"]);
    expect(contracts.meta).toBe("CHF 6,270,000 · USD 100,000 annual spend");
  });

  it("is honest when no spend has been analysed yet", () => {
    const [contracts] = buildKpiCells(kpis({ annualSpendAnalyzed: [], contractsAnalyzedCount: 0 }));
    expect(contracts.lines).toEqual(["0"]);
    expect(contracts.meta).toBe("no annual spend recorded yet");
  });
});

describe("formatSavingsSummary", () => {
  it("is the off-tier sentence with nothing to show", () => {
    expect(formatSavingsSummary(null, 0)).toBe(SAVINGS_SUMMARY_OFF);
    expect(formatSavingsSummary(kpis({ savingsIdentified: [] }), 0)).toBe(SAVINGS_SUMMARY_OFF);
  });

  it("combines the row count with the identified range, singular and plural", () => {
    expect(formatSavingsSummary(kpis(), 1)).toBe("1 opportunity · CHF 410,000–590,000 identified");
    expect(formatSavingsSummary(kpis(), 3)).toBe("3 opportunities · CHF 410,000–590,000 identified");
    expect(formatSavingsSummary(null, 2)).toBe("2 opportunities");
    expect(formatSavingsSummary(kpis(), 0)).toBe("CHF 410,000–590,000 identified");
  });
});

describe("getOpportunityNavigation (rows open Contract 360)", () => {
  it("a contract-linked opportunity opens Contract 360", () => {
    expect(getOpportunityNavigation("contract-1")).toEqual({ kind: "contract", contractId: "contract-1" });
  });

  it("an opportunity with no contractId falls back to the Quote check landing route", () => {
    expect(getOpportunityNavigation(null)).toEqual({ kind: "quote" });
  });
});

describe("getSavingsStatusTag / getSavingsConfidenceTag", () => {
  it("Identified is neutral; InProgress/Realized are accent, distinguished by their own label text", () => {
    expect(getSavingsStatusTag("Identified")).toEqual({ variant: "neutral", label: "Identified" });
    expect(getSavingsStatusTag("InProgress")).toEqual({ variant: "accent", label: "In progress" });
    expect(getSavingsStatusTag("Realized")).toEqual({ variant: "accent", label: "Realized" });
  });

  it("confidence tag pairs the qualitative tier with the raw score as a percentage", () => {
    expect(getSavingsConfidenceTag(0.92, "High")).toEqual({ variant: "neutral", label: "High · 92%" });
    expect(getSavingsConfidenceTag(0.6, "Medium")).toEqual({ variant: "accent", label: "Medium · 60%" });
    expect(getSavingsConfidenceTag(0.3, "Low")).toEqual({ variant: "outline", label: "Low · 30%" });
  });
});

describe("buildSupplierNameIndex", () => {
  it("maps contract ids to the portfolio's resolved supplier names, skipping unresolved ones", () => {
    const index = buildSupplierNameIndex([
      { contractId: "c-1", supplierName: "Salesforce" },
      { contractId: "c-2", supplierName: null },
      { contractId: "c-3", supplierName: "  " },
    ] as never);
    expect([...index.entries()]).toEqual([["c-1", "Salesforce"]]);
  });
});

describe("buildOpportunityRows (Supplier · Action · Estimate · Status)", () => {
  it("an empty tenant (no real opportunities, nothing tracked this session) is an honestly empty list", () => {
    expect(buildOpportunityRows([], [])).toEqual([]);
  });

  it("tracked renewal actions render first, then the real list", () => {
    const rows = buildOpportunityRows([opportunity()], [tracked()]);
    expect(rows.map((row) => row.key)).toEqual([`renewal-action-${tracked().contractId}`, opportunity().id]);
  });

  it("uses the portfolio's supplier name for the contract, else the id fragment, else an honest placeholder", () => {
    const names = new Map([[opportunity().contractId!, "Salesforce"]]);
    const [named] = buildOpportunityRows([opportunity()], [], names);
    expect(named.supplierLabel).toBe("Salesforce");
    expect(named.supplierTitle).toBe(opportunity().supplierId);

    const [fragment] = buildOpportunityRows([opportunity()], []);
    expect(fragment.supplierLabel).toBe("Supplier 22222222");

    const [placeholder] = buildOpportunityRows([opportunity({ supplierId: null, contractId: null })], []);
    expect(placeholder.supplierLabel).toBe("Supplier not resolved");
    expect(placeholder.navigation).toEqual({ kind: "quote" });
  });

  it("a real row carries type as the action, the estimate range, the confidence tag and the status tag", () => {
    const [row] = buildOpportunityRows([opportunity()], []);
    expect(row.action).toBe("Renewal");
    expect(row.estimate).toBe("CHF 80,000–120,000");
    expect(row.confidence).toEqual({ variant: "neutral", label: "High · 92%" });
    expect(row.status).toEqual({ variant: "neutral", label: "Identified" });
    expect(row.navigation).toEqual({ kind: "contract", contractId: opportunity().contractId });
  });

  it("a tracked row's honest gaps: no confidence, 'Not yet available' estimate, its own action as the status", () => {
    const [row] = buildOpportunityRows([], [tracked()]);
    expect(row.action).toBe("Renewal");
    expect(row.confidence).toBeNull();
    expect(row.estimate).toBe("Not yet available");
    expect(row.status).toEqual({ variant: "accent", label: "In negotiation" });
    expect(row.navigation).toEqual({ kind: "contract", contractId: tracked().contractId });
  });
});
