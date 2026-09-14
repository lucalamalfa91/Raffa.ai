import { useCallback, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useMsal } from "@azure/msal-react";
import type { ApiClient } from "../../../api/client";
import type { AppConfig } from "../../../config/appConfig";
import { buildLoginRequest } from "../../../auth/msalConfig";
import { workspaceRoleLabel } from "../../../components/shell/workspaceRole";
import { selectCurrentWorkspace } from "../../signin/workspaceStore";
import "./accept.css";

export interface AcceptInvitationRouteProps {
  apiClient: ApiClient;
  appConfig: AppConfig;
}

/**
 * Task E14/F03/US02/T01 (wave w14 "workspace is real"). Screen 11, `/invite/accept` -- the one
 * public route this wave adds (ADR-018 w14 footer): reachable signed out and with no workspace,
 * rendered outside `AppShell` (an invitee is not yet a member of anything the rail could list), ten
 * states (ADR-020's screen-11 footer). Nothing resembling this screen exists in the V2 export; every
 * state and every rule below is this ADR's own, not a pixel quoted from `markup.html`.
 *
 * **Rule C9 (ADR-025 §C).** The token travels in the URL *fragment* (`/invite/accept#<token>`),
 * never a path segment or a query string -- a query string would ride into the Static Web Apps
 * platform's own access log, the `Referer` header of every third-party asset this page loads, and
 * browser history; a fragment does none of that and is never sent to any server at all.
 *
 * **Rule C10/C10a/C10b (ADR-025 §C, its own w14 footer).** The token is read once, held in memory
 * for this one mount, and never written to any browser-persisted store of any kind --
 * `readAndClearInvitationToken` below runs inside a lazy `useState` initializer specifically
 * so the address bar is already clear *before this component returns its first JSX*, i.e. strictly
 * before any control that could initiate authentication (the Entra CTA, state 2) ever renders. Rule
 * C10a (`navigateToLoginRequestUrl: false`, explicit) lives in `src/auth/msalConfig.ts`; both rules
 * are needed because they fail differently -- C10a survives a refactor of *this* ordering, C10b
 * survives a change to MSAL's own default.
 *
 * **Fix 2026-09-14: `loginRedirect`, not `loginPopup` -- the guarantee is now unconditional.**
 * ADR-012 w14 footer clause 6 originally sanctioned `loginPopup` here, and only here, so a signed-out
 * invitee's click never unloaded this page and the in-memory token survived the round-trip. In
 * practice that round-trip depends on `@azure/msal-browser`'s popup-completion handoff -- the popup
 * lands back on this app's own origin, and a `BroadcastChannel` the popup's own fresh page instance
 * must post to is what lets the opener's `loginPopup()` promise ever resolve
 * (`node_modules/@azure/msal-browser/dist/utils/BrowserUtils.mjs`'s `waitForBridgeResponse`) -- and on
 * dev that handoff was observed to hang indefinitely: the popup lands on `#code=...`, shows this
 * app's own generic sign-in screen stuck mid-interaction, and never closes. Confirmed live 2026-09-14.
 *
 * The redirect-loses-the-token trade-off this screen was built to avoid no longer matters: a signed-
 * out invitee who clicks this CTA now leaves via `loginRedirect` exactly like `/signin`'s own CTA
 * always has (`routes/signin/index.tsx`), and lands back at `App.tsx`'s account/workspace gate with
 * no token and no memory of ever having been on this page -- deliberately. That gate's own
 * `AuthenticatedGate` (fix 2026-09-14) now retries the join itself, silently, from the identity alone:
 * `GET /api/workspaces` surfaces the same live invitation as a `pendingInvitation`, and
 * `POST /api/workspaces/{tenantId}/invites/accept` grants it with no token at all. The invitee lands
 * directly in the workspace with no picker and no second click -- a strictly better outcome than the
 * popup's own best case, reached over a path (full-page redirect) that is already proven reliable
 * (it is `/signin`'s only path). "Sign in, then open the invitation link again" -- this screen's own
 * long-standing fallback copy (state 5, below) -- is no longer the outcome for this CTA either: the
 * invitee does not need to come back here at all.
 *
 * The one thing this trade gives up: state 5 ("wrong-account") and this screen's own "invalid"/
 * "already-accepted" copy are reachable only via `handleJoin` below now -- the token-based accept for
 * a visitor who arrives *already* signed in (state 3, unchanged). A signed-out visitor who redirects
 * through the wrong Microsoft account no longer sees "this invitation was sent to a different
 * address"; they see the ordinary empty-workspace picker, because `AcceptForIdentityAsync`
 * deliberately answers "no live invitation for this identity here" and "wrong identity entirely" with
 * the same 404 (never an oracle -- see that method's own doc comment, backend). Accepted as strictly
 * better than the failure mode this fix replaces: a screen stuck forever with no error at all.
 */
function readAndClearInvitationToken(): string | null {
  const { hash } = window.location;
  if (hash.length <= 1) {
    return null;
  }
  const token = hash.slice(1);
  // Rule C10b: clear the address bar synchronously, in this same lazy initializer, so it has
  // already happened by the time this component's first render -- including its own Entra CTA --
  // ever reaches the DOM.
  window.history.replaceState(null, "", window.location.pathname + window.location.search);
  return token;
}

type AcceptState =
  | { phase: "no-token" }
  | { phase: "loading" }
  | { phase: "offer"; workspaceName: string; role: string }
  | { phase: "accepting"; workspaceName: string; role: string }
  | { phase: "wrong-account" }
  /** Expired, revoked, or unknown/malformed -- one state, one first sentence, on purpose (ADR-020
   * screen-11 footer point 4): the screen must never tell a probing visitor which case they hit. */
  | { phase: "invalid" }
  | { phase: "already-accepted"; workspaceName: string }
  | { phase: "transport-error"; message: string; retry: () => void };

function MicrosoftMark() {
  // Duplicated from `routes/signin/SignInScreen.tsx`'s own un-exported icon rather than imported --
  // two independent screens, one small pure decoration, the same "duplicated, not imported"
  // precedent `useValidatedContractCount.ts`'s own header comment sets for this exact situation.
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true" focusable="false">
      <rect x="1" y="1" width="6.5" height="6.5" fill="currentColor" />
      <rect x="8.5" y="1" width="6.5" height="6.5" fill="currentColor" opacity=".7" />
      <rect x="1" y="8.5" width="6.5" height="6.5" fill="currentColor" opacity=".7" />
      <rect x="8.5" y="8.5" width="6.5" height="6.5" fill="currentColor" opacity=".45" />
    </svg>
  );
}

export default function AcceptInvitationRoute({ apiClient, appConfig }: AcceptInvitationRouteProps) {
  const navigate = useNavigate();
  const { instance, accounts } = useMsal();
  const account = accounts[0];

  const [token] = useState(readAndClearInvitationToken);
  const [state, setState] = useState<AcceptState>(() =>
    token === null ? { phase: "no-token" } : { phase: "loading" },
  );

  const fetchInvitation = useCallback(() => {
    if (token === null) return;
    setState({ phase: "loading" });
    void apiClient.getInvitation(token).then((result) => {
      if (result.ok && result.invitation) {
        setState({ phase: "offer", workspaceName: result.invitation.workspaceName, role: result.invitation.role });
        return;
      }
      if (result.statusCode === 410 || result.statusCode === 404) {
        setState({ phase: "invalid" });
        return;
      }
      setState({
        phase: "transport-error",
        message: result.error ?? "Could not check this invitation link.",
        retry: fetchInvitation,
      });
    });
  }, [apiClient, token]);

  useEffect(() => {
    // Mount-only: `token` is read once (readAndClearInvitationToken, above) and never changes again
    // for the life of this component -- the same "read once, on mount" convention
    // `routes/documents/index.tsx`'s own `?filter=` effect already uses in this app.
    if (token !== null) {
      fetchInvitation();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleJoin = useCallback(() => {
    if (state.phase !== "offer") return;
    const { workspaceName, role } = state;
    if (token === null) return;

    setState({ phase: "accepting", workspaceName, role });
    void apiClient.acceptInvitation(token).then((result) => {
      if (result.ok && result.acceptance) {
        // A hint, not a trust -- membership is proven by the next GET /api/workspaces, which the
        // navigation below triggers by remounting the account/workspace gate fresh. Setting the
        // hint first means that re-resolution enters *this* workspace even if the caller already
        // belongs to others (the "hint ∈ list" path, not a fresh picker).
        selectCurrentWorkspace({ id: result.acceptance.workspaceId, name: result.acceptance.workspaceName });
        navigate("/", { replace: true });
        return;
      }
      if (result.statusCode === 403) {
        setState({ phase: "wrong-account" });
        return;
      }
      if (result.statusCode === 410 || result.statusCode === 404) {
        setState({ phase: "invalid" });
        return;
      }
      if (result.statusCode === 409) {
        setState({ phase: "already-accepted", workspaceName });
        return;
      }
      setState({
        phase: "transport-error",
        message: result.error ?? "Could not accept this invitation.",
        retry: handleJoin,
      });
    });
  }, [apiClient, navigate, state, token]);

  const handleContinueWithEntra = () => {
    // Fix 2026-09-14: `loginRedirect`, the same call `/signin`'s own CTA makes
    // (`routes/signin/index.tsx`'s `handleContinue`) -- see this file's own header comment for why
    // the popup this used to call is gone. Fire-and-forget, same as that CTA: the browser is about to
    // navigate away, there is no meaningful `.then()` to run first, and `AuthenticatedGate` (App.tsx)
    // -- not this component, which will have unmounted -- picks up the join on the other side.
    void instance.loginRedirect(buildLoginRequest(appConfig));
  };

  const handleSignOutAndSwitch = () => {
    void instance.logoutRedirect();
  };

  // "Already accepted" (409) carries no body (a bare string, ADR-026's schema) -- there is no id to
  // set as a hint, unlike the real success path above. The next GET /api/workspaces still finds
  // this membership on its own once re-resolution runs; a name-only hint would risk matching the
  // wrong row if two workspaces ever shared a name.
  const goToWorkspace = () => {
    navigate("/", { replace: true });
  };

  return (
    <main className="invite-accept-screen">
      <section className="card invite-accept-card">
        {state.phase === "no-token" && (
          <>
            <h2 className="screen-title">Open your invitation link again</h2>
            <p className="micro-meta">
              Signing in takes you away from this page, so the invitation link has to be opened once
              more. It is still valid.
            </p>
          </>
        )}

        {state.phase === "loading" && (
          <div role="status" aria-live="polite">
            <p className="micro-meta">Checking your invitation…</p>
            {Array.from({ length: 3 }, (_, index) => (
              <div key={index} className="skeleton invite-accept-skeleton-row" />
            ))}
          </div>
        )}

        {(state.phase === "offer" || state.phase === "accepting") && (
          <>
            <p className="screen-kicker">{account ? `Signed in as ${account.username}` : "Invitation"}</p>
            <h2 className="screen-title">Join {state.workspaceName}</h2>
            <p className="micro-meta">You have been invited as {workspaceRoleLabel(state.role)}.</p>
            {account ? (
              <button
                type="button"
                className="btn btn-primary btn-block"
                onClick={handleJoin}
                disabled={state.phase === "accepting"}
              >
                {state.phase === "accepting" ? "Joining…" : `Join ${state.workspaceName}`}
              </button>
            ) : (
              <button type="button" className="btn btn-primary btn-block" onClick={handleContinueWithEntra}>
                <MicrosoftMark />
                Continue with Microsoft Entra ID
              </button>
            )}
          </>
        )}

        {state.phase === "wrong-account" && (
          <>
            <h2 className="screen-title">This invitation was sent to a different address.</h2>
            <p className="micro-meta">You are signed in as {account?.username ?? "a different account"}.</p>
            <button type="button" className="btn btn-secondary" onClick={handleSignOutAndSwitch}>
              Sign out and use a different account
            </button>
          </>
        )}

        {state.phase === "invalid" && (
          // States 7/9 share this first sentence on purpose (ADR-020 screen-11 footer point 4) --
          // "expired" and "revoked/unknown" render identically so this screen never tells a probing
          // visitor which case they hit.
          <>
            <h2 className="screen-title">This invitation is no longer valid.</h2>
            <p className="micro-meta">Ask a Workspace Admin to send you a new one.</p>
          </>
        )}

        {state.phase === "already-accepted" && (
          <>
            <h2 className="screen-title">You have already joined {state.workspaceName}.</h2>
            <button type="button" className="btn btn-primary" onClick={goToWorkspace}>
              Go to {state.workspaceName}
            </button>
          </>
        )}

        {state.phase === "transport-error" && (
          // ADR-018 `:112-119`: a token lookup that 503s is an error; expired/revoked are terminal
          // informational states above and do not get this treatment.
          <div className="error-state" role="alert">
            <h4>Invitation lookup unavailable</h4>
            <p className="micro-meta">{state.message}</p>
            <button type="button" className="btn btn-secondary" onClick={state.retry}>
              Retry
            </button>
          </div>
        )}
      </section>
    </main>
  );
}
