import { describe, expect, it } from "vitest";
import type { RenewalActionRow, RenewalPipelineItemBody } from "../../../src/api/client";
import {
  DEFAULT_RENEWAL_STATUS_LABEL,
  RENEWALS_SUMMARY_OFF,
  RENEWAL_ACTION_KINDS,
  EMPTY_RENEWAL_FILTERS,
  UNASSIGNED_OWNER_LABEL,
  buildRenewalContractIndex,
  buildRenewalFacts,
  buildRenewalKpis,
  buildRenewalRows,
  filterRenewalRows,
  formatRenewalSpend,
  getRenewalOwner,
  getRenewalWorkflowState,
  isKpiFilterActive,
  isRenewalFilterActive,
  toggleKpiFilter,
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
  isRenewalItemReady,
} from "../../../src/routes/renewals/renewalPipelineViewModel";

function item(overrides: Partial<RenewalPipelineItemBody> = {}): RenewalPipelineItemBody {
  return {
    contractId: "c-1",
    supplierId: "s-1",
    supplierName: "Salesforce",
    status: "Determined",
    contractStatus: "active",
    documentProcessingStatus: "Completed",
    renewalDate: "2026-12-01",
    daysUntilRenewal: 83,
    annualSpend: 500_000,
    cancellationDeadline: "2026-10-01",
    daysUntilCancellationDeadline: 22,
    autoRenewal: true,
    action: "Finalize decision now",
    priority: null,
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

const tracked: RenewalActionRow = {
  contractId: "c-1",
  owner: "user@example.test",
  status: "InProgress",
  action: "In negotiation",
  updatedAt: "2026-09-06T09:00:00Z",
};

describe("buildRenewalRows (app.jsx: sorted by score, highest first)", () => {
  it("orders by score descending, puts unscored rows last, and attaches each row's own savedAction", () => {
    const rows = buildRenewalRows(
      [item({ contractId: "low" }), item({ contractId: "none" }), item({ contractId: "high", savedAction: { ...tracked, contractId: "high" } })],
      { low: 40, high: 91, none: null },
    );

    expect(rows.map((row) => row.item.contractId)).toEqual(["high", "low", "none"]);
    expect(rows.map((row) => row.score)).toEqual([91, 40, null]);
    expect(rows[0].tracked?.action).toBe("In negotiation");
    expect(rows[1].tracked).toBeNull();
  });

  it("Ready follows Portfolio validation, not date-determination", () => {
    expect(isRenewalItemReady(item({ status: "Determined", contractStatus: "active", documentProcessingStatus: "Completed" }))).toBe(true);
    expect(isRenewalItemReady(item({ status: "Determined", contractStatus: "needs_review", documentProcessingStatus: "NeedsReview" }))).toBe(false);
    expect(isRenewalItemReady(item({ status: "Determined", contractStatus: "processing", documentProcessingStatus: "Uploaded" }))).toBe(false);
    expect(isRenewalItemReady(item({ status: "Determined", contractStatus: "processing", documentProcessingStatus: "Processing" }))).toBe(false);
    expect(isRenewalItemReady(item({ status: "CannotDetermine", contractStatus: "active", documentProcessingStatus: "Completed" }))).toBe(true);
    expect(isRenewalItemReady(item({ status: "NoRenewal", contractStatus: "active", documentProcessingStatus: "Completed" }))).toBe(true);
  });

  it("treats a contract missing from the score map as unscored, not as zero", () => {
    const rows = buildRenewalRows([item({ contractId: "a" }), item({ contractId: "b" })], { a: 0 });

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
    );

    expect(rows.map((row) => row.item.contractId)).toEqual(["w", "y", "z", "x"]);
  });

  it("does not mutate the input array", () => {
    const items = [item({ contractId: "a" }), item({ contractId: "b" })];
    buildRenewalRows(items, { a: 1, b: 2 });
    expect(items.map((entry) => entry.contractId)).toEqual(["a", "b"]);
  });

  it("treats NotStarted as no action taken, never as a visible status", () => {
    const rows = buildRenewalRows(
      [item({ savedAction: { ...tracked, status: "NotStarted", action: "Open" } })],
      { "c-1": 50 },
    );
    expect(rows[0].tracked).toBeNull();
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

describe("portfolio enrichment (contract type + currency)", () => {
  it("indexes the portfolio's type label and currency per contract, and rows pick them up", () => {
    const index = buildRenewalContractIndex([{ contractId: "c-1", type: "Msa", currency: "CHF" }, { contractId: "c-2", type: "Sow" }] as never);
    expect(index.get("c-1")).toEqual({ typeLabel: "MSA", currency: "CHF" });
    expect(index.get("c-2")).toEqual({ typeLabel: "SOW", currency: null });

    const [row] = buildRenewalRows([item()], { "c-1": 80 }, index);
    expect(row.contract).toEqual({ typeLabel: "MSA", currency: "CHF" });
    expect(formatContractRef("c-1", row.contract)).toEqual({ label: "MSA", title: "c-1" });
    expect(formatRenewalSpend(500_000, row.contract)).toBe("CHF 500k");
    // No portfolio: never a guessed currency.
    expect(formatRenewalSpend(500_000, null)).toBe("500k");
    expect(formatRenewalSpend(null, row.contract)).toBe("—");
  });
});

describe("workflow state, owner, KPIs and list filters", () => {
  const rows = buildRenewalRows(
    [
      item({ contractId: "soon", supplierName: "Soon Co", daysUntilCancellationDeadline: 10, annualSpend: 100_000 }),
      item({ contractId: "later", supplierName: "Later Co", daysUntilCancellationDeadline: 60, annualSpend: 50_000 }),
      item({ contractId: "passed", supplierName: "Passed Co", daysUntilCancellationDeadline: -3 }),
      item({ contractId: "far", supplierName: "Far Co", daysUntilCancellationDeadline: 200, savedAction: { ...tracked, contractId: "far" } }),
      item({
        contractId: "closed",
        supplierName: "Closed Co",
        daysUntilCancellationDeadline: 20,
        savedAction: { ...tracked, contractId: "closed", status: "Completed", action: "Terminated — notice sent 08/09/2026" },
      }),
      item({
        contractId: "assigned",
        supplierName: "Assigned Co",
        daysUntilCancellationDeadline: null,
        savedAction: { ...tracked, contractId: "assigned", status: "NotStarted", action: "Assigned" },
      }),
    ],
    {},
    new Map([["soon", { typeLabel: "MSA", currency: "CHF" }], ["later", { typeLabel: "SOW", currency: "EUR" }]]),
  );
  const byId = (id: string) => rows.find((row) => row.item.contractId === id)!;

  it("reads the workflow state off the persisted action, and keeps the owner of an 'Assign to me' that has not started", () => {
    expect(getRenewalWorkflowState(byId("soon").tracked)).toBe("open");
    expect(getRenewalWorkflowState(byId("assigned").tracked)).toBe("open");
    expect(getRenewalOwner(byId("assigned").item)).toBe("user@example.test");
    expect(getRenewalOwner(byId("soon").item)).toBeNull();
    expect(getRenewalWorkflowState(byId("far").tracked)).toBe("negotiating");
    expect(getRenewalWorkflowState(byId("closed").tracked)).toBe("closed");
  });

  it("counts notice windows, spend per currency inside 90 days, and the workflow", () => {
    const kpis = buildRenewalKpis(rows);
    expect(kpis.map((cell) => [cell.key, cell.value])).toEqual([
      ["notice-30", "2"],
      ["notice-90", "3"],
      // soon CHF 100k + later EUR 50k + closed (no portfolio row: no code) 500k -- never summed across currencies.
      ["spend-90", "500k + CHF 100k + EUR 50k"],
      ["open", "4"],
      ["negotiating", "1"],
      ["closed", "1"],
    ]);
    expect(kpis[0].meta).toBe("1 notice date already passed");
    expect(kpis[0].urgent).toBe(true);
    expect(kpis[3].meta).toBe("3 with nobody assigned");
  });

  it("filters by notice window, workflow state and free text, and a KPI press toggles its own dimension", () => {
    const ids = (filters: typeof EMPTY_RENEWAL_FILTERS) => filterRenewalRows(rows, filters).map((row) => row.item.contractId);
    expect(ids(EMPTY_RENEWAL_FILTERS)).toHaveLength(6);
    expect(ids({ ...EMPTY_RENEWAL_FILTERS, window: "30" })).toEqual(["soon", "closed"]);
    expect(ids({ ...EMPTY_RENEWAL_FILTERS, window: "passed" })).toEqual(["passed"]);
    expect(ids({ ...EMPTY_RENEWAL_FILTERS, window: "90", state: "open" })).toEqual(["soon", "later"]);
    expect(ids({ ...EMPTY_RENEWAL_FILTERS, query: "sow" })).toEqual(["later"]);
    expect(isRenewalFilterActive(EMPTY_RENEWAL_FILTERS)).toBe(false);
    expect(isRenewalFilterActive({ ...EMPTY_RENEWAL_FILTERS, query: "  " })).toBe(false);

    const [in30, in90, spend90, open] = buildRenewalKpis(rows);
    const applied = toggleKpiFilter(in30, { ...EMPTY_RENEWAL_FILTERS, state: "open" });
    expect(applied).toEqual({ window: "30", state: "open", query: "" });
    expect(isKpiFilterActive(in30, applied)).toBe(true);
    expect(isKpiFilterActive(open, applied)).toBe(true);
    expect(toggleKpiFilter(in30, applied)).toEqual({ window: "all", state: "open", query: "" });
    // The two 90-day cells are one filter.
    const ninety = toggleKpiFilter(spend90, EMPTY_RENEWAL_FILTERS);
    expect(isKpiFilterActive(in90, ninety)).toBe(true);
  });

  it("lists the pane's four key facts, honest where the wire has none", () => {
    expect(buildRenewalFacts({ ...byId("soon"), score: 91 }).map((fact) => [fact.label, fact.value, fact.urgent])).toEqual([
      ["Notice by", "01 Oct 2026", true],
      ["Renews", "01 Dec 2026", false],
      ["Annual spend", "CHF 100k", false],
      ["Priority", "91 / 100", true],
    ]);
    const bare = buildRenewalFacts({ ...byId("assigned"), score: null, item: { ...byId("assigned").item, cancellationDeadline: null, renewalDate: null, annualSpend: null } });
    expect(bare.map((fact) => fact.value)).toEqual(["—", "—", "—", "—"]);
  });
});
