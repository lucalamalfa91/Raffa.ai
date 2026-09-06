import type { WorkspaceRole } from "./navItems";

/**
 * Interim client-side role resolution (ADR-018 "Assumptions": "Entra ID will
 * return role/permission claims usable client-side to gate
 * /workspace/members (otherwise the non-admin 'request access' state must be
 * server-driven)"). No JWT bearer auth is wired into the API host yet
 * (backend/src/Contigo.Api/Program.cs has no `AddAuthentication`/
 * `AddJwtBearer`) and no endpoint returns "what is my role in this
 * workspace" — see src/routes/signin/workspaceStore.ts's own doc comment on
 * the identical gap for its `roleLabel` field. So there is exactly one
 * honest default today: whoever created/picked a workspace in this browser
 * is its Admin (backend's `WorkspaceFactory.CreateWorkspaceWithDefaultRoles`
 * seeds an Admin role for every new workspace; workspaceStore.ts's
 * `WorkspaceSummary.roleLabel` already encodes this same fact for the picker
 * list).
 *
 * A `?role=procurement` URL override exists so the guard this task builds
 * (RailNav hiding "Workspace & members", RequireRole's "Request access"
 * state) is genuinely reachable and demoable on `demo` — not just provable
 * in a unit test — until a real claim exists. The override is deliberately
 * narrow (two recognised values, session-scoped, persisted only so
 * in-app navigation doesn't drop it every click) so it can never be mistaken
 * for a real authorization claim. Once ADR-010's JWT/claim wiring lands,
 * only this function's body changes — RailNav/RequireRole/WorkspaceShellApp
 * all consume a plain `WorkspaceRole` value and do not know or care where it
 * came from (the same seam src/routes/signin/index.tsx's own header comment
 * describes for swapping its MSAL-state gate for a real `<Route>`).
 */
const ROLE_STORAGE_KEY = "contigo.shell.workspaceRole";

function parseRole(value: string | null): WorkspaceRole | null {
  return value === "admin" || value === "procurement" ? value : null;
}

export function resolveWorkspaceRole(
  location: Pick<Location, "search"> = window.location,
  storage: Storage = window.sessionStorage,
): WorkspaceRole {
  const fromQuery = parseRole(new URLSearchParams(location.search).get("role"));
  if (fromQuery) {
    storage.setItem(ROLE_STORAGE_KEY, fromQuery);
    return fromQuery;
  }

  return parseRole(storage.getItem(ROLE_STORAGE_KEY)) ?? "admin";
}
