import { beforeEach, describe, expect, it } from "vitest";
import {
  clearCurrentWorkspace,
  loadCurrentWorkspace,
  loadKnownWorkspaces,
  rememberWorkspace,
  selectCurrentWorkspace,
  type WorkspaceSummary,
} from "../../../src/routes/signin/workspaceStore";

const acme: WorkspaceSummary = {
  id: "11111111-1111-1111-1111-111111111111",
  name: "Acme Procurement",
  createdAt: "2026-09-06T08:00:00Z",
  contractCount: 0,
  roleLabel: "Workspace Admin",
};

const globex: WorkspaceSummary = {
  id: "22222222-2222-2222-2222-222222222222",
  name: "Globex Sandbox",
  createdAt: "2026-09-06T09:00:00Z",
  contractCount: 0,
  roleLabel: "Workspace Admin",
};

describe("workspaceStore", () => {
  beforeEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
  });

  describe("known workspaces (localStorage, per account)", () => {
    it("returns an empty list for an account nothing has been remembered for", () => {
      expect(loadKnownWorkspaces("account-1")).toEqual([]);
    });

    it("remembers a workspace and reads it back", () => {
      const result = rememberWorkspace("account-1", acme);

      expect(result).toEqual([acme]);
      expect(loadKnownWorkspaces("account-1")).toEqual([acme]);
    });

    it("appends a second workspace rather than overwriting the first", () => {
      rememberWorkspace("account-1", acme);
      const result = rememberWorkspace("account-1", globex);

      expect(result).toEqual([acme, globex]);
    });

    it("is idempotent for the same workspace id (no duplicate row)", () => {
      rememberWorkspace("account-1", acme);
      const result = rememberWorkspace("account-1", { ...acme, name: "Renamed elsewhere" });

      expect(result).toEqual([acme]);
    });

    it("keeps two different accounts' lists isolated", () => {
      rememberWorkspace("account-1", acme);
      rememberWorkspace("account-2", globex);

      expect(loadKnownWorkspaces("account-1")).toEqual([acme]);
      expect(loadKnownWorkspaces("account-2")).toEqual([globex]);
    });

    it("treats malformed stored JSON as an empty list instead of throwing", () => {
      window.localStorage.setItem("raffa.signin.knownWorkspaces.account-1", "{not valid json");

      expect(loadKnownWorkspaces("account-1")).toEqual([]);
    });

    it("treats a stored non-array value as an empty list instead of throwing", () => {
      window.localStorage.setItem("raffa.signin.knownWorkspaces.account-1", JSON.stringify({ not: "an array" }));

      expect(loadKnownWorkspaces("account-1")).toEqual([]);
    });
  });

  describe("current workspace (sessionStorage)", () => {
    it("is null when nothing has been selected", () => {
      expect(loadCurrentWorkspace()).toBeNull();
    });

    it("round-trips a selected workspace", () => {
      selectCurrentWorkspace({ id: acme.id, name: acme.name });

      expect(loadCurrentWorkspace()).toEqual({ id: acme.id, name: acme.name });
    });

    it("clears the selection", () => {
      selectCurrentWorkspace({ id: acme.id, name: acme.name });
      clearCurrentWorkspace();

      expect(loadCurrentWorkspace()).toBeNull();
    });

    it("treats malformed stored JSON as no selection instead of throwing", () => {
      window.sessionStorage.setItem("raffa.signin.currentWorkspace", "{not valid json");

      expect(loadCurrentWorkspace()).toBeNull();
    });
  });
});
