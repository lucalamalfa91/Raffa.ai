import { describe, expect, it } from "vitest";
import type { CapabilityBody } from "../../api/client";
import { contractIdForPath, getAskBarCopy, suggestionsFromCapabilityCatalog } from "./askSuggestions";

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

/**
 * Task E27/F03/US01/T01 (bar-scope; parent story us-01-bar-scope AC-1; closes NW-77, ADR-012 cl. 49
 * per `reports/architecture/waves/w19.md`). `contractIdForPath` is the one predicate
 * `GlobalAskBar.tsx#submit` reads to decide whether to navigate `/ask` or `/ask?scope=<id>` -- it
 * has to agree exactly with `screenForPath`'s own c360 test (documents/portfolio/review all stay
 * unscoped) since AC-3 depends on that same boundary.
 */
describe("contractIdForPath (AC-1)", () => {
  const CONTRACT_ID = "11111111-1111-1111-1111-111111111111";

  it("reads the :contractId segment on Contract 360", () => {
    expect(contractIdForPath(`/contracts/${CONTRACT_ID}`)).toBe(CONTRACT_ID);
  });

  it("stays null on Portfolio (/contracts, no id) -- AC-3", () => {
    expect(contractIdForPath("/contracts")).toBeNull();
  });

  it("stays null on the review sub-route -- never scoped from there", () => {
    expect(contractIdForPath(`/contracts/${CONTRACT_ID}/review`)).toBeNull();
  });

  it("stays null on Ask-home and every other screen -- AC-3", () => {
    expect(contractIdForPath("/ask")).toBeNull();
    expect(contractIdForPath("/documents")).toBeNull();
    expect(contractIdForPath("/renewals")).toBeNull();
  });
});

/**
 * Task E27/F03/US01/T01 (bar-scope; parent story us-01-bar-scope AC-2/AC-3; closes NW-77). The
 * notice chip's supplier name, sourced the same way `routes/ask/askViewModel.ts
 * #buildScopedSuggestions` already sources it for the Ask screen's own scoped chips
 * (`GlobalAskBar.tsx`'s own `getContract360` read, see that file's header comment) -- `capabilities`
 * is always `null` below since the catalog has no c360 key (`CAPABILITY_KEY_BY_SCREEN.c360`) and so
 * is never consulted for this screen.
 */
describe("getAskBarCopy -- c360 notice chip names the real supplier (AC-2/AC-3)", () => {
  const C360_PATH = "/contracts/11111111-1111-1111-1111-111111111111";

  it("AC-2: names the real supplier in both notice chips once resolved", () => {
    const copy = getAskBarCopy(C360_PATH, true, null, "procurement", "Acme Corp");
    expect(copy.suggestions).toEqual([
      "When must we give notice to Acme Corp?",
      "What is our liability cap with Acme Corp?",
    ]);
  });

  it('falls back to "this supplier" while the supplier name is unresolved (null)', () => {
    const copy = getAskBarCopy(C360_PATH, true, null, "procurement", null);
    expect(copy.suggestions).toEqual([
      "When must we give notice to this supplier?",
      "What is our liability cap with this supplier?",
    ]);
  });

  it('falls back to "this supplier" when the caller omits supplierName entirely', () => {
    const copy = getAskBarCopy(C360_PATH, true, null, "procurement");
    expect(copy.suggestions).toEqual([
      "When must we give notice to this supplier?",
      "What is our liability cap with this supplier?",
    ]);
  });

  it('falls back to "this supplier" for a blank, whitespace-only name', () => {
    const copy = getAskBarCopy(C360_PATH, true, null, "procurement", "   ");
    expect(copy.suggestions).toEqual([
      "When must we give notice to this supplier?",
      "What is our liability cap with this supplier?",
    ]);
  });

  it("AC-3: a supplier name passed on Portfolio is ignored -- chips stay the static, unscoped pair", () => {
    const copy = getAskBarCopy("/contracts", true, null, "procurement", "Acme Corp");
    expect(copy.suggestions).toEqual([
      "Which of these have uncapped liability?",
      "Which contracts renew in the next 120 days?",
    ]);
  });

  it("AC-3: a supplier name passed on Ask-home is ignored -- chips stay the static, unscoped pair", () => {
    const copy = getAskBarCopy("/ask", true, null, "procurement", "Acme Corp");
    expect(copy.suggestions).toEqual(["When does Salesforce expire?", "What liabilities do we have?"]);
  });
});
