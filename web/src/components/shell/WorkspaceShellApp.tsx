import { Navigate, Route, Routes } from "react-router-dom";
import AppShell from "./AppShell";
import type { WorkspaceRole } from "./navItems";
import type { ApiClient } from "../../api/client";
import DocumentsRoute from "../../routes/documents";
import DocumentViewerRoute from "../../routes/documents/viewer";
import PortfolioRoute from "../../routes/contracts";
import Contract360Route from "../../routes/contracts/contract360";
import ReviewRoute from "../../routes/contracts/review";
import RenewalsRoute from "../../routes/renewals";
import QuoteCheckRoute from "../../routes/quotes";
import AskRoute from "../../routes/ask";
import SavingsRoute from "../../routes/savings";
import MembersRoute from "../../routes/workspace/members";

export interface WorkspaceShellAppProps {
  /**
   * Task E14/F03/US02/T01 (wave w14 "workspace is real"): the id half of the one workspace
   * `App.tsx`'s resolution already settled on -- threaded down so a route that needs the tenant id
   * (today: `workspace/members`) reads it as a prop instead of re-deriving it from the session hint
   * `routes/signin/workspaceStore.ts` now demotes to a revalidated-once-per-mount value, not a
   * per-screen source of truth.
   */
  workspaceId: string;
  workspaceName: string;
  role: WorkspaceRole;
  userLabel: string;
  onSignOut: () => void;
  /**
   * Task E06/F05/US01/T01 (document-upload): threaded through from
   * App.tsx so the `documents` route below can call the real
   * `POST /api/documents` -- the same generated-client instance every other
   * screen shares, not a second one constructed here.
   */
  apiClient: ApiClient;
}

/**
 * V2 route table (ADR-024 amendment to ADR-018's locked "Route map"; `ia-v2.md` "Route map (V2)"):
 * `/` -> `/ask` (R-WEB-01, sign-in and every stale bookmark lands on the new home); `ask` /
 * `ask/:conversationId` -> `AskRoute`; `savings` -> `SavingsRoute` (not a rail item -- reached from
 * Ask actions, Renewals and Contract 360); `review` redirects to `/documents?filter=attention`
 * (Review is a *state* of Documents in V2, not a rail destination or its own screen -- the old
 * `routes/review/` rail landing has been deleted). `contracts`, `contracts/:contractId`,
 * `renewals`, `quotes`, `quotes/:quoteId` and `workspace/members` all render their V2 screens
 * (`raffa-v2/screens-v2.md` #5-#10); `userLabel` reaches Renewals and Contract 360 as the owner
 * of every renewal action they post.
 *
 * Task E14/F03/US02/T01 (wave w14): `workspace/members` no longer wraps itself in `RequireRole` --
 * the server role, not a client-side gate, decides what that screen renders for a non-Admin caller
 * now that NW-04 ships a real roster endpoint (ADR-018 w14 design footer: "Procurement sees the
 * roster, read-only", buildable for the first time this wave). `RequireRole.tsx` is untouched by
 * this task; removing its wrap here is the whole of this task's half of that change.
 *
 * **Interface contract with `E15/F02/US01/T01`** (which owns `routes/workspace/members/**` and
 * nothing else): `membersRouteProps` below spreads `workspaceId` and `role` onto `MembersRoute` in
 * addition to its already-declared `apiClient`/`userLabel`. Spread rather than written as direct
 * JSX attributes on purpose -- a same-phase sibling branch is what actually widens
 * `MembersRouteProps` to declare them; spreading a separately-typed object is the one shape that
 * type-checks against *either* the pre-merge or the post-merge signature (TypeScript's excess-
 * property check only fires on a fresh literal assigned straight into a narrower target, not on a
 * named variable spread into one), so this task's own build stays green before that merge and the
 * two extra props start being consumed, unchanged, the moment it lands.
 */
export function ShellRoutes({ workspaceId, workspaceName, role, userLabel, onSignOut, apiClient }: WorkspaceShellAppProps) {
  const membersRouteProps = { apiClient, userLabel, workspaceId, role };

  return (
    <Routes>
      <Route
        element={
          <AppShell
            workspaceId={workspaceId}
            workspaceName={workspaceName}
            role={role}
            userLabel={userLabel}
            onSignOut={onSignOut}
            apiClient={apiClient}
          />
        }
      >
        <Route index element={<Navigate to="/ask" replace />} />
        <Route path="ask" element={<AskRoute apiClient={apiClient} />} />
        <Route path="ask/:conversationId" element={<AskRoute apiClient={apiClient} />} />
        <Route path="savings" element={<SavingsRoute apiClient={apiClient} />} />
        <Route path="contracts" element={<PortfolioRoute apiClient={apiClient} />} />
        <Route path="contracts/:contractId" element={<Contract360Route apiClient={apiClient} userLabel={userLabel} />} />
        <Route path="contracts/:contractId/review" element={<ReviewRoute apiClient={apiClient} />} />
        <Route path="renewals" element={<RenewalsRoute apiClient={apiClient} userLabel={userLabel} />} />
        <Route path="quotes" element={<QuoteCheckRoute apiClient={apiClient} />} />
        <Route path="quotes/:quoteId" element={<QuoteCheckRoute apiClient={apiClient} />} />
        <Route path="documents" element={<DocumentsRoute apiClient={apiClient} role={role} />} />
        <Route path="review" element={<Navigate to="/documents?filter=attention" replace />} />
        <Route path="workspace/members" element={<MembersRoute {...membersRouteProps} />} />
        <Route path="documents/:documentId/viewer" element={<DocumentViewerRoute apiClient={apiClient} />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}

/**
 * Task E14/F03/US02/T01 (wave w14): `BrowserRouter` no longer mounts here. Every route used to
 * live only once `App.tsx`'s own `account && workspace` gate passed, so a `BrowserRouter` at this
 * level was the only router the signed-in app ever had. `/invite/accept` (ADR-018 w14 footer) is
 * reachable signed out and with no workspace -- a state this component never renders for at all --
 * so the router had to move up to `App.tsx`, which now mounts one `BrowserRouter` spanning both
 * branches. `ShellRoutes` was already exported separately as a testing seam; that seam is what made
 * this a supported change rather than a rewrite, and it is also why this component is now a thin,
 * router-free wrapper rather than deleted outright -- `App.tsx` still imports it by this name.
 */
export default function WorkspaceShellApp(props: WorkspaceShellAppProps) {
  return <ShellRoutes {...props} />;
}
