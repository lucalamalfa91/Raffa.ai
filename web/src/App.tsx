import { useCallback, useEffect, useState } from "react";
import { BrowserRouter, Route, Routes } from "react-router-dom";
import { useMsal } from "@azure/msal-react";
import type { AccountInfo, IPublicClientApplication } from "@azure/msal-browser";
import type { AppConfig } from "./config/appConfig";
import type { ApiClient, WorkspaceSummaryBody } from "./api/client";
import SignInRoute from "./routes/signin";
import { clearCurrentWorkspace, loadCurrentWorkspace, selectCurrentWorkspace } from "./routes/signin/workspaceStore";
import { resolveWorkspaceSelection, type WorkspacePickerState } from "./routes/signin/WorkspacePickerScreen";
import WorkspaceShellApp from "./components/shell/WorkspaceShellApp";
import { parseWorkspaceRole } from "./components/shell/workspaceRole";
import AcceptInvitationRoute from "./routes/invite/accept";

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

/**
 * Task E14/F03/US02/T01 (wave w14 "workspace is real"; ADR-012/ADR-018/ADR-025
 * w14 footers). `App.tsx`'s own resolution outcome: everything
 * `WorkspacePickerState` (routes/signin/WorkspacePickerScreen.tsx) already
 * models, plus the one phase that skips that screen entirely -- a definite
 * workspace to enter.
 */
type AppResolution = WorkspacePickerState | { phase: "resolved"; workspace: WorkspaceSummaryBody };

interface AuthenticatedGateProps {
  account: AccountInfo | undefined;
  instance: IPublicClientApplication;
  appConfig: AppConfig;
  apiClient: ApiClient;
}

/**
 * The account/workspace gate `App.tsx` used to run inline, factored out so `App` itself only wires
 * the router and the health probe. `!account` is the one branch resolved with no network wait (MSAL
 * already knows synchronously); everything else waits on one `apiClient.listWorkspaces()` call.
 *
 * **The third state.** `App` used to have two branches (`!account || !workspace` -> `SignInRoute`;
 * else -> the shell), because `loadCurrentWorkspace()` was a synchronous, blindly-trusted
 * `sessionStorage` read (AC-1/AC-2 of us-01-signin-workspace-picker). Once the workspace became a
 * server fact (ADR-026 §D1) that trust has to be earned with a request, and a request has a state in
 * between "not asked yet" and "answered" -- so there are three branches now: *resolving* (this
 * gate's own account-having, not-yet-answered moment, rendered as a neutral placeholder that is
 * neither `SignInRoute` nor the shell); *no account* -> `SignInRoute`; *account + resolved* -> the
 * shell or (if resolution needs the caller to choose or create one) `SignInRoute`'s picker branch.
 *
 * **Resolution order** (AC-2/AC-3, `resolveWorkspaceSelection`): empty -> create form; exactly one
 * row -> enter it, no picker; >=2 rows with the session hint in the list -> enter the hint's row;
 * >=2 rows with the hint absent or not found -> picker, and the hint is discarded (`hint ∉ list ⇒
 * discard the hint` -- no endpoint, no polling, no cache invalidation; revalidation against this
 * same GET *is* the mechanism). `hasResolvedOnce` is what keeps that gap narrow: only the *first*
 * resolution after an account appears (or changes) renders the blank placeholder below: a `Retry`
 * from the picker's error state re-runs the identical resolution but stays inside the picker's own
 * chrome while it does, because by then the caller is already looking at `/signin`, not reloading
 * into it.
 */
function AuthenticatedGate({ account, instance, appConfig, apiClient }: AuthenticatedGateProps) {
  const [resolution, setResolution] = useState<AppResolution>({ phase: "resolving" });
  const [hasResolvedOnce, setHasResolvedOnce] = useState(false);

  const runResolution = useCallback(() => {
    setResolution({ phase: "resolving" });
    void apiClient.listWorkspaces().then((result) => {
      setHasResolvedOnce(true);

      if (!result.ok || result.workspaces === null) {
        setResolution({ phase: "error", message: result.error ?? "The workspace list is unavailable." });
        return;
      }

      const decision = resolveWorkspaceSelection(result.workspaces, loadCurrentWorkspace());

      if (decision.kind === "empty") {
        setResolution({ phase: "empty" });
        return;
      }

      if (decision.kind === "pick") {
        // hint ∉ list, or there never was one -- either way nothing here should keep trusting it.
        clearCurrentWorkspace();
        setResolution({ phase: "pick", workspaces: decision.workspaces });
        return;
      }

      selectCurrentWorkspace({ id: decision.workspace.id, name: decision.workspace.name });
      setResolution({ phase: "resolved", workspace: decision.workspace });
    });
  }, [apiClient]);

  useEffect(() => {
    if (!account) return;
    runResolution();
    // account?.homeAccountId (a primitive), not account itself -- MSAL hands back a fresh object on
    // every render, the same convention this app already applies to loadCurrentWorkspace() callers.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [account?.homeAccountId, runResolution]);

  const enterWorkspace = useCallback((workspace: WorkspaceSummaryBody) => {
    selectCurrentWorkspace({ id: workspace.id, name: workspace.name });
    setHasResolvedOnce(true);
    setResolution({ phase: "resolved", workspace });
  }, []);

  const handleSignOut = useCallback(() => {
    // Mirrors the picker's own handleSignOut: a new sign-in should re-resolve a workspace
    // explicitly, not silently resume whichever tenant this session left selected.
    clearCurrentWorkspace();
    void instance.logoutRedirect();
  }, [instance]);

  if (!account) {
    return <SignInRoute appConfig={appConfig} apiClient={apiClient} picker={null} />;
  }

  if (!hasResolvedOnce && resolution.phase === "resolving") {
    // AC-8: a neutral full-page state, not SignInRoute and not the shell. Rendering SignInRoute here
    // would flash its picker chrome at every returning user with exactly one workspace -- by far the
    // common case -- immediately before the very same resolution auto-enters them; that flash reads
    // as a logout on every reload, which is the failure this branch exists to prevent.
    return <main className="app-resolving" data-testid="app-resolving" aria-busy="true" />;
  }

  if (resolution.phase === "resolved") {
    return (
      <WorkspaceShellApp
        workspaceId={resolution.workspace.id}
        workspaceName={resolution.workspace.name}
        role={parseWorkspaceRole(resolution.workspace.role)}
        userLabel={account.username}
        apiClient={apiClient}
        onSignOut={handleSignOut}
      />
    );
  }

  return (
    <SignInRoute
      appConfig={appConfig}
      apiClient={apiClient}
      picker={{ state: resolution, onRetry: runResolution, onEnter: enterWorkspace }}
    />
  );
}

// Task E06/F03/US01/T01 (signin-workspace-picker): App used to own a minimal
// inline sign-in/sign-out shell directly (task E01/F07/US01/T02's proof that
// the OIDC Authorization Code + PKCE flow works end to end). That shell is
// now ./routes/signin (SignInRoute) -- the real `/signin` screen (ADR-018)
// with the workspace picker AC-1/AC-2 need.
//
// Task E14/F03/US02/T01 (wave w14 "workspace is real"; ADR-018/ADR-025 w14
// footers): **`BrowserRouter` hoists up into this component.** Every route
// used to live inside `WorkspaceShellApp`, which mounted its own
// `BrowserRouter` only once `account && workspace` were both true -- so a
// signed-out (or not-yet-a-member) invitee had no router mounted anywhere
// above them at all, and `/invite/accept` (reachable signed out and with no
// workspace, ADR-018 w14 footer) could not be added to that inner
// `<Routes>`. One router now spans the whole app: a public branch for
// `/invite/accept`, rendered outside the shell entirely, and every other path falling
// through to `AuthenticatedGate` above -- the same account/workspace gate
// this component always ran, now able to coexist with a route the gate
// itself must never see. `ShellRoutes` stays exported separately
// (`components/shell/WorkspaceShellApp.tsx`) as the testing seam that made
// this a supported change rather than a rewrite.
export default function App({ appConfig, apiClient }: AppProps) {
  const { instance, accounts } = useMsal();
  const account = accounts[0];

  // "wire /health" (task E01/F07/US01/T02) and the parent story's Definition
  // of Done ("curl on /health via the API client succeeds"): a static SPA has
  // no shell to literally run curl in, so this effect is the equivalent
  // proof-of-connectivity -- every load of the deployed bundle calls /health
  // through the generated-type-backed client and surfaces the result,
  // independent of sign-in state (a reachability probe, not an authenticated
  // call) -- which is also why it renders above the router rather than
  // inside either branch below.
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

  // Task E11/F01/US01/T01 (hide-health-lock-chrome; ADR-019, gap G-HEALTH):
  // the compiled prototype's canvas has no "API:" line. The probe stays wired
  // exactly as task E01/F07/US01/T02 left it (still runs on every mount,
  // still exposes data-testid for tests) -- only the paint moves, via the
  // `.visually-hidden` clip technique (base.css), not `display:none`, so the
  // node stays in the accessibility tree and this testid still resolves
  // (AC-1, AC-3).
  const healthStatus = (
    <p className="visually-hidden" data-testid="api-health-status">
      {health.phase === "checking" && "API: checking…"}
      {health.phase === "ok" && `API: reachable (${health.body})`}
      {health.phase === "unreachable" &&
        `API: unreachable (${health.statusCode ?? "network error"}: ${health.body})`}
    </p>
  );

  return (
    <BrowserRouter>
      {healthStatus}
      <Routes>
        {/* ADR-018 w14 footer: the one public route, reachable signed out and with no workspace,
            rendered outside AppShell. Token in the URL fragment (ADR-025 Rule C9) -- never a query
            string, never a path parameter. */}
        <Route path="/invite/accept" element={<AcceptInvitationRoute apiClient={apiClient} appConfig={appConfig} />} />
        <Route
          path="/*"
          element={
            <AuthenticatedGate account={account} instance={instance} appConfig={appConfig} apiClient={apiClient} />
          }
        />
      </Routes>
    </BrowserRouter>
  );
}
