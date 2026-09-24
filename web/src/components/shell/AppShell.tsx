import { useMemo, useState, type CSSProperties } from "react";
import { Outlet, useLocation } from "react-router-dom";
import RailNav from "./RailNav";
import RailResizeHandle from "./RailResizeHandle";
import { useRailWidth } from "./useRailWidth";
import { useValidatedContractCount } from "./useValidatedContractCount";
import { useDocumentCounts } from "./useDocumentCounts";
import { usePollBudget } from "./usePollBudget";
import type { WorkspaceRole } from "./navItems";
import type { ApiClient } from "../../api/client";
import { DocumentViewerProvider } from "../../routes/documents/viewer/DocumentViewerOverlay";
import { AskSessionsProvider } from "../../routes/ask/AskSessionsContext";
import AskReplyNotifier from "../../routes/ask/AskReplyNotifier";
import "./shell.css";

export { isAskRoute } from "./isAskRoute";

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
   * the validated-contract count (`useValidatedContractCount`) and pass it to both the rail
   * (greyed secondary tier, count badges) and the global Ask bar (off placeholder) -- the same
   * generated-client instance every routed screen already shares (`WorkspaceShellApp.tsx`).
   */
  apiClient: ApiClient;
}

/**
 * The shell: resizable rail + routed content. There is no global Ask bar above the content --
 * screens carry their own "Ask Raffa" action and `/ask` renders its own composer.
 *
 * Task E14/F03/US02/T01 (wave w14): `useValidatedContractCount` now answers from NW-01's server
 * field instead of a client-side portfolio scan (see that hook's own header comment) -- this
 * component's own call site is unchanged, because the one resolved workspace it renders for is
 * exactly what makes that field meaningful.
 *
 * While documents are still `Uploaded`/`Processing` or waiting in Needs review, both rail counts
 * re-read on the shared 2 s poll budget (and on every navigation) so "N to review" / Portfolio /
 * Renewals move with ingest instead of freezing at the first shell mount.
 */

export default function AppShell({ workspaceId, workspaceName, role, userLabel, onSignOut, apiClient }: AppShellProps) {
  const location = useLocation();
  const [pollTick, setPollTick] = useState(0);
  const railWidth = useRailWidth();
  const refreshKey = `${location.pathname}:${pollTick}`;
  const documentCounts = useDocumentCounts(apiClient, refreshKey);
  const { count, kbReady } = useValidatedContractCount(apiClient, refreshKey);

  const fingerprint = useMemo(
    () =>
      documentCounts === null
        ? "pending"
        : `${documentCounts.all}/${documentCounts.needsAttention}/${documentCounts.needsReview}/${documentCounts.processing}/${documentCounts.rejected}`,
    [documentCounts],
  );
  usePollBudget({
    active:
      documentCounts === null ||
      documentCounts.processing > 0 ||
      documentCounts.needsReview > 0,
    fingerprint,
    onTick: () => setPollTick((current) => current + 1),
  });


  return (
    <DocumentViewerProvider apiClient={apiClient}>
      {/* Parallel Ask sessions (`routes/ask/askSessions.ts`): one store for the rail, the Ask screen
          and the "reply ready" notices, so a chat keeps answering while the user is elsewhere. */}
      <AskSessionsProvider>
        {/* The rail is resizable (RailResizeHandle): its width is this one custom property. */}
        <div className="shell-layout" style={{ "--shell-rail-width": `${railWidth.width}px` } as CSSProperties}>
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
          <RailResizeHandle {...railWidth} />
          <main className="shell-main">
            {/* No global Ask bar: every screen already offers its own "Ask Raffa" action, and /ask
                has its own composer. */}
            <div className="shell-content">
              {/* Shared with every screen through the router outlet (shellContext.ts): the same kbReady /
                  validated-count verdict the rail and the Ask bar already render, plus the same document
                  counts the rail badge shows, so a screen never has to re-fetch for a second opinion. */}
              <Outlet context={{ workspaceId, kbReady, validatedContractCount: count, documentCounts }} />
            </div>
          </main>
        </div>
        <AskReplyNotifier />
      </AskSessionsProvider>
    </DocumentViewerProvider>
  );
}
