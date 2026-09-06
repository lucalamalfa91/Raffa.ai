import { useEffect, useState } from "react";
import { useMsal } from "@azure/msal-react";
import type { AppConfig } from "./config/appConfig";
import type { ApiClient } from "./api/client";
import SignInRoute from "./routes/signin";
import { clearCurrentWorkspace, loadCurrentWorkspace } from "./routes/signin/workspaceStore";
import WorkspaceShellApp from "./components/shell/WorkspaceShellApp";
import { resolveWorkspaceRole } from "./components/shell/workspaceRole";

interface AppProps {
  appConfig: AppConfig;
  apiClient: ApiClient;
}

// Task E01/F07/US01/T02: mirrors the generated API client's HealthCheckResult
// (src/api/client.ts) but adds the "still in flight" phase a UI needs and the
// wrapper's own result did not: a health check that has not resolved yet is
// not the same thing as an unreachable API.
type HealthState =
  | { phase: "checking" }
  | { phase: "ok"; body: string }
  | { phase: "unreachable"; statusCode: number | null; body: string };

// Task E06/F03/US01/T01 (signin-workspace-picker): App used to own a minimal
// inline sign-in/sign-out shell directly (task E01/F07/US01/T02's proof that
// the OIDC Authorization Code + PKCE flow works end to end). That shell is
// now ./routes/signin (SignInRoute) -- the real `/signin` screen (ADR-018)
// with the workspace picker AC-1/AC-2 need.
//
// Task E06/F03/US02/T01 (navigation-shell) added App's second composition
// decision: signed in AND a workspace picked -> mount the router + shell
// this task introduces (WorkspaceShellApp, components/shell/); otherwise
// (no account yet, or an account that has not picked a workspace this
// session) -> ./routes/signin, unchanged from task E06/F03/US01/T01. This is
// exactly the seam SignInRoute's own header comment already anticipated
// ("introducing [a router] ... is E06/F03/US02/T01's job").
// loadCurrentWorkspace() is workspaceStore.ts's existing sessionStorage
// read, used here read-only (that file's own exports are unchanged).
// WorkspacePickerScreen.tsx's "Continue to <workspace> ->" control is a
// plain hard navigation (`<a href="/">`), not a client-side router link --
// no router is mounted at that point in the tree yet -- so the fresh page
// load re-runs this component from scratch with both facts already true
// (MSAL's own sessionStorage-cached account, and this same sessionStorage
// "current workspace") rather than needing extra state plumbing between the
// two screens.
export default function App({ appConfig, apiClient }: AppProps) {
  const { instance, accounts } = useMsal();
  const account = accounts[0];
  const workspace = loadCurrentWorkspace();

  // "wire /health" (task E01/F07/US01/T02) and the parent story's Definition
  // of Done ("curl on /health via the API client succeeds"): a static SPA has
  // no shell to literally run curl in, so this effect is the equivalent
  // proof-of-connectivity -- every load of the deployed bundle calls /health
  // through the generated-type-backed client and surfaces the result,
  // independent of sign-in state (a reachability probe, not an authenticated
  // call).
  const [health, setHealth] = useState<HealthState>({ phase: "checking" });
  useEffect(() => {
    let cancelled = false;
    setHealth({ phase: "checking" });
    void apiClient.getHealth().then((result) => {
      if (cancelled) return;
      setHealth(
        result.ok
          ? { phase: "ok", body: result.body }
          : { phase: "unreachable", statusCode: result.statusCode, body: result.body },
      );
    });
    return () => {
      cancelled = true;
    };
  }, [apiClient]);

  const healthStatus = (
    <p data-testid="api-health-status">
      {health.phase === "checking" && "API: checking…"}
      {health.phase === "ok" && `API: reachable (${health.body})`}
      {health.phase === "unreachable" &&
        `API: unreachable (${health.statusCode ?? "network error"}: ${health.body})`}
    </p>
  );

  if (!account || !workspace) {
    return (
      <main>
        {healthStatus}
        <SignInRoute appConfig={appConfig} apiClient={apiClient} />
      </main>
    );
  }

  return (
    <>
      {healthStatus}
      <WorkspaceShellApp
        workspaceName={workspace.name}
        role={resolveWorkspaceRole()}
        userLabel={account.username}
        onSignOut={() => {
          // Mirrors SignInRoute's own handleSignOut (index.tsx): a new
          // sign-in should re-pick a workspace explicitly, not silently
          // resume whichever tenant this session left selected.
          clearCurrentWorkspace();
          void instance.logoutRedirect();
        }}
      />
    </>
  );
}
