import { Outlet } from "react-router-dom";
import RailNav from "./RailNav";
import GlobalAskBar from "../ask-bar/GlobalAskBar";
import { useValidatedContractCount } from "./useValidatedContractCount";
import type { WorkspaceRole } from "./navItems";
import type { ApiClient } from "../../api/client";
import "./shell.css";

export interface AppShellProps {
  workspaceName: string;
  role: WorkspaceRole;
  userLabel: string;
  onSignOut: () => void;
  /**
   * Task E13/F09/US01/T01 (web-shell-v2): threaded through so this shell can fetch `kbReady` /
   * the validated-contract count once (`useValidatedContractCount`) and pass it to both the rail
   * (greyed secondary tier, count badges) and the global Ask bar (off placeholder) -- the same
   * generated-client instance every routed screen already shares (`WorkspaceShellApp.tsx`).
   */
  apiClient: ApiClient;
}

/**
 * The composite ADR-018/ADR-024 names: 224px rail + global Ask bar + routed content. AC-3 ("Global
 * Ask bar on every app screen") is why GlobalAskBar lives here, above `<Outlet/>`, rather than
 * inside each screen -- every route rendered through WorkspaceShellApp.tsx gets it automatically.
 */
export default function AppShell({ workspaceName, role, userLabel, onSignOut, apiClient }: AppShellProps) {
  const { count, kbReady } = useValidatedContractCount(apiClient);

  return (
    <div className="shell-layout">
      <RailNav
        workspaceName={workspaceName}
        role={role}
        userLabel={userLabel}
        onSignOut={onSignOut}
        kbReady={kbReady}
        validatedContractCount={count}
      />
      <main className="shell-main">
        <GlobalAskBar kbReady={kbReady} />
        <div className="shell-content">
          <Outlet />
        </div>
      </main>
    </div>
  );
}
