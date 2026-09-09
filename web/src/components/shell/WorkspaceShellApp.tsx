import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import AppShell from "./AppShell";
import RequireRole from "./RequireRole";
import type { WorkspaceRole } from "./navItems";
import type { ApiClient } from "../../api/client";
import DocumentsRoute from "../../routes/documents";
import PortfolioRoute from "../../routes/contracts";
import Contract360Route from "../../routes/contracts/contract360";
import ReviewRoute from "../../routes/contracts/review";
import RenewalsRoute from "../../routes/renewals";
import QuoteCheckRoute from "../../routes/quotes";
import AskRoute from "../../routes/ask";
import SavingsRoute from "../../routes/savings";
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
 * V2 route table (ADR-024 amendment to ADR-018's locked "Route map"; `ia-v2.md` "Route map (V2)"):
 * `/` -> `/ask` (R-WEB-01, sign-in and every stale bookmark lands on the new home); `ask` /
 * `ask/:conversationId` -> `AskRoute`; `savings` -> `SavingsRoute` (not a rail item -- reached from
 * Ask actions, Renewals and Contract 360); `review` redirects to `/documents?filter=attention`
 * (Review is a *state* of Documents in V2, not a rail destination or its own screen -- the old
 * `routes/review/` rail landing has been deleted). `contracts`, `contracts/:contractId`,
 * `renewals`, `quotes`, `quotes/:quoteId` and `workspace/members` all render their V2 screens
 * (`contigo-v2/screens-v2.md` #5-#10); `userLabel` reaches Renewals and Contract 360 as the owner
 * of every renewal action they post.
 */
export function ShellRoutes({ workspaceName, role, userLabel, onSignOut, apiClient }: WorkspaceShellAppProps) {
  return (
    <Routes>
      <Route
        element={
          <AppShell
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
        <Route path="documents" element={<DocumentsRoute apiClient={apiClient} />} />
        <Route path="review" element={<Navigate to="/documents?filter=attention" replace />} />
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
