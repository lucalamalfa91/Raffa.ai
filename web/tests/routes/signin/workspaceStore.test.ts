import { beforeEach, describe, expect, it } from "vitest";
import { clearCurrentWorkspace, loadCurrentWorkspace, selectCurrentWorkspace } from "../../../src/routes/signin/workspaceStore";

// Task E14/F03/US02/T01 (wave w14 "workspace is real"): the per-account localStorage cache
// (loadKnownWorkspaces/rememberWorkspace, the "raffa.signin.knownWorkspaces.*" prefix) is deleted --
// see workspaceStore.ts's own header comment for why (GET /api/workspaces, ADR-026 D1, made it
// redundant and, worse, wrong: a browser that had not itself created a workspace could never answer
// "which workspaces am I a member of"). What survives below is the *session hint* only --
// `raffa.signin.currentWorkspace` -- demoted from source of truth to a value every reader must
// revalidate against the server list before trusting (see App.tsx's AuthenticatedGate /
// resolveWorkspaceSelection for that revalidation, covered by
// tests/routes/signin/WorkspacePickerScreen.test.tsx's own "resolution order" describe block, and
// tests/components/shell/workspaceRole.test.ts's sibling in src/ for the analogous role-side rule).

const acme = { id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" };

describe("workspaceStore", () => {
  beforeEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
  });

  describe("current workspace (sessionStorage session hint, not a source of truth)", () => {
    it("is null when nothing has been selected", () => {
      expect(loadCurrentWorkspace()).toBeNull();
    });

    it("round-trips a selected workspace", () => {
      selectCurrentWorkspace(acme);

      expect(loadCurrentWorkspace()).toEqual(acme);
    });

    it("clears the selection", () => {
      selectCurrentWorkspace(acme);
      clearCurrentWorkspace();

      expect(loadCurrentWorkspace()).toBeNull();
    });

    it("treats malformed stored JSON as no selection instead of throwing", () => {
      window.sessionStorage.setItem("raffa.signin.currentWorkspace", "{not valid json");

      expect(loadCurrentWorkspace()).toBeNull();
    });

    it("treats a stored non-object value as no selection instead of throwing", () => {
      window.sessionStorage.setItem("raffa.signin.currentWorkspace", JSON.stringify("just a string"));

      expect(loadCurrentWorkspace()).toBeNull();
    });

    it("is session-scoped, not account-scoped -- there is no per-account key at all any more", () => {
      selectCurrentWorkspace(acme);

      expect(window.localStorage.length).toBe(0);
    });
  });
});
