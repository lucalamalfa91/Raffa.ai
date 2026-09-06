import { describe, expect, it } from "vitest";
import { getVisibleNavItems, NAV_ITEMS } from "../../../src/components/shell/navItems";

describe("navItems", () => {
  it("lists all eight rail items, in AC-1 order, for the Admin role", () => {
    const labels = getVisibleNavItems("admin").map((item) => item.label);
    expect(labels).toEqual([
      "Home",
      "Portfolio",
      "Renewals",
      "Ask (⌘K)",
      "Quote check",
      "Documents",
      "Review queue",
      "Workspace & members",
    ]);
  });

  it("hides Workspace & members for Procurement (AC-2 — the task's required unit test)", () => {
    const labels = getVisibleNavItems("procurement").map((item) => item.label);
    expect(labels).not.toContain("Workspace & members");
    expect(labels).toHaveLength(NAV_ITEMS.length - 1);
  });

  it("keeps every other item visible to Procurement (roles are a permission gate, never an IA fork)", () => {
    const labels = getVisibleNavItems("procurement").map((item) => item.label);
    expect(labels).toEqual([
      "Home",
      "Portfolio",
      "Renewals",
      "Ask (⌘K)",
      "Quote check",
      "Documents",
      "Review queue",
    ]);
  });
});
