import { beforeEach, describe, expect, it } from "vitest";
import { resolveWorkspaceRole } from "../../../src/components/shell/workspaceRole";

describe("resolveWorkspaceRole", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
  });

  it("defaults to admin with no override present", () => {
    expect(resolveWorkspaceRole({ search: "" })).toBe("admin");
  });

  it("honours an explicit ?role= override", () => {
    expect(resolveWorkspaceRole({ search: "?role=procurement" })).toBe("procurement");
  });

  it("persists the override so it survives a client-side navigation that drops the query string", () => {
    resolveWorkspaceRole({ search: "?role=procurement" });

    expect(resolveWorkspaceRole({ search: "" })).toBe("procurement");
  });

  it("ignores an unrecognised ?role= value and falls back to the stored/default role", () => {
    expect(resolveWorkspaceRole({ search: "?role=superadmin" })).toBe("admin");
  });
});
