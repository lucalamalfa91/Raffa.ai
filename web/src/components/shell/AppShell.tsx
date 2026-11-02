import { useMemo, useState } from "react";
import { Outlet, useLocation } from "react-router-dom";
import { useValidatedContractCount } from "./useValidatedContractCount";
import { useDocumentCounts } from "./useDocumentCounts";
import { usePollBudget } from "./usePollBudget";
import { usePlatformAdmin } from "./usePlatformAdmin";
import type { WorkspaceRole } from "./navItems";
import type { ApiClient } from "../../api/client";
import { DocumentViewerProvider } from "../../routes/documents/viewer/DocumentViewerOverlay";
import { I18nProvider } from "../../i18n/I18nProvider";
import DemoBar from "../../fixtures/DemoBar";
import { useFixtureSession } from "../../fixtures/FixtureSessionContext";
import TopNav from "./TopNav";
import GuidePanel from "./GuidePanel";
import { GuideProvider, useGuideState } from "./guide";
import { useTodayBadge } from "../../routes/today/loadToday";
import { useCarriedScroll } from "./useCarriedScroll";
import "./shell4.css";

export { isAskRoute } from "./isAskRoute";

export interface AppShellProps {
  /** The one resolved workspace's id, passed to every screen through the outlet (`shellContext.ts`). */
  workspaceId: string;
  workspaceName: string;
  role: WorkspaceRole;
  userLabel: string;
  onSignOut: () => void;
  onSwitchWorkspace?: () => void;
  /** The client every routed screen shares; the shell reads `kbReady` / document counts with it. */
  apiClient: ApiClient;
}

/**
 * The AskRaffa 4.0 shell (IMPLEMENTATION-PLAN.md §1 "Shell"; AskRaffa V4.dc.html lines 26-47,
 * 309-321): [demo bar, fixture mode only] + top nav + main grid (`minmax(0,1fr) 300px` with the
 * guide open, `minmax(0,1fr)` closed) + Guida Raffa. The old rail is retired.
 *
 * The shell still reads `kbReady` / the validated-contract count and the tenant's document counts
 * once for every screen (router outlet context) and re-reads them on the shared poll budget while
 * documents are being processed or wait for review.
 */
export default function AppShell(props: AppShellProps) {
  return (
    <I18nProvider>
      <GuideProvider>
        <DocumentViewerProvider apiClient={props.apiClient}>
          <ShellLayout {...props} />
        </DocumentViewerProvider>
      </GuideProvider>
    </I18nProvider>
  );
}

function ShellLayout({ workspaceId, workspaceName, role, userLabel, onSignOut, onSwitchWorkspace, apiClient }: AppShellProps) {
  const location = useLocation();
  const fixture = useFixtureSession();
  const { open } = useGuideState();
  const isPlatformAdmin = usePlatformAdmin(apiClient);
  const [pollTick, setPollTick] = useState(0);
  const refreshKey = `${location.pathname}:${pollTick}`;
  const documentCounts = useDocumentCounts(apiClient, refreshKey);
  const { count, kbReady } = useValidatedContractCount(apiClient, refreshKey);
  // Oggi badge on every screen (README "Top nav"): the caller's items.
  useTodayBadge(apiClient, workspaceId, location.pathname);
  const mainRef = useCarriedScroll(location.pathname);

  const fingerprint = useMemo(
    () =>
      documentCounts === null
        ? "pending"
        : `${documentCounts.all}/${documentCounts.needsAttention}/${documentCounts.needsReview}/${documentCounts.processing}/${documentCounts.rejected}`,
    [documentCounts],
  );
  usePollBudget({
    active: documentCounts === null || documentCounts.processing > 0 || documentCounts.needsReview > 0,
    fingerprint,
    onTick: () => setPollTick((current) => current + 1),
  });

  return (
    <div className="v4">
      {fixture ? <DemoBar /> : null}
      <TopNav showAdmin={isPlatformAdmin} workspaceName={workspaceName} role={role} userLabel={userLabel} onSignOut={onSignOut} onSwitchWorkspace={onSwitchWorkspace} />
      <div className={open ? "v4-body" : "v4-body is-guide-closed"}>
        <main className="v4-main" ref={mainRef}>
          <Outlet context={{ workspaceId, kbReady, validatedContractCount: count, documentCounts }} />
        </main>
        {open ? <GuidePanel /> : null}
      </div>
    </div>
  );
}
