import { describe, expect, it } from "vitest";
import type { SavingsKpiSummaryBody, SavingsOpportunityBody } from "../../../src/api/client";
import {
  SAVINGS_SUMMARY_OFF,
  buildContextCells,
  buildKpiCells,
  buildOpportunitiesPortfolioHref,
  buildOpportunityRows,
  findNoticeSoonContractIds,
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
    savingsRealized: [{ currency: "CHF", amount: 85_000, count: 1 }],
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

describe("buildOpportunityRows (Supplier · Action · Estimate · Status)", () => {
  it("an empty tenant is an honestly empty list", () => {
    expect(buildOpportunityRows([])).toEqual([]);
  });

  it("uses the portfolio's supplier name for the contract, else the id fragment, else an honest placeholder", () => {
    const names = new Map([[opportunity().contractId!, "Salesforce"]]);
    const [named] = buildOpportunityRows([opportunity()], names);
    expect(named.supplierLabel).toBe("Salesforce");
    expect(named.supplierTitle).toBe(opportunity().supplierId);

    const [fragment] = buildOpportunityRows([opportunity()]);
    expect(fragment.supplierLabel).toBe("Supplier 22222222");

    const [placeholder] = buildOpportunityRows([opportunity({ supplierId: null, contractId: null })]);
    expect(placeholder.supplierLabel).toBe("Supplier not resolved");
    expect(placeholder.navigation).toEqual({ kind: "quote" });
  });

  it("a real row carries type as the action, the estimate range, the confidence tag and the status tag", () => {
    const [row] = buildOpportunityRows([opportunity()]);
    expect(row.action).toBe("Renewal");
    expect(row.estimate).toBe("CHF 80,000–120,000");
    expect(row.confidence).toEqual({ variant: "neutral", label: "High · 92%" });
    expect(row.status).toEqual({ variant: "neutral", label: "Identified" });
    expect(row.navigation).toEqual({ kind: "contract", contractId: opportunity().contractId });
  });

  it("carries the spend, the contract's notice days, a scoped Ask question and a Renewals link while notice is ahead", () => {
    const contractId = opportunity().contractId!;
    const names = new Map([[contractId, "Salesforce"]]);
    const [row] = buildOpportunityRows([opportunity()], names, new Map([[contractId, 14]]));
    expect(row.contractId).toBe(contractId);
    expect(row.currentSpend).toBe("CHF 640,000");
    expect(row.noticeDays).toBe(14);
    expect(row.noticeUrgent).toBe(true);
    expect(row.askQuestion).toBe("Where can we save with Salesforce?");
    expect(row.renewalHref).toBe(`/renewals?select=${contractId}`);

    const [unnamed] = buildOpportunityRows([opportunity()]);
    expect(unnamed.noticeDays).toBeNull();
    expect(unnamed.renewalHref).toBeNull();
    // Never an id in a question.
    expect(unnamed.askQuestion).toBe("Where can we save on this contract?");

    const [realized] = buildOpportunityRows([opportunity({ status: "Realized", realizedAmount: 90_000 })], names, new Map([[contractId, 14]]));
    expect(realized.renewalHref).toBeNull();
  });

  it("does not invent a tracked-action row — only real SavingsOpportunity payloads render", () => {
    const rows = buildOpportunityRows([opportunity()]);
    expect(rows).toHaveLength(1);
    expect(rows[0].key).toBe(opportunity().id);
  });
});

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

describe("buildKpiCells (headline band: verified first, then the open estimates)", () => {
  it("names the four cells in band order in both the failed-fetch and ready branches, verified money leading", () => {
    const keys = ["savings-verified", "savings-identified", "savings-in-progress", "savings-potential"];
    expect(buildKpiCells(null).map((cell) => cell.key)).toEqual(keys);
    expect(buildKpiCells(kpis()).map((cell) => cell.key)).toEqual(keys);
    expect(buildKpiCells(null).map((cell) => cell.label)).toEqual([
      "Savings verified",
      "Savings identified",
      "Savings in progress",
      "Savings potential",
    ]);
    expect(buildKpiCells(kpis()).map((cell) => cell.hero)).toEqual([true, false, false, false]);
  });

  it("kpis:null renders every cell with an honestly empty lines array, no meta and no notes", () => {
    const cells = buildKpiCells(null);
    expect(cells).toHaveLength(4);
    expect(cells.every((cell) => cell.lines.length === 0 && cell.meta === null && cell.notes.length === 0)).toBe(true);
  });

  it("carries real values and meta lines from the same response", () => {
    const [verified, identified, inProgress, potential] = buildKpiCells(kpis());
    expect(verified.lines).toEqual(["CHF 85,000"]);
    expect(verified.meta).toBe("from 1 recorded outcome");
    // 85,000 / 6,270,000 = 1.36% of the same currency's spend.
    expect(verified.notes).toEqual(["1.4% of CHF annual spend"]);
    expect(identified.lines).toEqual(["CHF 410,000–590,000"]);
    expect(identified.meta).toBe("6 opportunities · avg. confidence 82%");
    expect(inProgress.lines).toEqual(["CHF 240,000"]);
    expect(inProgress.meta).toBe("2 being negotiated");
    // (410k + 240k) / 6.27M = 10.4% ... (590k + 240k) / 6.27M = 13.2%
    expect(potential.lines).toEqual(["10–13%"]);
    expect(potential.meta).toBe("of annual spend analyzed, from open estimates");
  });

  it("multiple currency buckets render one line per currency, never summed across currencies", () => {
    const twoCurrencies = kpis({
      savingsIdentified: [
        { currency: "CHF", low: 410_000, high: 590_000, count: 6, averageConfidence: 0.82 },
        { currency: "USD", low: 20_000, high: 20_000, count: 1, averageConfidence: 0.7 },
      ],
      savingsRealized: [
        { currency: "CHF", amount: 85_000, count: 1 },
        { currency: "USD", amount: 12_000, count: 2 },
      ],
      annualSpendAnalyzed: [
        { currency: "CHF", amount: 6_270_000, contractCount: 9 },
        { currency: "USD", amount: 100_000, contractCount: 1 },
      ],
    });
    const [verified, identified, , potential] = buildKpiCells(twoCurrencies);
    expect(identified.lines).toEqual(["CHF 410,000–590,000", "USD 20,000"]);
    // Weighted by count: (0.82 * 6 + 0.7 * 1) / 7 = 0.803.
    expect(identified.meta).toBe("7 opportunities · avg. confidence 80%");
    expect(verified.lines).toEqual(["CHF 85,000", "USD 12,000"]);
    expect(verified.meta).toBe("from 3 recorded outcomes");
    expect(verified.notes).toEqual(["1.4% of CHF annual spend", "12% of USD annual spend"]);
    expect(potential.lines).toEqual(["CHF 10–13%", "USD 20%"]);
  });

  it("loaded empty verified savings is an em-dash with a non-null meta, never a fabricated 0", () => {
    const [verified] = buildKpiCells(kpis({ savingsRealized: [] }));
    expect(verified.lines).toEqual([]);
    expect(verified.meta).toBe("no verified savings recorded yet");
    expect(verified.notes).toEqual([]);
    expect(verified.label).toBe("Savings verified");
  });

  it("is honest when no spend has been analyzed yet: no share of spend is divided by nothing", () => {
    const [verified, , , potential] = buildKpiCells(kpis({ annualSpendAnalyzed: [], contractsAnalyzedCount: 0 }));
    expect(verified.notes).toEqual([]);
    expect(potential.lines).toEqual([]);
    expect(potential.meta).toBe("needs annual spend on validated contracts");
  });

  it("says so when nothing is open or being negotiated", () => {
    const [, identified, inProgress] = buildKpiCells(kpis({ savingsIdentified: [], savingsInProgress: [] }));
    expect(identified.lines).toEqual([]);
    expect(identified.meta).toBe("no open opportunities yet");
    expect(inProgress.meta).toBe("nothing being negotiated yet");
  });
});

describe("buildContextCells (portfolio context under the band)", () => {
  it("names contracts and spend analyzed, upcoming renewals and notice deadlines, each leading to the screen that owns it", () => {
    const cells = buildContextCells(kpis(), ["c-1", "c-2"]);
    expect(cells.map((cell) => [cell.key, cell.value, cell.href])).toEqual([
      ["contracts-analyzed", "9", "/contracts"],
      ["annual-spend", "CHF 6,270,000", "/contracts"],
      ["upcoming-renewals", "4", "/renewals"],
      ["notice-soon", "2", "/contracts?ids=c-1,c-2&from=savings"],
    ]);
    expect(cells[3].urgent).toBe(true);
    expect(cells[3].linkLabel).toBe("Show them");
  });

  it("is honest before anything loads, and with no deadline this close", () => {
    const empty = buildContextCells(null, null);
    expect(empty.map((cell) => cell.value)).toEqual(["—", "—", "—", "—"]);
    expect(empty[3].href).toBe("/renewals");

    const none = buildContextCells(kpis(), []);
    expect(none[3].value).toBe("0");
    expect(none[3].urgent).toBe(false);
    expect(none[3].meta).toBe("no notice deadline this close");
  });
});

describe("findNoticeSoonContractIds / buildOpportunitiesPortfolioHref", () => {
  const now = new Date("2026-09-23T10:00:00Z");
  function portfolioItem(contractId: string, cancellationDeadline: string | null, status = "active") {
    return {
      contractId,
      supplierId: null,
      supplierName: null,
      type: "Msa",
      annualSpend: null,
      currency: "CHF",
      startDate: null,
      endDate: null,
      renewalDate: null,
      cancellationDeadline,
      autoRenewal: true,
      status,
      risk: null,
      documentProcessingStatus: "Completed",
    } as const;
  }

  it("lists validated contracts whose notice is today or within 45 days -- never a passed or far deadline", () => {
    const ids = findNoticeSoonContractIds(
      [
        portfolioItem("soon", "2026-10-07"),
        portfolioItem("today", "2026-09-23"),
        portfolioItem("passed", "2026-09-01"),
        portfolioItem("far", "2027-03-01"),
        portfolioItem("none", null),
      ],
      now,
    );
    expect(ids).toEqual(["today", "soon"]);
  });

  it("narrows Portfolio to the distinct contracts behind the rows on screen, with Savings as the source", () => {
    const rows = buildOpportunityRows([opportunity(), opportunity({ id: "other" }), opportunity({ id: "quote", contractId: null })]);
    expect(buildOpportunitiesPortfolioHref(rows)).toBe(`/contracts?ids=${opportunity().contractId}&from=savings`);
    expect(buildOpportunitiesPortfolioHref(buildOpportunityRows([opportunity({ contractId: null })]))).toBeNull();
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
