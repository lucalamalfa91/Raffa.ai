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
 * **Guaranteed flow, by design -- with one optimisation.** `/signin`'s own CTA
 * (`routes/signin/index.tsx`) is redirect-only, so a signed-out invitee who signs in from *there*
 * loses this page (and the token) to the redirect round-trip -- "sign in, then open the invitation
 * link again" is the guaranteed path, not a fallback. `WorkspacePickerScreen.tsx`'s empty-list state
 * carries the pointer back for exactly that reload/redirect landing (ADR-012 w14 footer clause 3,
 * corrected copy in ADR-020's own second w14 footer).
 *
 * **This screen's own Entra CTA is the one path that can beat the guarantee.** ADR-012 w14 footer
 * clause 6 sanctions `loginPopup` *here, and only here* -- "the one path on which the invitee never
 * leaves the accept screen" (the same footer's second amendment, point 4) -- because this page is
 * never unloaded and the in-memory token survives a popup round-trip: single-click where the browser
 * allows it. `handleContinueWithEntra` below calls `loginPopup`, not `loginRedirect`. On success this
 * component does nothing further: `useMsal()`'s `accounts` (destructured above, already the reactive
 * source `App.tsx`'s own top-level gate relies on) updates via MSAL's event system once the popup
 * resolves, which alone turns the CTA into state 3's "Join" button -- no navigation, no local state
 * change. If the popup is blocked, closed, or rejects for any other reason, the handler falls back to
 * state 5 ("Open your invitation link again") -- **the same state a reload lands on, on purpose**:
 * "the popup-blocked and reload cases share one state, so it ships regardless" (ADR-012 w14 footer
 * clause 6). That state's copy already covers this without a dedicated eleventh state.
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
  // Task E17/F02/US01/T01 (ADR-012 w15 §8): set only by this screen's own Entra CTA once its popup
  // has resolved, so the accept fires on the transition THIS screen initiated -- never an effect
  // on `accounts`, which would silently auto-join a visitor who arrived already signed in and
  // remove the one consent step state 3 exists for. If the offer read has not resolved when the
  // popup does, the accept waits for it (the `useEffect` below) instead of weakening handleJoin's
  // own guard into a race.
  const [joinOnceOffered, setJoinOnceOffered] = useState(false);

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

  // Task E17/F02/US01/T01 (NW-67, A15-4): once the popup has resolved, the accept is the SAME user
  // gesture -- the consent the button carries is consent to join -- so it continues straight into
  // the accept the moment the offer is on screen, with no second, mandatory "Join" click.
  useEffect(() => {
    if (!joinOnceOffered || state.phase !== "offer") return;
    setJoinOnceOffered(false);
    handleJoin();
  }, [joinOnceOffered, state.phase, handleJoin]);

  const handleContinueWithEntra = () => {
    // ADR-012 w14 footer clause 6: `loginPopup`, not the app-wide `loginRedirect` -- the request
    // shape is identical (`{ scopes }`, `buildLoginRequest`'s only field), and structurally satisfies
    // `PopupRequest` as well as `RedirectRequest`. A rejection (blocked popup, closed by the user, or
    // any other failure) falls back to state 5, deliberately the same state a reload lands on --
    // "the popup-blocked and reload cases share one state, so it ships regardless". Task
    // E17/F02/US01/T01 (ADR-012 w15 §8): the handler now AWAITS its own promise and continues into
    // the accept on resolution -- previously "success needed no handling here", which is exactly why
    // A15-4 died on a second click. A visitor who arrived already signed in never reaches this
    // handler (state 3 renders "Join" instead), so their explicit consent step is untouched.
    instance
      .loginPopup(buildLoginRequest(appConfig))
      .then(() => {
        setJoinOnceOffered(true);
      })
      .catch(() => {
        setState({ phase: "no-token" });
      });
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
