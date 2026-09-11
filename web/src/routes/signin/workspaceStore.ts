// Session hint for "which workspace am I in" -- ADR-012 w14 footer clause 1/2
// (task E14/F03/US02/T01, wave w14 "workspace is real"): a client store never
// stands in for a missing GET, and this file used to be exactly that. Three
// things died together when `GET /api/workspaces` landed (ADR-026 §D1):
//
//   - The per-account "workspaces this browser has *created*" `localStorage`
//     array and its two reader/writer functions -- that was never the same
//     question as "workspaces this identity *belongs to*", and a browser that
//     had not created anything itself (a fresh context, or a different
//     device) could never answer it. `WorkspacePickerScreen.tsx` now reads
//     `apiClient.listWorkspaces()` directly.
//   - The old per-row summary type's two invented fields: a contract count
//     that was always zero from this screen (it never called the portfolio
//     API) and a role display string inferred client-side -- "whoever's
//     browser created it is Admin" -- typed as one fixed string literal, so
//     the type system itself asserted every workspace you could see was one
//     you administered. Both are server fields on that same response now
//     (`WorkspaceSummaryBody` in src/api/client.ts) and the type this file
//     invented for them is gone.
//
// What survives, below, is the one thing that was never the source of truth
// even before this wave: the *session hint*. `raffa.signin.currentWorkspace`
// keeps its name and its three functions' exact signatures, but nothing
// trusts the value it holds until `WorkspacePickerScreen.tsx`'s resolution
// logic has checked it against the server's own list on this mount --
// **`hint ∉ list ⇒ discard the hint`**, the client half of NW-58's removal
// requirement. That check needs no endpoint, no polling and no cache
// invalidation of its own: revalidation against a real GET *is* the
// mechanism. Session-scoped (`sessionStorage`), not account-scoped:
// switching tabs/reopening the browser should not silently resume a previous
// tenant without it being re-confirmed against the caller's real membership.

const CURRENT_WORKSPACE_KEY = "raffa.signin.currentWorkspace";

export interface CurrentWorkspace {
  id: string;
  name: string;
}

/**
 * The workspace the caller last entered, *as a hint only* -- see this file's
 * header comment. Every reader of this value must revalidate it (or accept
 * that whatever mounted it already did) before treating it as fact.
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
