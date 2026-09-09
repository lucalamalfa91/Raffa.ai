import { describe, expect, it } from "vitest";
import type { RenewalPipelineItemBody } from "../../../src/api/client";
import type { TrackedRenewalAction } from "../../../src/routes/renewals/renewalActionStore";
import {
  DEFAULT_RENEWAL_STATUS_LABEL,
  RENEWALS_SUMMARY_OFF,
  RENEWAL_ACTION_KINDS,
  UNASSIGNED_OWNER_LABEL,
  buildRenewalRows,
  formatContractRef,
  formatDays,
  formatPaneHeading,
  formatRenewalSupplier,
  formatRenewalsSummary,
  formatScore,
  getInsightOwner,
  getRenewalActionPlan,
  getRenewalStatusLabel,
  getRenewalStatusTag,
  isHighPriorityScore,
  isNoticeUrgent,
} from "../../../src/routes/renewals/renewalPipelineViewModel";

function item(overrides: Partial<RenewalPipelineItemBody> = {}): RenewalPipelineItemBody {
  return {
    contractId: "c-1",
    supplierId: "s-1",
    supplierName: "Salesforce",
    status: "Determined",
    renewalDate: "2026-12-01",
    daysUntilRenewal: 83,
    annualSpend: 500_000,
    cancellationDeadline: "2026-10-01",
    daysUntilCancellationDeadline: 22,
    autoRenewal: true,
    action: "Finalize decision now",
    insightCard: {
      facts: {
        supplierId: "s-1",
        supplierName: "Salesforce",
        renewalDate: "2026-12-01",
        daysUntilRenewal: 83,
        annualSpend: 500_000,
        cancellationDeadline: "2026-10-01",
        daysUntilCancellationDeadline: 22,
      },
      recommendations: {
        recommendedAction: "Finalize decision now",
        explanation: "The cancellation deadline is 22 day(s) away.",
        annualUpliftPercent: null,
        marketPosition: null,
        potentialSavingsRange: null,
      },
    },
    ...overrides,
  };
}

const tracked: TrackedRenewalAction = {
  contractId: "c-1",
  supplierId: "s-1",
  annualSpend: 500_000,
  owner: "user@example.test",
  status: "InProgress",
  action: "In negotiation",
  updatedAt: "2026-09-06T09:00:00Z",
};

describe("buildRenewalRows (app.jsx: sorted by score, highest first)", () => {
  it("orders by score descending, puts unscored rows last, and attaches each row's own tracked action", () => {
    const rows = buildRenewalRows(
      [item({ contractId: "low" }), item({ contractId: "none" }), item({ contractId: "high" })],
      { low: 40, high: 91, none: null },
      new Map([["high", { ...tracked, contractId: "high" }]]),
    );

    expect(rows.map((row) => row.item.contractId)).toEqual(["high", "low", "none"]);
    expect(rows.map((row) => row.score)).toEqual([91, 40, null]);
    expect(rows[0].tracked?.action).toBe("In negotiation");
    expect(rows[1].tracked).toBeNull();
  });

  it("treats a contract missing from the score map as unscored, not as zero", () => {
    const rows = buildRenewalRows([item({ contractId: "a" }), item({ contractId: "b" })], { a: 0 }, new Map());

    expect(rows.map((row) => row.item.contractId)).toEqual(["a", "b"]);
    expect(rows[1].score).toBeNull();
  });

  it("breaks score ties on the sooner notice deadline, then on the contract id", () => {
    const rows = buildRenewalRows(
      [
        item({ contractId: "z", daysUntilCancellationDeadline: 30 }),
        item({ contractId: "y", daysUntilCancellationDeadline: 30 }),
        item({ contractId: "x", daysUntilCancellationDeadline: null }),
        item({ contractId: "w", daysUntilCancellationDeadline: 5 }),
      ],
      { z: 70, y: 70, x: 70, w: 70 },
      new Map(),
    );

    expect(rows.map((row) => row.item.contractId)).toEqual(["w", "y", "z", "x"]);
  });

  it("does not mutate the input array", () => {
    const items = [item({ contractId: "a" }), item({ contractId: "b" })];
    buildRenewalRows(items, { a: 1, b: 2 }, new Map());
    expect(items.map((entry) => entry.contractId)).toEqual(["a", "b"]);
  });
});

describe("formatRenewalsSummary (app.jsx rnSummary)", () => {
  it("is the off-tier sentence with nothing validated", () => {
    expect(formatRenewalsSummary(0)).toBe(RENEWALS_SUMMARY_OFF);
    expect(RENEWALS_SUMMARY_OFF).toBe("Computed from validated end dates and notice periods");
  });

  it("counts contracts with validated dates, singular and plural", () => {
    expect(formatRenewalsSummary(1)).toBe("1 contract with validated dates · sorted by priority");
    expect(formatRenewalsSummary(4)).toBe("4 contracts with validated dates · sorted by priority");
  });
});

describe("score and day formatting", () => {
  it("emphasises scores from 80 up (app.jsx scoreFg)", () => {
    expect(isHighPriorityScore(79)).toBe(false);
    expect(isHighPriorityScore(80)).toBe(true);
  });

  it("renders an honest dash for an unresolved score and rounds a real one", () => {
    expect(formatScore(null)).toBe("—");
    expect(formatScore(84.6)).toBe("85");
  });

  it("renders days as 'N d' or a dash", () => {
    expect(formatDays(14)).toBe("14 d");
    expect(formatDays(0)).toBe("0 d");
    expect(formatDays(null)).toBe("—");
  });

  it("flags a notice deadline only inside the locked critical window", () => {
    expect(isNoticeUrgent(null)).toBe(false);
    expect(isNoticeUrgent(14)).toBe(true);
    expect(isNoticeUrgent(200)).toBe(false);
  });
});

describe("Supplier · contract column", () => {
  it("shortens the contract id and keeps the full id as the tooltip", () => {
    expect(formatContractRef("22222222-2222-2222-2222-222222222222")).toEqual({
      label: "Contract 22222222",
      title: "22222222-2222-2222-2222-222222222222",
    });
  });

  it("uses the wire's supplier name or an honest placeholder, never an id fragment", () => {
    expect(formatRenewalSupplier("Salesforce")).toBe("Salesforce");
    expect(formatRenewalSupplier(null)).toBe("Supplier not resolved");
  });
});

describe("Status column and owner (app.jsx st / stTag)", () => {
  it("is a neutral 'Open' before any action this session", () => {
    expect(DEFAULT_RENEWAL_STATUS_LABEL).toBe("Open");
    expect(getRenewalStatusLabel(null)).toBe("Open");
    expect(getRenewalStatusTag(null)).toEqual({ variant: "neutral", label: "Open" });
    expect(getInsightOwner(null)).toBe(UNASSIGNED_OWNER_LABEL);
  });

  it("is the accent-tagged action once acted on", () => {
    expect(getRenewalStatusLabel(tracked)).toBe("In negotiation");
    expect(getRenewalStatusTag(tracked)).toEqual({ variant: "accent", label: "In negotiation" });
    expect(getInsightOwner(tracked)).toBe("user@example.test");
  });
});

describe("formatPaneHeading (markup.html '{{ rsel.supplier }} — {{ rsel.cancelDays }} days to notice')", () => {
  it("counts the days to notice, singular and plural", () => {
    expect(formatPaneHeading("Salesforce", 14)).toBe("Salesforce — 14 days to notice");
    expect(formatPaneHeading("Salesforce", 1)).toBe("Salesforce — 1 day to notice");
  });

  it("says so when the notice date is not determined, with the supplier placeholder when unresolved", () => {
    expect(formatPaneHeading(null, null)).toBe("Supplier not resolved — notice date not determined");
  });
});

describe("getRenewalActionPlan (markup.html rAct)", () => {
  it("offers exactly Start negotiation (primary) then Assign to me (secondary)", () => {
    expect(RENEWAL_ACTION_KINDS).toEqual(["negotiate", "assign"]);
    expect(getRenewalActionPlan("negotiate")).toEqual({
      status: "InProgress",
      action: "In negotiation",
      buttonLabel: "Start negotiation",
      emphasis: "primary",
    });
    expect(getRenewalActionPlan("assign")).toEqual({
      status: "NotStarted",
      action: "Assigned",
      buttonLabel: "Assign to me",
      emphasis: "secondary",
    });
  });
});
