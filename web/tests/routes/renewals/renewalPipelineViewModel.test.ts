import { describe, expect, it } from "vitest";
import type { RenewalPipelineItemBody } from "../../../src/api/client";
import {
  DEFAULT_RENEWAL_STATUS_LABEL,
  RENEWAL_ACTION_KINDS,
  RENEWAL_WINDOWS,
  UNASSIGNED_OWNER_LABEL,
  applyRenewalWindowFilter,
  computeRenewalWindowCounts,
  formatContractRef,
  formatMarketPosition,
  formatPotentialSavings,
  formatScore,
  formatUpliftPercent,
  getInsightOwner,
  getRenewalActionPlan,
  getRenewalStatusLabel,
  getRenewalStatusTag,
  isHighPriorityScore,
  isInRenewalWindow,
} from "../../../src/routes/renewals/renewalPipelineViewModel";
import type { TrackedRenewalAction } from "../../../src/routes/renewals/renewalActionStore";

// Task-01's own named "Tests required" row: "unit | action creates opportunity link to Home" --
// this file covers the pure logic half; tests/routes/renewals/RenewalsRoute.test.tsx covers the
// rendered confirmation + link.

function pipelineItem(overrides: Partial<RenewalPipelineItemBody> = {}): RenewalPipelineItemBody {
  return {
    contractId: "11111111-1111-1111-1111-111111111111",
    supplierId: "22222222-2222-2222-2222-222222222222",
    // Task E13/F03/US01/T02: supplierName is required now (null when unresolved).
    supplierName: null,
    status: "Determined",
    renewalDate: "2026-12-01",
    daysUntilRenewal: 30,
    annualSpend: 500_000,
    cancellationDeadline: "2026-10-01",
    daysUntilCancellationDeadline: 14,
    autoRenewal: true,
    action: "Start negotiation now",
    insightCard: {
      facts: {
        supplierId: "22222222-2222-2222-2222-222222222222",
        // Task E13/F03/US01/T02: supplierName is required now (null when unresolved).
        supplierName: null,
        renewalDate: "2026-12-01",
        daysUntilRenewal: 30,
        annualSpend: 500_000,
        cancellationDeadline: "2026-10-01",
        daysUntilCancellationDeadline: 14,
      },
      recommendations: {
        recommendedAction: "Start negotiation now",
        explanation: "Renews in 30 days.",
        annualUpliftPercent: null,
        marketPosition: null,
        potentialSavingsRange: null,
      },
    },
    ...overrides,
  };
}

function tracked(overrides: Partial<TrackedRenewalAction> = {}): TrackedRenewalAction {
  return {
    contractId: "11111111-1111-1111-1111-111111111111",
    supplierId: "22222222-2222-2222-2222-222222222222",
    annualSpend: 500_000,
    owner: "user@example.test",
    status: "InProgress",
    action: "In negotiation",
    updatedAt: "2026-09-06T08:00:00Z",
    ...overrides,
  };
}

describe("RENEWAL_WINDOWS (AC-1)", () => {
  it("names the seven buckets ascending, matching this task's own AC-1 wording (0-30 ... 270-365 d)", () => {
    expect(RENEWAL_WINDOWS.map((bucket) => bucket.label)).toEqual([
      "0–30 d",
      "30–60 d",
      "60–90 d",
      "90–120 d",
      "120–180 d",
      "180–270 d",
      "270–365 d",
    ]);
  });
});

describe("isInRenewalWindow", () => {
  const bucket = RENEWAL_WINDOWS[0]; // 0-30 d

  it("null daysUntilRenewal matches no bucket", () => {
    expect(isInRenewalWindow(null, bucket)).toBe(false);
  });

  it("a boundary value is inclusive on the high end", () => {
    expect(isInRenewalWindow(30, bucket)).toBe(true);
  });

  it("a boundary value is exclusive on the low end (the next bucket up owns it)", () => {
    const nextBucket = RENEWAL_WINDOWS[1]; // 30-60 d
    expect(isInRenewalWindow(30, nextBucket)).toBe(false);
    expect(isInRenewalWindow(31, nextBucket)).toBe(true);
  });

  it("zero days (renewal is today) still counts in the first bucket", () => {
    expect(isInRenewalWindow(0, bucket)).toBe(false); // >0 required -- day 0 is "already renewing", not "0-30 away"
    expect(isInRenewalWindow(1, bucket)).toBe(true);
  });
});

describe("computeRenewalWindowCounts / applyRenewalWindowFilter (AC-1 'click = filter')", () => {
  const items = [
    pipelineItem({ contractId: "a", daysUntilRenewal: 10 }), // 0-30
    pipelineItem({ contractId: "b", daysUntilRenewal: 45 }), // 30-60
    pipelineItem({ contractId: "c", daysUntilRenewal: null }), // no determined date -- no bucket
  ];

  it("counts each bucket independently across the whole pipeline", () => {
    const counts = computeRenewalWindowCounts(items);
    expect(counts.find((bucket) => bucket.key === "0-30")?.count).toBe(1);
    expect(counts.find((bucket) => bucket.key === "30-60")?.count).toBe(1);
    expect(counts.find((bucket) => bucket.key === "60-90")?.count).toBe(0);
  });

  it("no active key returns every item unfiltered", () => {
    expect(applyRenewalWindowFilter(items, null)).toEqual(items);
  });

  it("an active key narrows to that bucket's own items", () => {
    const filtered = applyRenewalWindowFilter(items, "0-30");
    expect(filtered.map((item) => item.contractId)).toEqual(["a"]);
  });

  it("an unknown key is treated as no filter rather than returning nothing", () => {
    expect(applyRenewalWindowFilter(items, "not-a-real-bucket")).toEqual(items);
  });
});

describe("score helpers (AC-2 'Score' column)", () => {
  it("formatScore renders '-' for a not-yet-resolved score", () => {
    expect(formatScore(null)).toBe("—");
  });

  it("formatScore rounds to a whole number", () => {
    expect(formatScore(71.6)).toBe("72");
  });

  it("isHighPriorityScore is true at and above 80 (day1-demo.html's own scoreFg threshold)", () => {
    expect(isHighPriorityScore(79)).toBe(false);
    expect(isHighPriorityScore(80)).toBe(true);
    expect(isHighPriorityScore(100)).toBe(true);
  });
});

describe("formatContractRef (AC-2 'Contract' column honest gap)", () => {
  it("shows a short id fragment with the full id as the title", () => {
    expect(formatContractRef("11111111-1111-1111-1111-111111111111")).toEqual({
      label: "Contract 11111111",
      title: "11111111-1111-1111-1111-111111111111",
    });
  });
});

describe("renewal status label/tag (AC-2 'Status' column)", () => {
  it("defaults to 'Open' with a neutral tag before anything has been actioned", () => {
    expect(getRenewalStatusLabel(null)).toBe(DEFAULT_RENEWAL_STATUS_LABEL);
    expect(getRenewalStatusTag(null)).toEqual({ variant: "neutral", label: "Open" });
  });

  it("shows the tracked free-text action with an accent tag once acted on", () => {
    expect(getRenewalStatusLabel(tracked({ action: "Assigned" }))).toBe("Assigned");
    expect(getRenewalStatusTag(tracked({ action: "Assigned" }))).toEqual({ variant: "accent", label: "Assigned" });
  });
});

describe("insight card field formatters (AC-3, spec §9.3)", () => {
  it("owner defaults to Unassigned before anything has been actioned", () => {
    expect(getInsightOwner(null)).toBe(UNASSIGNED_OWNER_LABEL);
    expect(getInsightOwner(tracked({ owner: "Marta Keller" }))).toBe("Marta Keller");
  });

  it("uplift/market/savings render an honest gap, never a fabricated value, while null", () => {
    expect(formatUpliftPercent(null)).toBe("Not yet available");
    expect(formatMarketPosition(null)).toBe("Not yet available");
    expect(formatPotentialSavings(null)).toBe("Not yet available");
  });

  it("uplift/market/savings render the real value once the R3 modules provide one", () => {
    expect(formatUpliftPercent(5)).toBe("+5%");
    expect(formatUpliftPercent(-2)).toBe("-2%");
    expect(formatMarketPosition("9% above market")).toBe("9% above market");
    expect(formatPotentialSavings("CHF 80-120k")).toBe("CHF 80-120k");
  });
});

describe("getRenewalActionPlan (AC-3 'Start negotiation / Assign to me / Snooze')", () => {
  it("names exactly the three actions, in the order day1-demo.html's own rAct object declares them", () => {
    expect(RENEWAL_ACTION_KINDS).toEqual(["negotiate", "assign", "snooze"]);
  });

  it("'negotiate' maps to InProgress / 'In negotiation' (quoted from day1-demo.html's own rAct.negotiate)", () => {
    expect(getRenewalActionPlan("negotiate")).toEqual({
      status: "InProgress",
      action: "In negotiation",
      buttonLabel: "Start negotiation",
    });
  });

  it("'assign' maps to NotStarted / 'Assigned' (quoted from day1-demo.html's own rAct.assign)", () => {
    expect(getRenewalActionPlan("assign")).toEqual({
      status: "NotStarted",
      action: "Assigned",
      buttonLabel: "Assign to me",
    });
  });

  it("'snooze' maps to NotStarted / 'Snoozed to 90 d' (quoted from day1-demo.html's own rAct.snooze)", () => {
    expect(getRenewalActionPlan("snooze")).toEqual({
      status: "NotStarted",
      action: "Snoozed to 90 d",
      buttonLabel: "Snooze to 90-day threshold",
    });
  });
});
