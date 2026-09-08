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
 * V2 route table (ADR-024 amendment to ADR-018's locked "Route map"; task E13/F09/US01/T01, gap
 * G-IA-V2). `ia-v2.md` "Route map (V2)" is the source: `/` -> `/ask` (R-WEB-01, sign-in and every
 * stale bookmark lands on the new home); `ask` / `ask/:conversationId` -> `AskRoute` (the
 * `:conversationId` param is not read yet -- `routes/ask/**` is this task's own "do not touch"
 * boundary; F09/T04 wires resume); `savings` -> `SavingsRoute` (renamed from the old index/Home
 * route, "keep behaviour" -- see `routes/savings/index.tsx`'s own header comment); `review` now
 * redirects to `/documents?filter=attention` (Review is a *state* of Documents in V2, not a rail
 * destination or its own screen -- `routes/review/` itself is untouched/unrouted dead code until a
 * cleanup task removes it, out of this task's own file scope). Every other route
 * (`documents`, `contracts`, `contracts/:contractId`, `contracts/:contractId/review`, `renewals`,
 * `quotes`, `quotes/:quoteId`, `workspace/members`) is unchanged -- "still reachable" per this
 * task's own text.
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
        <Route path="contracts/:contractId" element={<Contract360Route apiClient={apiClient} />} />
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
