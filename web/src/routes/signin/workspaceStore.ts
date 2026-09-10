// Client-side record of "workspaces this browser has created for this
// signed-in account" (ADR-018 route `/signin`; story us-01-signin-workspace-
// picker, AC-1 "workspace list").
//
// Why this is client-side, not server-queried: there is no backend endpoint
// that lists the workspaces a signed-in identity belongs to.
//   - backend/src/Raffa.Api/WorkspaceEndpointExtensions.cs maps only
//     `POST /api/workspaces` (create) and `POST /api/workspaces/{tenantId}
//     /invites` (invite) -- no `GET`.
//   - backend/src/Raffa.Identity.Workspace/Infrastructure
//     /WorkspaceProvisioningService.cs's CreateWorkspaceAsync does not create
//     a membership for the caller: there is no caller identity at creation
//     time (ADR-010's JWT/claims wiring is not yet in force -- "Workspace
//     creation is the pre-authentication signup step", that service's own
//     doc comment).
//   - backend/src/Raffa.Identity.Workspace/Infrastructure
//     /WorkspaceMembershipService.cs has InviteAsync/LinkSignInAsync but no
//     "list workspaces for this user" query.
// A future backend task would need to add a `GET` endpoint (e.g. keyed off
// the OIDC subject once ADR-010 lands) plus a WorkspaceMembershipService
// query before this module can be replaced with a real server call.
//
// Until then, this is the honest interim: every entry here is a *real*
// workspace this browser really created via the real `POST /api/workspaces`
// (see src/api/client.ts's createWorkspace) -- never fabricated data. The
// known limitation is discovery, not truth: this browser cannot learn about
// a workspace created on another device, or one this account was invited
// into without visiting this browser first. Keyed by the signed-in MSAL
// account's `homeAccountId` so two different Entra accounts on the same
// browser never see each other's list.

const KNOWN_WORKSPACES_KEY_PREFIX = "raffa.signin.knownWorkspaces.";
const CURRENT_WORKSPACE_KEY = "raffa.signin.currentWorkspace";

export interface WorkspaceSummary {
  id: string;
  name: string;
  createdAt: string;
  /**
   * Always 0 from this screen: it never calls the portfolio API
   * (`GET /api/contracts` -- task E07/F01/US01/T01 owns that). A workspace
   * this screen just learned about from a `POST /api/workspaces` response
   * has ingested nothing yet, so 0 is the true count, not a placeholder.
   */
  contractCount: number;
  /**
   * Not modelled by the backend yet -- `WorkspaceTenant`
   * (backend/src/Raffa.Identity.Workspace/Domain/WorkspaceTenant.cs)
   * carries only `Name`/`CreatedAt`, no currency or region column. Left
   * `undefined` rather than invented; render only when present. (The
   * prototype's own fresh/sandbox workspace row omits it too -- only a
   * populated demo workspace shows one -- see
   * inputs/design/prototypes/day1-demo.html.)
   */
  currencyRegion?: string;
  /**
   * Client-side inference, not a server-issued claim: ADR-010's role claims
   * are not wired yet, so there is no way to ask the server "what is my role
   * in this workspace". Whoever's browser created the workspace is treated
   * as its Admin here, matching WorkspaceFactory
   * .CreateWorkspaceWithDefaultRoles seeding an Admin role for every new
   * workspace.
   */
  roleLabel: "Workspace Admin";
}

export interface CurrentWorkspace {
  id: string;
  name: string;
}

function readWorkspaceArray(storage: Storage, key: string): WorkspaceSummary[] {
  const raw = storage.getItem(key);
  if (!raw) return [];
  try {
    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed) ? (parsed as WorkspaceSummary[]) : [];
  } catch {
    // Malformed/foreign localStorage content under this key is not this
    // screen's problem to throw over -- treat it the same as "nothing known
    // yet" (AppConfig's "fail loud" convention applies to the *required*
    // runtime config; this cache is best-effort by design).
    return [];
  }
}

/** All workspaces this browser has created for `accountKey` (MSAL `homeAccountId`), oldest first. */
export function loadKnownWorkspaces(
  accountKey: string,
  storage: Storage = window.localStorage,
): WorkspaceSummary[] {
  return readWorkspaceArray(storage, KNOWN_WORKSPACES_KEY_PREFIX + accountKey);
}

/** Appends `workspace` to `accountKey`'s known list (no-op if its id is already present) and returns the resulting list. */
export function rememberWorkspace(
  accountKey: string,
  workspace: WorkspaceSummary,
  storage: Storage = window.localStorage,
): WorkspaceSummary[] {
  const key = KNOWN_WORKSPACES_KEY_PREFIX + accountKey;
  const existing = readWorkspaceArray(storage, key);
  const next = existing.some((known) => known.id === workspace.id) ? existing : [...existing, workspace];
  storage.setItem(key, JSON.stringify(next));
  return next;
}

/**
 * The workspace the user picked this session -- future screens (the nav
 * shell, portfolio, ...) read this to know which tenant to send as the
 * `X-Tenant-Id` header every other backend endpoint requires today (no
 * screen outside this task's scope consumes it yet). Session-scoped
 * (`sessionStorage`), not account-scoped: switching tabs/reopening the
 * browser should not silently resume a previous tenant without picking it
 * again.
 */
export function loadCurrentWorkspace(storage: Storage = window.sessionStorage): CurrentWorkspace | null {
  const raw = storage.getItem(CURRENT_WORKSPACE_KEY);
  if (!raw) return null;
  try {
    const parsed: unknown = JSON.parse(raw);
    if (
      parsed !== null &&
      typeof parsed === "object" &&
      typeof (parsed as CurrentWorkspace).id === "string" &&
      typeof (parsed as CurrentWorkspace).name === "string"
    ) {
      return parsed as CurrentWorkspace;
    }
    return null;
  } catch {
    return null;
  }
}

export function selectCurrentWorkspace(
  workspace: CurrentWorkspace,
  storage: Storage = window.sessionStorage,
): void {
  storage.setItem(CURRENT_WORKSPACE_KEY, JSON.stringify(workspace));
}

export function clearCurrentWorkspace(storage: Storage = window.sessionStorage): void {
  storage.removeItem(CURRENT_WORKSPACE_KEY);
}
