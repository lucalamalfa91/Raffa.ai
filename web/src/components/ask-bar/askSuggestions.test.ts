import { describe, expect, it } from "vitest";
import type { CapabilityBody } from "../../api/client";
import { getAskBarCopy, suggestionsFromCapabilityCatalog } from "./askSuggestions";

/**
 * Task E25/F01/US01/T01 (ask-chip-role-gate; ADR-022 S16-11, ADR-012 w17 cl 40): a catalog entry
 * whose own `roleGate` is not `"any"` -- today, only `workspace-members`
 * (`backend/src/Raffa.Chat/Application/Capabilities/CapabilityCatalog.cs`) -- must never surface
 * its `exampleQuestions` as suggestion chips to a non-Admin; an Admin still sees them. This is
 * presentation only (AC-2): `GET /api/capabilities` itself returns the identical, whole catalog to
 * both roles -- these tests never touch that endpoint, only the client-side chip selection that
 * reads its `roleGate` field.
 */
function workspaceMembersCapability(overrides: Partial<CapabilityBody> = {}): CapabilityBody {
  return {
    key: "workspace-members",
    title: "Workspace & members",
    routePattern: "/workspace/members",
    description: "Invite teammates, assign their role and manage the workspace member list.",
    exampleQuestions: ["How do I invite a teammate?", "Who can upload documents?"],
    roleGate: "admin",
    availability: "admin",
    howTo: [],
    ...overrides,
  };
}

describe("suggestionsFromCapabilityCatalog (role gate)", () => {
  it("drops an admin-gated entry's chips for a non-Admin (AC-1)", () => {
    expect(
      suggestionsFromCapabilityCatalog([workspaceMembersCapability()], "workspace-members", "procurement"),
    ).toBeNull();
  });

  it("keeps an admin-gated entry's chips for an Admin (AC-1)", () => {
    expect(suggestionsFromCapabilityCatalog([workspaceMembersCapability()], "workspace-members", "admin")).toEqual([
      "How do I invite a teammate?",
      "Who can upload documents?",
    ]);
  });

  it("never drops a roleGate: \"any\" entry's chips, for either role", () => {
    const catalog = [
      workspaceMembersCapability({ key: "documents", roleGate: "any", exampleQuestions: ["a?", "b?"] }),
    ];
    expect(suggestionsFromCapabilityCatalog(catalog, "documents", "procurement")).toEqual(["a?", "b?"]);
    expect(suggestionsFromCapabilityCatalog(catalog, "documents", "admin")).toEqual(["a?", "b?"]);
  });

  it("stays null while the catalog has not loaded yet, independent of role", () => {
    expect(suggestionsFromCapabilityCatalog(null, "workspace-members", "procurement")).toBeNull();
    expect(suggestionsFromCapabilityCatalog(null, "workspace-members", "admin")).toBeNull();
  });
});

describe("getAskBarCopy (role gate integration, workspace screen)", () => {
  it("falls back to the static chip pair for a non-Admin when the screen's own catalog entry is admin-gated", () => {
    const copy = getAskBarCopy("/workspace/members", true, [workspaceMembersCapability()], "procurement");
    expect(copy.suggestions).toEqual(["When does Salesforce expire?", "What liabilities do we have?"]);
  });

  it("uses the catalog's own chips for an Admin on the same screen", () => {
    const copy = getAskBarCopy("/workspace/members", true, [workspaceMembersCapability()], "admin");
    expect(copy.suggestions).toEqual(["How do I invite a teammate?", "Who can upload documents?"]);
  });

  it("reads the identical catalog array for both roles -- AC-2, the GET /api/capabilities body itself is never filtered, only which chips this client renders", () => {
    // The same `catalog` reference (what a single, un-gated GET /api/capabilities response would
    // deserialize to) feeds both calls below -- nothing here trims, clones-minus-one, or otherwise
    // mutates it per role. Only `copy.suggestions` (a client-side presentation decision) differs.
    const catalog = [workspaceMembersCapability()];

    const procurementCopy = getAskBarCopy("/workspace/members", true, catalog, "procurement");
    const adminCopy = getAskBarCopy("/workspace/members", true, catalog, "admin");

    expect(catalog).toHaveLength(1);
    expect(catalog[0].roleGate).toBe("admin");
    expect(procurementCopy.suggestions).not.toEqual(adminCopy.suggestions);
  });
});
