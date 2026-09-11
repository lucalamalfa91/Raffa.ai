import { describe, expect, it } from "vitest";
import { parseWorkspaceRole, workspaceRoleLabel } from "./workspaceRole";

/**
 * Task E14/F03/US02/T01 (wave w14 "workspace is real"; ADR-012/ADR-019/ADR-025/ADR-026 w14 footers).
 * `parseWorkspaceRole` is the affordance-gating half (least-privilege default, never `"admin"`);
 * `workspaceRoleLabel` is the display half (pass-through, never degraded) -- two key spaces over one
 * vocabulary that must emit the identical two strings for the two modelled roles (ADR-019 w14 footer
 * clause 5) and must never be conflated: a wire value this app does not model loses every Admin
 * affordance but keeps its own name on screen.
 */
describe("parseWorkspaceRole", () => {
  it('maps the wire "Admin" to the modelled "admin"', () => {
    expect(parseWorkspaceRole("Admin")).toBe("admin");
  });

  it('maps the wire "Procurement" to the modelled "procurement"', () => {
    expect(parseWorkspaceRole("Procurement")).toBe("procurement");
  });

  it.each(["Legal", "Finance", "ReadOnly", "SomethingNewTheServerAdded", "", "admin", "ADMIN"])(
    'maps the unmodelled wire value %j to the least-privileged role, never "admin"',
    (wire) => {
      expect(parseWorkspaceRole(wire)).toBe("procurement");
    },
  );
});

describe("workspaceRoleLabel", () => {
  it('renders the wire "Admin" as "Workspace Admin"', () => {
    expect(workspaceRoleLabel("Admin")).toBe("Workspace Admin");
  });

  it('renders the wire "Procurement" as "Procurement"', () => {
    expect(workspaceRoleLabel("Procurement")).toBe("Procurement");
  });

  it.each(["Legal", "Finance", "ReadOnly", "SomethingNewTheServerAdded"])(
    "passes an unmodelled wire value %j straight through, never substituting a modelled label",
    (wire) => {
      expect(workspaceRoleLabel(wire)).toBe(wire);
    },
  );
});
