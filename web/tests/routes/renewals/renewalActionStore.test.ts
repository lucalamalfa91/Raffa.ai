import { beforeEach, describe, expect, it } from "vitest";
import {
  getTrackedRenewalAction,
  loadTrackedRenewalActions,
  rememberRenewalAction,
  type TrackedRenewalAction,
} from "../../../src/routes/renewals/renewalActionStore";

function trackedAction(overrides: Partial<TrackedRenewalAction> = {}): TrackedRenewalAction {
  return {
    contractId: "contract-1",
    supplierId: "supplier-1",
    annualSpend: 500_000,
    owner: "user@example.test",
    status: "InProgress",
    action: "In negotiation",
    updatedAt: "2026-09-06T08:00:00Z",
    ...overrides,
  };
}

describe("renewalActionStore (us-01-renewal-pipeline AC-3: session-scoped renewal-action mirror)", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
  });

  it("returns an empty list before anything has been remembered", () => {
    expect(loadTrackedRenewalActions()).toEqual([]);
  });

  it("persists a remembered action across loadTrackedRenewalActions calls", () => {
    rememberRenewalAction(trackedAction());

    expect(loadTrackedRenewalActions()).toEqual([trackedAction()]);
  });

  it("prepends new actions (most-recently-acted first)", () => {
    rememberRenewalAction(trackedAction({ contractId: "contract-1" }));
    rememberRenewalAction(trackedAction({ contractId: "contract-2" }));

    const loaded = loadTrackedRenewalActions();
    expect(loaded.map((tracked) => tracked.contractId)).toEqual(["contract-2", "contract-1"]);
  });

  it("updates (does not duplicate) an existing action by contractId, moving it to the front", () => {
    rememberRenewalAction(trackedAction({ contractId: "contract-1", action: "Assigned" }));
    rememberRenewalAction(trackedAction({ contractId: "contract-2" }));

    rememberRenewalAction(trackedAction({ contractId: "contract-1", action: "In negotiation" }));

    const loaded = loadTrackedRenewalActions();
    expect(loaded).toHaveLength(2);
    expect(loaded[0]).toEqual(trackedAction({ contractId: "contract-1", action: "In negotiation" }));
    expect(loaded[1].contractId).toBe("contract-2");
  });

  it("getTrackedRenewalAction finds this session's own action for one contract, or null", () => {
    rememberRenewalAction(trackedAction({ contractId: "contract-1" }));

    expect(getTrackedRenewalAction("contract-1")).toEqual(trackedAction({ contractId: "contract-1" }));
    expect(getTrackedRenewalAction("contract-does-not-exist")).toBeNull();
  });

  it("treats malformed sessionStorage content as an empty list rather than throwing", () => {
    window.sessionStorage.setItem("contigo.renewals.actions", "not json");

    expect(loadTrackedRenewalActions()).toEqual([]);
  });

  it("treats a non-array JSON payload under the key as an empty list", () => {
    window.sessionStorage.setItem("contigo.renewals.actions", JSON.stringify({ not: "an array" }));

    expect(loadTrackedRenewalActions()).toEqual([]);
  });

  it("accepts an explicit Storage instance instead of the window.sessionStorage default", () => {
    const customStorage = window.localStorage; // any Storage-shaped object works
    customStorage.clear();

    rememberRenewalAction(trackedAction(), customStorage);

    expect(loadTrackedRenewalActions(customStorage)).toEqual([trackedAction()]);
    expect(loadTrackedRenewalActions()).toEqual([]); // default sessionStorage untouched
    customStorage.clear();
  });
});
