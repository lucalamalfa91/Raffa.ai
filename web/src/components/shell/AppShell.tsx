import { Outlet } from "react-router-dom";
import RailNav from "./RailNav";
import GlobalAskBar from "../ask-bar/GlobalAskBar";
import { useValidatedContractCount } from "./useValidatedContractCount";
import { useDocumentCounts } from "./useDocumentCounts";
import type { WorkspaceRole } from "./navItems";
import type { ApiClient } from "../../api/client";
import "./shell.css";

export interface AppShellProps {
  /**
   * Task E14/F03/US02/T01 (wave w14 "workspace is real"): the one resolved workspace's id, passed
   * through to the router outlet (`shellContext.ts`) so a routed screen never has to re-derive it
   * from the session hint. This shell does not call an endpoint with it itself.
   */
  workspaceId: string;
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
 * The composite ADR-018/ADR-024 names: 232px rail + global Ask bar + routed content. AC-3 ("Global
 * Ask bar on every app screen") is why GlobalAskBar lives here, above `<Outlet/>`, rather than
 * inside each screen -- every route rendered through WorkspaceShellApp.tsx gets it automatically.
 *
 * Task E14/F03/US02/T01 (wave w14): `useValidatedContractCount` now answers from NW-01's server
 * field instead of a client-side portfolio scan (see that hook's own header comment) -- this
 * component's own call site is unchanged, because the one resolved workspace it renders for is
 * exactly what makes that field meaningful.
 */
export default function AppShell({ workspaceId, workspaceName, role, userLabel, onSignOut, apiClient }: AppShellProps) {
  const { count, kbReady } = useValidatedContractCount(apiClient);
  // Task E16/F03/US01/T01 (wave w15, NW-10): the rail's Documents badge reads the server's own
  // `counts`, fetched once here beside the validated-contract count and handed down the same way.
  const documentCounts = useDocumentCounts(apiClient);

  return (
    <div className="shell-layout">
      <RailNav
        workspaceName={workspaceName}
        role={role}
        userLabel={userLabel}
        onSignOut={onSignOut}
        kbReady={kbReady}
        validatedContractCount={count}
        documentCounts={documentCounts}
        apiClient={apiClient}
      />
      <main className="shell-main">
        <GlobalAskBar kbReady={kbReady} apiClient={apiClient} />
        <div className="shell-content">
          {/* Shared with every screen through the router outlet (shellContext.ts): the same kbReady /
              validated-count verdict the rail and the Ask bar already render, plus the same document
              counts the rail badge shows, so a screen never has to re-fetch for a second opinion. */}
          <Outlet context={{ workspaceId, kbReady, validatedContractCount: count, documentCounts }} />
        </div>
      </main>
    </div>
  );
}
