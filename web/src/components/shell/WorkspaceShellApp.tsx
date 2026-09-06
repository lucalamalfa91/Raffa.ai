import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import AppShell from "./AppShell";
import RequireRole from "./RequireRole";
import ScaffoldScreen from "./ScaffoldScreen";
import type { WorkspaceRole } from "./navItems";

export interface WorkspaceShellAppProps {
  workspaceName: string;
  role: WorkspaceRole;
  userLabel: string;
  onSignOut: () => void;
}

/**
 * The route table this task introduces — src/routes/signin/index.tsx's own
 * header comment anticipated it: "No client-side router is wired into this
 * app yet ... introducing one is E06/F03/US02/T01's ('navigation-shell')
 * job, since it also owns the admin/procurement route guards a real router
 * enables". Every path matches ADR-018's locked "Route map" table, except
 * the two rows flagged in navItems.ts's header comment (/review, /quotes),
 * which are this task's own placeholder landing routes.
 *
 * Screens that belong to a later epic render ScaffoldScreen rather than real
 * content — this task's job is the shell/guards/ask-bar, not those screens
 * (see each `note` below for the owning epic/feature).
 *
 * `ShellRoutes` (the route tree alone, no router) is exported separately so
 * tests can wrap it in a `MemoryRouter` with a specific `initialEntries`
 * path instead of depending on the real browser URL a `BrowserRouter` reads.
 */
export function ShellRoutes({ workspaceName, role, userLabel, onSignOut }: WorkspaceShellAppProps) {
  return (
    <Routes>
      <Route
        element={<AppShell workspaceName={workspaceName} role={role} userLabel={userLabel} onSignOut={onSignOut} />}
      >
        <Route
          index
          element={
            <ScaffoldScreen
              title="Home"
              release="R3"
              note="Savings KPIs + opportunities ship in epic-08/feature-02-savings-ui."
            />
          }
        />
        <Route
          path="contracts"
          element={
            <ScaffoldScreen
              title="Portfolio"
              release="R1"
              note="Portfolio table + filters ship in epic-07/feature-01-portfolio-ui."
            />
          }
        />
        <Route
          path="contracts/:contractId"
          element={
            <ScaffoldScreen
              title="Contract 360"
              release="R1"
              note="Contract 360 ships in epic-07/feature-02-contract-360-ui."
            />
          }
        />
        <Route
          path="contracts/:contractId/review"
          element={
            <ScaffoldScreen
              title="Field review"
              release="R1"
              note="Field review + correction ships in epic-07/feature-03-review-correction-ui."
            />
          }
        />
        <Route
          path="review"
          element={
            <ScaffoldScreen
              title="Review queue"
              release="R1"
              note="Review queue ships in epic-07/feature-03-review-correction-ui (ADR-020 screen 6). This landing path is this task's own placeholder — ia.md names only /contracts/:id/review."
            />
          }
        />
        <Route
          path="renewals"
          element={
            <ScaffoldScreen
              title="Renewals"
              release="R2"
              note="Renewal pipeline ships in epic-08/feature-01-renewal-pipeline-ui."
            />
          }
        />
        <Route
          path="ask"
          element={
            <ScaffoldScreen
              title="Ask Contigo"
              release="R1"
              note="Ask Contigo chat + citations ships in epic-07/feature-04-ask-contigo-ui. The global Ask bar above already gets your typed query here via router state."
            />
          }
        />
        <Route
          path="quotes"
          element={
            <ScaffoldScreen
              title="Quote check"
              release="R4"
              note="Quote check stepper ships in epic-08/feature-03-quote-check-ui. This landing path is this task's own placeholder — ADR-018 names only /quotes/:id."
            />
          }
        />
        <Route
          path="quotes/:quoteId"
          element={
            <ScaffoldScreen
              title="Quote check"
              release="R4"
              note="Quote check stepper ships in epic-08/feature-03-quote-check-ui."
            />
          }
        />
        <Route
          path="documents"
          element={
            <ScaffoldScreen
              title="Documents"
              release="R0"
              note="Upload + document status ships in epic-06/feature-05-document-upload-ui."
            />
          }
        />
        <Route
          path="workspace/members"
          element={
            <RequireRole role={role} allow="admin">
              <ScaffoldScreen
                title="Workspace & members"
                release="R0"
                note="Members table + invite ships in epic-06/feature-04-workspace-members-ui."
              />
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
