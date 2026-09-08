import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import AppShell from "./AppShell";
import RequireRole from "./RequireRole";
import type { WorkspaceRole } from "./navItems";
import type { ApiClient } from "../../api/client";
import DocumentsRoute from "../../routes/documents";
import PortfolioRoute from "../../routes/contracts";
import Contract360Route from "../../routes/contracts/contract360";
import ReviewRoute from "../../routes/contracts/review";
import ReviewQueueRoute from "../../routes/review";
import RenewalsRoute from "../../routes/renewals";
import QuoteCheckRoute from "../../routes/quotes";
import AskRoute from "../../routes/ask";
import HomeRoute from "../../routes/home";
import MembersRoute from "../../routes/workspace/members";

export interface WorkspaceShellAppProps {
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
 * The route table this task introduces — src/routes/signin/index.tsx's own
 * header comment anticipated it: "No client-side router is wired into this
 * app yet ... introducing one is E06/F03/US02/T01's ('navigation-shell')
 * job, since it also owns the admin/procurement route guards a real router
 * enables". Every path matches ADR-018's locked "Route map" table, except
 * the two list landings flagged in navItems.ts (/review, /quotes) — both are
 * now real screens (ReviewQueueRoute, QuoteCheckRoute).
 *
 * Screens that belong to a later epic used to render ScaffoldScreen rather than
 * real content — this task's job was the shell/guards/ask-bar, not those screens.
 * `contracts/:contractId/review` is real as of task E07/F03/US01/T01 (ReviewRoute,
 * ADR-020 screen 6). The `review` landing path is real as of E06/F04 recovery
 * (ReviewQueueRoute). `workspace/members` is real as of E06/F04/US01/T01
 * (MembersRoute, ADR-020 screen 2). `renewals` is real as of task E08/F01/US01/T01
 * (RenewalsRoute, ADR-020 screen 8). `ask` is real as of task E07/F04/US01/T01
 * (AskRoute, ADR-020 screen 7). Quote check is real as of E08/F03/US01/T01.
 * Home (`index`) is real as of task E08/F02/US01/T01 (HomeRoute, ADR-020
 * screen 9).
 */
export function ShellRoutes({ workspaceName, role, userLabel, onSignOut, apiClient }: WorkspaceShellAppProps) {
  return (
    <Routes>
      <Route
        element={<AppShell workspaceName={workspaceName} role={role} userLabel={userLabel} onSignOut={onSignOut} />}
      >
        <Route index element={<HomeRoute apiClient={apiClient} />} />
        <Route path="contracts" element={<PortfolioRoute apiClient={apiClient} />} />
        <Route path="contracts/:contractId" element={<Contract360Route apiClient={apiClient} />} />
        <Route path="contracts/:contractId/review" element={<ReviewRoute apiClient={apiClient} />} />
        <Route path="review" element={<ReviewQueueRoute apiClient={apiClient} />} />
        <Route path="renewals" element={<RenewalsRoute apiClient={apiClient} userLabel={userLabel} />} />
        <Route path="ask" element={<AskRoute apiClient={apiClient} />} />
        <Route path="quotes" element={<QuoteCheckRoute apiClient={apiClient} />} />
        <Route path="quotes/:quoteId" element={<QuoteCheckRoute apiClient={apiClient} />} />
        <Route path="documents" element={<DocumentsRoute apiClient={apiClient} />} />
        <Route
          path="workspace/members"
          element={
            <RequireRole role={role} allow="admin">
              <MembersRoute apiClient={apiClient} userLabel={userLabel} />
            </RequireRole>
          }
        />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}

export default function WorkspaceShellApp(props: WorkspaceShellAppProps) {
  return (
    <BrowserRouter>
      <ShellRoutes {...props} />
    </BrowserRouter>
  );
}
