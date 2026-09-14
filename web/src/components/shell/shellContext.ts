import { useOutletContext } from "react-router-dom";
import type { DocumentCountsBody } from "./useDocumentCounts";

/**
 * What the app shell knows once, for every screen under it (ADR-024 V2 IA; R-WEB-02): whether the
 * "From your contracts" tier is lit (`kbReady`, at least one validated contract) and how many
 * validated contracts there are. `AppShell.tsx` fetches this through `useValidatedContractCount`
 * and hands it down the router `<Outlet context>`, so a screen that needs the same signal (the
 * Workspace & members tip, for instance) reads the shell's answer instead of re-fetching the
 * portfolio for a second, possibly different, verdict.
 */
export interface ShellOutletContext {
  /**
   * Task E14/F03/US02/T01 (wave w14): the id of the one workspace `App.tsx`'s resolution already
   * settled on, so a routed screen that needs the tenant id can read it from here instead of
   * re-deriving it from `routes/signin/workspaceStore.ts`'s session hint on every screen.
   * `AppShell.tsx` always supplies it; optional here (not `string`) only so a caller that builds
   * this context by hand -- today, `E15/F02/US01/T01`'s own `MembersRoute.test.tsx`, a same-phase
   * sibling this task's file scope forbids touching -- keeps type-checking against a literal that
   * predates this field, the same "widen, don't break a sibling's pre-existing literal" posture
   * `WorkspaceShellApp.tsx`'s own `membersRouteProps` comment documents for the identical seam.
   */
  workspaceId?: string;
  kbReady: boolean;
  validatedContractCount: number;
  /**
   * Task E16/F03/US01/T01 (wave w15): the server's tenant-wide document `counts` the rail badge
   * renders (`useDocumentCounts`), fetched once by the shell at mount; `null` until known. Optional
   * for the same "widen, don't break a sibling's hand-built literal" reason as `workspaceId`.
   */
  documentCounts?: DocumentCountsBody | null;
}

/**
 * The shell context for the current screen, or `null` when the screen is rendered outside the
 * shell (unit tests mount routes directly) -- a caller treats `null` as "unknown" and falls back to
 * whatever it can derive from its own data, never as "not ready".
 */
export function useShellContext(): ShellOutletContext | null {
  const context = useOutletContext<ShellOutletContext | undefined>();
  return context ?? null;
}
