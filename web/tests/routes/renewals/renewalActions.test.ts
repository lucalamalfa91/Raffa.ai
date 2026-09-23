import { describe, expect, it } from "vitest";
import type { RenewalActionRow, RenewalPipelineItemBody } from "../../../src/api/client";
import { buildRenewalRows, type RenewalTableRow } from "../../../src/routes/renewals/renewalPipelineViewModel";
import {
  RENEWAL_ACTIONS,
  RENEWAL_ACTION_GROUPS,
  buildRenewalActionContext,
  buildRenewalActionGroups,
  buildRenewalBulkActions,
  type RenewalActionDefinition,
} from "../../../src/routes/renewals/renewalActions";

function item(overrides: Partial<RenewalPipelineItemBody> = {}): RenewalPipelineItemBody {
  return {
    contractId: "c-1",
    supplierId: null,
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
        supplierId: null,
        supplierName: "Salesforce",
        renewalDate: null,
        daysUntilRenewal: null,
        annualSpend: null,
        cancellationDeadline: null,
        daysUntilCancellationDeadline: null,
      },
      recommendations: { recommendedAction: "x", explanation: "y", annualUpliftPercent: null, marketPosition: null, potentialSavingsRange: null },
    },
    ...overrides,
  };
}

function row(overrides: Partial<RenewalPipelineItemBody> = {}): RenewalTableRow {
  return buildRenewalRows([item(overrides)], {})[0];
}

const saved = (status: RenewalActionRow["status"]): RenewalActionRow => ({
  contractId: "c-1",
  owner: "someone@example.test",
  status,
  action: status === "Completed" ? "Terminated — notice sent 08/09/2026" : "In negotiation",
  updatedAt: "2026-09-06T09:00:00Z",
});

describe("the renewal action registry", () => {
  it("declares every action once, in a known group, with a unique id", () => {
    const ids = RENEWAL_ACTIONS.map((action) => action.id);
    expect(new Set(ids).size).toBe(ids.length);
    const groups = new Set(RENEWAL_ACTION_GROUPS.map((group) => group.key));
    expect(RENEWAL_ACTIONS.every((action) => groups.has(action.group))).toBe(true);
    // A "soon" action is in the "soon" group and vice versa: the tag and the heading never disagree.
    expect(RENEWAL_ACTIONS.every((action) => (action.status === "soon") === (action.group === "soon"))).toBe(true);
  });

  it("resolves an open renewal into Ask launches bound to the contract, screen links, and the coming operations", () => {
    const groups = buildRenewalActionGroups(buildRenewalActionContext(row()));
    expect(groups.map((group) => [group.key, group.actions.map((action) => action.id)])).toEqual([
      ["ask", ["plan", "draft-email", "market", "why-here"]],
      ["open", ["contract-360", "savings", "portfolio", "quote-check"]],
      ["soon", ["reminder", "calendar", "send-notice", "export"]],
    ]);
    const plan = groups[0].actions[0];
    expect(plan.target).toEqual({ kind: "ask", question: "How should we approach the Salesforce renewal?", scopeContractId: "c-1" });
    expect(groups[1].actions[0].target).toEqual({
      kind: "link",
      to: "/contracts/c-1",
      state: { from: "renewals", returnTo: "/renewals?select=c-1" },
    });
    expect(groups[2].actions[0].target).toEqual({ kind: "ask", question: "Remind me before the Salesforce notice deadline", scopeContractId: "c-1" });
  });

  it("drops what cannot apply: nothing to remind about once the notice date passed, no email or quote on a closed renewal", () => {
    const passed = buildRenewalActionGroups(buildRenewalActionContext(row({ daysUntilCancellationDeadline: -2 })));
    expect(passed.find((group) => group.key === "soon")!.actions.map((action) => action.id)).toEqual(["send-notice", "export"]);

    const closedRow = { ...row(), tracked: saved("Completed") };
    const closed = buildRenewalActionGroups(buildRenewalActionContext(closedRow));
    expect(closed.find((group) => group.key === "ask")!.actions.map((action) => action.id)).toEqual(["plan", "market", "why-here"]);
    expect(closed.find((group) => group.key === "open")!.actions.map((action) => action.id)).not.toContain("quote-check");
  });

  it("never puts an id or a placeholder in a question: an unnamed supplier's renewal is 'this renewal' / 'this contract'", () => {
    const groups = buildRenewalActionGroups(buildRenewalActionContext(row({ supplierName: null })));
    expect(groups[0].actions.map((action) => (action.target.kind === "ask" ? action.target.question : null))).toEqual([
      "How should we approach this renewal?",
      "Draft the renewal negotiation email for this contract",
      "Are we paying above market on this contract?",
      "Why is this contract at the top of our renewals?",
    ]);
    expect(JSON.stringify(groups)).not.toContain("Supplier not resolved");
    expect(JSON.stringify(groups)).not.toContain("c-1 ");
  });

  it("switches a new capability on with one registry entry -- the pane renders whatever the registry lists", () => {
    const approval: RenewalActionDefinition = {
      id: "approval",
      group: "open",
      label: "Request approval",
      hint: "Send the plan to the budget owner",
      status: "available",
      target: ({ row: selected }) => ({ kind: "link", to: `/approvals/new?contract=${selected.item.contractId}` }),
    };
    const groups = buildRenewalActionGroups(buildRenewalActionContext(row()), [...RENEWAL_ACTIONS, approval]);
    const open = groups.find((group) => group.key === "open")!;
    expect(open.actions.at(-1)).toMatchObject({ id: "approval", target: { kind: "link", to: "/approvals/new?contract=c-1" } });
  });
});

describe("bulk actions", () => {
  it("assigns only the rows nobody acted on, and hides what does not apply", () => {
    const open = row({ contractId: "a", supplierName: "Alpha" });
    const negotiating = { ...row({ contractId: "b", supplierName: "Beta" }), tracked: saved("InProgress") };

    const both = buildRenewalBulkActions([open, negotiating]);
    expect(both.map((action) => [action.id, action.label])).toEqual([
      ["assign", "Assign 1 to me"],
      ["portfolio", "Show 2 in Portfolio"],
      ["ask-first", "Ask Raffa which to start first"],
    ]);
    expect(both[0].rows.map((selected) => selected.item.contractId)).toEqual(["a"]);
    expect(both[1].target).toEqual({ kind: "link", to: "/contracts?ids=a,b&from=renewals" });
    expect(both[2].target).toEqual({ kind: "ask", question: "Which of these renewals should we start first: Alpha, Beta?", scopeContractId: null });

    expect(buildRenewalBulkActions([negotiating]).map((action) => action.id)).toEqual(["portfolio"]);
    expect(buildRenewalBulkActions([])).toEqual([]);
  });
});
