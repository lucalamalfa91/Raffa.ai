import { useState } from "react";

export interface SignInScreenProps {
  /** Starts the MSAL Authorization Code + PKCE redirect (ADR-012, ADR-010). */
  onContinue: () => void;
  /**
   * True while MSAL itself is mid-interaction -- @azure/msal-react's
   * `useMsal().inProgress` via `InteractionStatus`, threaded down from
   * ./index.tsx. This covers the return leg (Entra redirecting back and MSAL
   * processing that response); the `clicked` state below covers the brief
   * moment between this screen's own button click and the browser actually
   * navigating away, which `inProgress` alone would not yet reflect.
   */
  interactionInFlight: boolean;
}

// Screen 1, V2 (inputs/design/prototypes/contigo-v2/markup.html, the
// `<!-- SIGN-IN / WORKSPACE -->` block; screens-v2.md #1): "Left: statement
// panel (accent-100 ground with grid, three-line north star, the four
// answers). Right: 'Continue with Microsoft Entra ID' -> redirect state ->
// workspace list."
//
// The V1 panel this replaces was built from the day1 export (a single
// north-star sentence and four kicker/bold pairs). The V2 design keeps the
// same canvas -- accent ground, grid, lockup, space-between flow -- and
// changes what it says: a three-line statement whose middle line is the
// accent one, and a bordered list that pairs each capability with the
// question it answers. Copy below is quoted verbatim from that markup.
const NORTH_STAR_LINES: ReadonlyArray<{ text: string; accent: boolean }> = [
  { text: "Your contracts.", accent: false },
  { text: "Your savings.", accent: true },
  { text: "Nothing missed.", accent: false },
];

const ANSWERS: ReadonlyArray<{ capability: string; answer: string }> = [
  { capability: "Contract Intelligence", answer: "What you bought" },
  { capability: "Renewal Intelligence", answer: "When to act" },
  { capability: "Savings Intelligence", answer: "Where to save" },
  { capability: "Quote Check", answer: "Before you buy" },
];

// Task E06/F06/US01/T01 (full-bleed-layout): the left "statement panel" half
// of screen 1 is constant across all three of its named states (idle ·
// redirecting · workspace picker) -- in the V2 markup too, the statement
// `<div>` sits outside the `sc-if` blocks that switch the right panel.
// Exported so WorkspacePickerScreen.tsx renders the same canvas.
export function SignInStatementPanel() {
  return (
    <section className="signin-statement">
      <div className="signin-lockup">
        <span className="signin-lockup-mark" aria-hidden="true" />
        Contigo
      </div>
      <div className="signin-statement-body">
        <p className="signin-north-star">
          {NORTH_STAR_LINES.map((line) => (
            <span key={line.text} className={line.accent ? "signin-accent" : undefined}>
              {line.text}
            </span>
          ))}
        </p>
        <dl className="signin-answers">
          <div className="signin-answers-kicker">One platform, four answers</div>
          {ANSWERS.map((entry) => (
            <div className="signin-answer" key={entry.capability}>
              <dt>{entry.capability}</dt>
              <dd>{entry.answer}</dd>
            </div>
          ))}
        </dl>
      </div>
      <p className="signin-statement-footer">Contract intelligence for procurement teams.</p>
    </section>
  );
}

function MicrosoftMark() {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true" focusable="false">
      <rect x="1" y="1" width="6.5" height="6.5" fill="currentColor" />
      <rect x="8.5" y="1" width="6.5" height="6.5" fill="currentColor" opacity=".7" />
      <rect x="1" y="8.5" width="6.5" height="6.5" fill="currentColor" opacity=".7" />
      <rect x="8.5" y="8.5" width="6.5" height="6.5" fill="currentColor" opacity=".45" />
    </svg>
  );
}

export default function SignInScreen({ onContinue, interactionInFlight }: SignInScreenProps) {
  const [clicked, setClicked] = useState(false);
  const redirecting = clicked || interactionInFlight;

  const handleContinue = () => {
    setClicked(true);
    onContinue();
  };

  return (
    <main className="signin-screen">
      <SignInStatementPanel />
      <section className="signin-action" aria-labelledby="signin-heading">
        {/* Task E11/F02/US01/T01 (signin-1to1): the export's right column is
            headed "Sign in" (h2), not the "Contigo" h1 the lockup already
            says once per panel -- gap G-S1-RIGHT. */}
        <h2 id="signin-heading" className="screen-title">
          Sign in
        </h2>
        <p className="signin-subtitle">
          Your organisation account. Contigo never stores a password.
        </p>
        <button
          type="button"
          className="btn btn-primary btn-block"
          onClick={handleContinue}
          disabled={redirecting}
        >
          {redirecting ? <span className="signin-spinner" aria-hidden="true" /> : <MicrosoftMark />}
          {redirecting ? "Redirecting to login.microsoftonline.com …" : "Continue with Microsoft Entra ID"}
        </button>
        <hr />
        <p className="micro-meta">
          Single sign-on (OIDC · PKCE). Tenant isolation is enforced before any data is loaded. No
          account? Ask your Workspace Admin for an invitation.
        </p>
      </section>
    </main>
  );
}
