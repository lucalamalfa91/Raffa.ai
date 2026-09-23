import { describe, expect, it } from "vitest";
import type { PortfolioPageBody, SavingsOpportunityBody } from "../../../src/api/client";
import {
  buildDeadlineQueue,
  buildMonthlyScale,
  buildMonthlySavings,
  buildNoticeIndex,
  buildRecentVerified,
  buildSavingsBreakdown,
  buildSavingsPipeline,
  buildVerifiedStats,
  findPeakVerifiedMonth,
  formatCompactMoney,
  formatCompactRange,
  formatPercent,
  listDashboardCurrencies,
  niceCeiling,
} from "../../../src/routes/savings/savingsDashboard";

const NOW = new Date("2026-09-23T10:00:00Z");

function opportunity(overrides: Partial<SavingsOpportunityBody> = {}): SavingsOpportunityBody {
  return {
    id: "opp-1",
    supplierId: "s-1",
    contractId: "c-1",
    type: "Renewal",
    currentSpend: 640_000,
    currency: "CHF",
    estimatedSavingsLow: 80_000,
    estimatedSavingsHigh: 120_000,
    confidence: 0.9,
    confidenceLevel: "High",
    status: "Identified",
    owner: null,
    createdAt: "2026-08-01T00:00:00Z",
    updatedAt: "2026-08-01T00:00:00Z",
    realizedAmount: null,
    ...overrides,
  };
}

function realized(id: string, amount: number | null, updatedAt: string, overrides: Partial<SavingsOpportunityBody> = {}): SavingsOpportunityBody {
  return opportunity({ id, status: "Realized", realizedAmount: amount, updatedAt, ...overrides });
}

const NAMES: Record<string, string> = { "c-1": "Salesforce", "c-2": "Fabrikam", "c-3": "Contoso" };
const supplierLabel = (item: SavingsOpportunityBody) => (item.contractId !== null ? (NAMES[item.contractId] ?? "Supplier not resolved") : "Supplier not resolved");

describe("formatters", () => {
  it("compacts money for chart labels, and collapses a zero-width range", () => {
    expect(formatCompactMoney("CHF", 85_000)).toBe("CHF 85k");
    expect(formatCompactMoney("CHF", 1_240_000)).toBe("CHF 1.2M");
    expect(formatCompactRange("CHF", 80_000, 120_000)).toBe("CHF 80k–120k");
    expect(formatCompactRange("CHF", 50_000, 50_000)).toBe("CHF 50k");
  });

  it("shows one decimal under 10%, whole percent from there", () => {
    expect(formatPercent(0.0136)).toBe("1.4%");
    expect(formatPercent(0.125)).toBe("13%");
    expect(formatPercent(0)).toBe("0%");
  });

  it("rounds a chart's top tick up to a clean number", () => {
    expect(niceCeiling(0)).toBe(0);
    expect(niceCeiling(85_000)).toBe(100_000);
    expect(niceCeiling(120_000)).toBe(200_000);
    expect(niceCeiling(230_000)).toBe(250_000);
    expect(niceCeiling(50_000)).toBe(50_000);
  });
});

describe("listDashboardCurrencies", () => {
  it("orders currencies by money at stake, largest first, and never invents one", () => {
    expect(listDashboardCurrencies([])).toEqual([]);
    expect(
      listDashboardCurrencies([
        opportunity({ id: "a", currency: "EUR", estimatedSavingsHigh: 10_000 }),
        opportunity({ id: "b", currency: "CHF", estimatedSavingsHigh: 120_000 }),
        realized("c", 50_000, "2026-09-01T00:00:00Z", { currency: "EUR" }),
      ]),
    ).toEqual(["CHF", "EUR"]);
  });
});

describe("buildSavingsPipeline", () => {
  it("is null for a currency with no opportunity, never an all-zero bar", () => {
    expect(buildSavingsPipeline([opportunity()], "EUR")).toBeNull();
  });

  it("sizes estimate stages at their midpoint and verified at recorded money, with ranges in the labels", () => {
    const pipeline = buildSavingsPipeline(
      [
        opportunity({ id: "a" }),
        opportunity({ id: "b", status: "InProgress", estimatedSavingsLow: 40_000, estimatedSavingsHigh: 60_000 }),
        realized("c", 50_000, "2026-09-01T00:00:00Z"),
        // A realized opportunity with no recorded amount is counted, never valued.
        realized("d", null, "2026-09-02T00:00:00Z"),
        opportunity({ id: "eur", currency: "EUR" }),
      ],
      "CHF",
    )!;

    expect(pipeline.segments.map((segment) => [segment.key, segment.value, segment.amountLabel, segment.countLabel])).toEqual([
      ["identified", 100_000, "CHF 80k–120k", "1 opportunity"],
      ["inProgress", 50_000, "CHF 40k–60k", "1 opportunity"],
      ["verified", 50_000, "CHF 50k", "2 opportunities"],
    ]);
    expect(pipeline.total).toBe(200_000);
    expect(pipeline.segments.map((segment) => segment.share)).toEqual([0.5, 0.25, 0.25]);
    expect(pipeline.verifiedShareLine).toBe("25% of the pipeline is already verified money");
  });

  it("has no verified line while nothing is verified", () => {
    const pipeline = buildSavingsPipeline([opportunity()], "CHF")!;
    expect(pipeline.segments[2].amountLabel).toBe("—");
    expect(pipeline.verifiedShareLine).toBeNull();
  });
});

describe("buildMonthlySavings", () => {
  const items = [
    opportunity({ id: "found-aug", createdAt: "2026-08-10T00:00:00Z" }),
    realized("won-sep", 50_000, "2026-09-15T00:00:00Z", { createdAt: "2026-07-01T00:00:00Z" }),
    realized("won-jul", 30_000, "2026-07-31T23:30:00Z", { createdAt: "2026-06-01T00:00:00Z" }),
    realized("too-old", 99_000, "2025-01-01T00:00:00Z", { createdAt: "2025-01-01T00:00:00Z" }),
    realized("eur", 10_000, "2026-09-01T00:00:00Z", { currency: "EUR" }),
  ];

  it("always spans the full twelve months ending this month, empty months included", () => {
    const buckets = buildMonthlySavings(items, "CHF", NOW);
    expect(buckets).toHaveLength(12);
    expect(buckets[0].key).toBe("2025-10");
    expect(buckets[11].key).toBe("2026-09");
    expect(buckets[11].label).toBe("Sep");
    expect(buckets[11].longLabel).toBe("September 2026");
  });

  it("files verified money under the month it was recorded and savings found under the month they were created, in one currency", () => {
    const buckets = buildMonthlySavings(items, "CHF", NOW);
    const byKey = Object.fromEntries(buckets.map((bucket) => [bucket.key, bucket]));
    expect(byKey["2026-09"].verified).toBe(50_000);
    expect(byKey["2026-09"].verifiedCount).toBe(1);
    expect(byKey["2026-07"].verified).toBe(30_000);
    expect(byKey["2026-08"].identified).toBe(100_000);
    expect(byKey["2026-08"].identifiedCount).toBe(1);
    expect(byKey["2026-07"].identified).toBe(100_000);
    // Outside the window, or another currency: never counted.
    expect(buckets.reduce((total, bucket) => total + bucket.verified, 0)).toBe(80_000);
    expect(byKey["2026-07"].cumulativeVerified).toBe(30_000);
    expect(byKey["2026-09"].cumulativeVerified).toBe(80_000);
  });

  it("scales both series on one axis and labels only the peak verified month", () => {
    const buckets = buildMonthlySavings(items, "CHF", NOW);
    expect(buildMonthlyScale(buckets)).toEqual({ max: 100_000, ticks: [0, 50_000, 100_000] });
    expect(findPeakVerifiedMonth(buckets)).toBe("2026-09");
    const empty = buildMonthlySavings([], "CHF", NOW);
    expect(buildMonthlyScale(empty)).toEqual({ max: 0, ticks: [0] });
    expect(findPeakVerifiedMonth(empty)).toBeNull();
  });
});

describe("buildVerifiedStats / buildRecentVerified (when you saved)", () => {
  const items = [
    realized("recent", 50_000, "2026-09-15T00:00:00Z", { contractId: "c-2", type: "Benchmark" }),
    realized("spring", 30_000, "2026-03-10T00:00:00Z"),
    realized("last-year", 20_000, "2025-11-20T00:00:00Z", { contractId: "c-3" }),
    realized("unrecorded", null, "2026-09-20T00:00:00Z"),
  ];

  it("sums verified money this year and in the last 90 days, and names the last verified saving", () => {
    const [year, quarter, last] = buildVerifiedStats(items, "CHF", supplierLabel, NOW);
    expect(year).toMatchObject({ label: "Verified in 2026", value: "CHF 80,000", meta: "2 recorded outcomes" });
    expect(quarter).toMatchObject({ label: "Last 90 days", value: "CHF 50,000", meta: "1 recorded outcome" });
    expect(last).toMatchObject({ label: "Last verified saving", value: "15 Sep 2026", meta: "Fabrikam · CHF 50,000" });
  });

  it("is an honest dash with nothing verified", () => {
    const stats = buildVerifiedStats([opportunity()], "CHF", supplierLabel, NOW);
    expect(stats.map((stat) => stat.value)).toEqual(["—", "—", "—"]);
    expect(stats[2].meta).toBe("no verified savings recorded yet");
  });

  it("lists recorded outcomes newest first, with the day, supplier, lever and amount", () => {
    expect(buildRecentVerified(items, "CHF", supplierLabel, 2)).toEqual([
      { key: "recent", date: "15 Sep 2026", supplierLabel: "Fabrikam", lever: "Benchmark", amount: "CHF 50,000", contractId: "c-2" },
      { key: "spring", date: "10 Mar 2026", supplierLabel: "Salesforce", lever: "Renewal", amount: "CHF 30,000", contractId: "c-1" },
    ]);
  });
});

describe("buildSavingsBreakdown", () => {
  const items = [
    opportunity({ id: "a", contractId: "c-1" }),
    realized("b", 40_000, "2026-09-01T00:00:00Z", { contractId: "c-1" }),
    opportunity({ id: "c", contractId: "c-2", type: "Benchmark", status: "InProgress", estimatedSavingsLow: 10_000, estimatedSavingsHigh: 30_000 }),
    opportunity({ id: "d", contractId: "c-3", estimatedSavingsLow: 1_000, estimatedSavingsHigh: 3_000 }),
  ];

  it("groups by supplier, largest first, stacking the three stages and scaling to the largest row", () => {
    const rows = buildSavingsBreakdown(items, "CHF", "supplier", supplierLabel);
    expect(rows.map((row) => [row.label, row.total, row.scale, row.totalLabel, row.opportunityCount])).toEqual([
      ["Salesforce", 140_000, 1, "CHF 140k", 2],
      ["Fabrikam", 20_000, 20_000 / 140_000, "CHF 20k", 1],
      ["Contoso", 2_000, 2_000 / 140_000, "CHF 2k", 1],
    ]);
    expect(rows[0].values).toEqual({ identified: 100_000, inProgress: 0, verified: 40_000 });
    expect(rows[0].contractId).toBe("c-1");
  });

  it("groups by lever, and folds a long tail into one Other row", () => {
    const byLever = buildSavingsBreakdown(items, "CHF", "lever", supplierLabel);
    expect(byLever.map((row) => row.label)).toEqual(["Renewal", "Benchmark"]);
    expect(byLever.every((row) => row.contractId === null)).toBe(true);

    const folded = buildSavingsBreakdown(items, "CHF", "supplier", supplierLabel, 2);
    expect(folded.map((row) => [row.label, row.total, row.isOther])).toEqual([
      ["Salesforce", 140_000, false],
      ["Other suppliers (2)", 22_000, true],
    ]);
  });
});

describe("buildDeadlineQueue / buildNoticeIndex (act before the notice deadline)", () => {
  function contract(contractId: string, cancellationDeadline: string | null): PortfolioPageBody["items"][number] {
    return {
      contractId,
      supplierId: null,
      supplierName: NAMES[contractId] ?? null,
      type: "Msa",
      annualSpend: null,
      startDate: null,
      endDate: null,
      renewalDate: null,
      cancellationDeadline,
      autoRenewal: true,
      status: "active",
      risk: null,
    };
  }
  const portfolio = [contract("c-1", "2026-10-07"), contract("c-2", "2026-12-31"), contract("c-3", "2026-09-01")];

  it("lists open savings on contracts whose notice date is ahead, soonest first, urgent inside 45 days", () => {
    const rows = buildDeadlineQueue(
      [
        opportunity({ id: "far", contractId: "c-2", status: "InProgress" }),
        opportunity({ id: "soon", contractId: "c-1" }),
        opportunity({ id: "passed", contractId: "c-3" }),
        realized("done", 10_000, "2026-09-01T00:00:00Z", { contractId: "c-1" }),
        opportunity({ id: "quote", contractId: null }),
      ],
      portfolio,
      supplierLabel,
      NOW,
    );
    expect(rows.map((row) => [row.key, row.daysToNotice, row.isUrgent, row.daysLabel, row.statusLabel, row.deadline])).toEqual([
      ["soon", 14, true, "in 14 days", "Identified", "07 Oct 2026"],
      ["far", 99, false, "in 99 days", "In progress", "31 Dec 2026"],
    ]);
    expect(rows[0].supplierLabel).toBe("Salesforce");
    expect(rows[0].estimate).toBe("CHF 80k–120k");
  });

  it("stops at the horizon", () => {
    const rows = buildDeadlineQueue([opportunity({ contractId: "c-2" })], portfolio, supplierLabel, NOW, 30);
    expect(rows).toEqual([]);
  });

  it("indexes only notice dates today or ahead", () => {
    expect([...buildNoticeIndex(portfolio, NOW).entries()]).toEqual([
      ["c-1", 14],
      ["c-2", 99],
    ]);
  });
});
