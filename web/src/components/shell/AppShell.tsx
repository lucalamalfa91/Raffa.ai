import { Outlet } from "react-router-dom";
import RailNav from "./RailNav";
import GlobalAskBar from "../ask-bar/GlobalAskBar";
import type { WorkspaceRole } from "./navItems";
import "./shell.css";

export interface AppShellProps {
  workspaceName: string;
  role: WorkspaceRole;
  userLabel: string;
  onSignOut: () => void;
}

/**
 * The composite ADR-018/us-02 names: 224px rail + global Ask bar + routed
 * content. AC-3 ("Global Ask bar on every app screen") is why GlobalAskBar
 * lives here, above `<Outlet/>`, rather than inside each screen — every
 * route rendered through WorkspaceShellApp.tsx gets it automatically.
 */
export default function AppShell({ workspaceName, role, userLabel, onSignOut }: AppShellProps) {
  return (
    <div className="shell-layout">
      <RailNav workspaceName={workspaceName} role={role} userLabel={userLabel} onSignOut={onSignOut} />
      <main className="shell-main">
        <GlobalAskBar />
        <div className="shell-content">
          <Outlet />
        </div>
      </main>
    </div>
  );
}
