import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { BrowserRouter, useNavigate } from "react-router-dom";
import type { AppConfig } from "../config/appConfig";
import { createApiClient, type WorkspaceSummaryBody } from "../api/client";
import WorkspaceShellApp from "../components/shell/WorkspaceShellApp";
import { parseWorkspaceRole } from "../components/shell/workspaceRole";
import { FixtureSessionContext, type FixtureSession } from "./FixtureSessionContext";
import {
  FIXTURE_NOW_HEADER,
  FIXTURE_USER_HEADER,
  MODE_STORAGE_KEY,
  VIEWER_STORAGE_KEY,
  createFixtureFetch,
  remember,
  resetFixtureTenant,
  type FixtureBoot,
  type FixtureScenario,
  type FixtureViewer,
} from "./fixtureMode";

const DISPLAY_NAME: Record<FixtureViewer, string> = { luca: "Luca Lamalfa", marta: "Marta Keller" };

type Phase =
  | { phase: "loading" }
  | { phase: "ready"; workspace: WorkspaceSummaryBody }
  | { phase: "error"; message: string };

interface FixtureAppProps {
  appConfig: AppConfig;
  boot: FixtureBoot;
}

/**
 * The app in fixture mode (see `fixtureMode.ts`): no MSAL, no sign-in screen. On boot it resets the
 * fixture tenant to the requested scenario, finds the fixture workspace through the normal
 * `GET /api/workspaces` (as the current viewer) and mounts the regular shell. Scenario / viewer
 * switches remount the shell so every screen reloads its data.
 */
export default function FixtureApp({ appConfig, boot }: FixtureAppProps) {
  return (
    <BrowserRouter>
      <FixtureRoot appConfig={appConfig} boot={boot} />
    </BrowserRouter>
  );
}

function FixtureRoot({ appConfig, boot }: FixtureAppProps) {
  const navigate = useNavigate();
  const viewerRef = useRef<FixtureViewer>(boot.viewer);
  const fetchImpl = useMemo(
    () => createFixtureFetch(appConfig.apiBaseUrl, () => ({ [FIXTURE_USER_HEADER]: viewerRef.current, [FIXTURE_NOW_HEADER]: boot.now })),
    [appConfig.apiBaseUrl, boot.now],
  );
  const apiClient = useMemo(() => createApiClient(appConfig.apiBaseUrl, undefined, { fetch: fetchImpl }), [appConfig.apiBaseUrl, fetchImpl]);

  const [scenario, setScenarioState] = useState<FixtureScenario>(boot.scenario);
  const [viewer, setViewerState] = useState<FixtureViewer>(boot.viewer);
  const [tenantName, setTenantName] = useState("Helvetia Foods AG");
  const [generation, setGeneration] = useState(0);
  const [state, setState] = useState<Phase>({ phase: "loading" });

  const enter = useCallback(async () => {
    const result = await apiClient.listWorkspaces();
    const workspace = result.ok ? (result.workspaces?.[0] ?? null) : null;
    if (!workspace) {
      setState({ phase: "error", message: result.error ?? "The fixture workspace is not available." });
      return;
    }
    setState({ phase: "ready", workspace });
    setGeneration((g) => g + 1);
  }, [apiClient]);

  const resetTo = useCallback(
    async (next: FixtureScenario) => {
      try {
        const session = await resetFixtureTenant(appConfig.apiBaseUrl, next, fetchImpl);
        setTenantName(session.tenantName);
      } catch (error) {
        setState({ phase: "error", message: error instanceof Error ? error.message : String(error) });
        return;
      }
      await enter();
    },
    [appConfig.apiBaseUrl, fetchImpl, enter],
  );

  // One reset per page load (StrictMode runs effects twice in development).
  const booted = useRef(false);
  useEffect(() => {
    if (booted.current) return;
    booted.current = true;
    void resetTo(boot.scenario);
  }, [boot.scenario, resetTo]);

  const session = useMemo<FixtureSession>(
    () => ({
      scenario,
      viewer,
      now: boot.now,
      tenantName,
      setScenario: async (next) => {
        remember(MODE_STORAGE_KEY, next);
        setScenarioState(next);
        await resetTo(next);
        navigate("/today");
      },
      setViewer: (next) => {
        if (next === viewerRef.current) return;
        viewerRef.current = next;
        remember(VIEWER_STORAGE_KEY, next);
        setViewerState(next);
        void enter();
      },
      reset: async () => {
        await resetTo(scenario);
        navigate("/today");
      },
    }),
    [scenario, viewer, boot.now, tenantName, resetTo, enter, navigate],
  );

  return (
    <FixtureSessionContext.Provider value={session}>
      {state.phase === "ready" ? (
        <WorkspaceShellApp
          key={generation}
          workspaceId={state.workspace.id}
          workspaceName={state.workspace.name}
          role={parseWorkspaceRole(state.workspace.role)}
          userLabel={DISPLAY_NAME[viewer]}
          apiClient={apiClient}
          onSignOut={() => undefined}
        />
      ) : state.phase === "error" ? (
        <div role="alert" className="startup-error">
          <h1>Fixture mode could not start</h1>
          <p>{state.message}</p>
        </div>
      ) : (
        <main className="app-resolving" data-testid="app-resolving" aria-busy="true" />
      )}
    </FixtureSessionContext.Provider>
  );
}
