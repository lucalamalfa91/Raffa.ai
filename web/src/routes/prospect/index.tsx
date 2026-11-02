import { useState } from "react";
import { useMsal } from "@azure/msal-react";
import type { ApiClient } from "../../api/client";
import type { AppConfig } from "../../config/appConfig";
import { buildLoginRequest } from "../../auth/msalConfig";
import { MicrosoftMark, SignInStatementPanel } from "../signin/SignInScreen";
import { rememberProspectToken, takeProspectToken } from "./prospectToken";
import "../signin/signin.css";

export interface ProspectAcceptRouteProps {
  apiClient: ApiClient;
  appConfig: AppConfig;
}

type Phase = "welcome" | "accepting" | "accepted" | "wrong-account" | "unavailable" | "failed";

const MESSAGE: Record<Exclude<Phase, "welcome" | "accepting" | "accepted">, string> = {
  "wrong-account":
    "This invitation was sent to a different email address. Sign out and sign in with the address it was sent to.",
  unavailable: "This invitation is no longer valid. It may have expired, been withdrawn or already been used. Ask Raffa for a new one.",
  failed: "We could not check this invitation. Please try again.",
};

/** Reads the secret from the URL fragment and clears the address bar before anything renders. */
function readFragmentToken(): string | null {
  const { hash } = window.location;
  if (hash.length <= 1) return null;
  const token = hash.slice(1);
  window.history.replaceState(null, "", window.location.pathname + window.location.search);
  return token;
}

/**
 * `/prospect/accept#<secret>` -- the page a prospect lands on from a Raffa invitation. Public: reachable
 * signed out, and drawn on the same canvas as the sign-in screen. Signed out, it welcomes them and sends
 * them to sign in (the secret waits in this tab for the redirect); signed in, it redeems the invitation,
 * after which the normal first-run screen lets them create their company workspace. An invitation is
 * bound to one address, single-use and expires, and the server decides all of it.
 */
export default function ProspectAcceptRoute({ apiClient, appConfig }: ProspectAcceptRouteProps) {
  const { instance, accounts } = useMsal();
  const account = accounts[0];
  const [token] = useState(() => {
    const fresh = readFragmentToken();
    if (fresh) rememberProspectToken(fresh);
    return fresh;
  });
  const [phase, setPhase] = useState<Phase>("welcome");

  const accept = async () => {
    const secret = token ?? takeProspectToken();
    if (!secret) {
      setPhase("unavailable");
      return;
    }
    setPhase("accepting");
    const result = apiClient.acceptProspectInvitation ? await apiClient.acceptProspectInvitation(secret) : null;
    takeProspectToken();
    if (result?.ok) {
      setPhase("accepted");
      window.location.assign("/");
    } else if (result?.statusCode === 403) {
      setPhase("wrong-account");
    } else if (result?.statusCode === 404 || result?.statusCode === 409 || result?.statusCode === 410) {
      setPhase("unavailable");
    } else {
      setPhase("failed");
    }
  };

  const busy = phase === "accepting";

  return (
    <main className="signin-screen">
      <SignInStatementPanel />
      <section className="signin-action" aria-labelledby="prospect-heading">
        <h2 id="prospect-heading" className="screen-title">
          Welcome to Raffa
        </h2>
        <p className="signin-subtitle">Your AI-powered procurement assistant.</p>
        {phase === "welcome" || busy ? (
          account ? (
            <button type="button" className="btn btn-primary btn-block" disabled={busy} onClick={() => void accept()}>
              {busy ? <span className="signin-spinner" aria-hidden="true" /> : <MicrosoftMark />}
              {busy ? "Accepting your invitation …" : "Accept invitation"}
            </button>
          ) : (
            <button
              type="button"
              className="btn btn-primary btn-block"
              onClick={() => void instance.loginRedirect(buildLoginRequest(appConfig))}
            >
              <MicrosoftMark />
              Sign in to accept
            </button>
          )
        ) : phase === "accepted" ? (
          <p role="status" className="micro-meta">
            Invitation accepted. Taking you to Raffa…
          </p>
        ) : (
          <p role="alert" className="micro-meta">
            {MESSAGE[phase]}
          </p>
        )}
        <hr />
        <p className="micro-meta">
          You will sign in with the email address this invitation was sent to. Microsoft sends you a one-time code; Raffa.ai
          never stores a password.
        </p>
      </section>
    </main>
  );
}
