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

// Screen 1 (inputs/design/prototypes/screens.md #1 "Sign-in -> workspace"):
// "Left: statement panel (accent-100 ground with grid, north-star sentence,
// 4 V1 jobs). Right: 'Continue with Microsoft Entra ID' -> redirect state ->
// workspace list ... States: idle · redirecting (spinner) · workspace
// picker." This component renders the left panel plus the right panel's
// idle/redirecting states; ./WorkspacePickerScreen.tsx is the third state.
//
// North-star sentence and the OIDC/PKCE caption below are quoted verbatim
// from inputs/design/prototypes/day1-demo.html (the compiled Claude Design
// bundle -- ADR-020 "the pixel reference"). Task E11/F02/US01/T01
// re-extracted the raw `<!-- SIGN-IN -->` block byte-for-byte (it is a
// single ~6.5KB line in the compiled export, which is why the previous pass
// -- reading it through a line-oriented tool -- could only disambiguate two
// of the four bold companion words). All four kicker/bold pairs below are
// now copied verbatim: Contract, Renewal, and Savings all pair with the
// *same* bold word "Intelligence" in the export, not the three distinct
// words ("Intelligence" / "Tracking" / "Opportunities") the earlier guess
// reconstructed from ADR-020's release vocabulary. Gap G-S1-JOBS
// (reports/audit/visual-fidelity-gaps.md).
const V1_JOBS: ReadonlyArray<{ kicker: string; bold: string }> = [
  { kicker: "Contract", bold: "Intelligence" },
  { kicker: "Renewal", bold: "Intelligence" },
  { kicker: "Savings", bold: "Intelligence" },
  { kicker: "New purchase", bold: "Quote Check" },
];

// Task E06/F06/US01/T01 (full-bleed-layout): the left "statement panel" half
// of screen 1 is constant across all three of its named states (idle ·
// redirecting · workspace picker) -- verified against the compiled
// prototype's own markup (inputs/design/prototypes/day1-demo.html): the
// statement-panel `<div>` sits *outside* the `sc-if` blocks that switch the
// right panel's content. Extracted here so WorkspacePickerScreen.tsx can
// render "the same canvas" for its own states too, instead of falling back
// to a second, narrower, standalone layout -- ADR-018/019/020 never
// describe the workspace list as a separate screen, only a state of this
// one.
export function SignInStatementPanel() {
  return (
    <section className="signin-statement">
      <div className="signin-lockup">
        <span className="signin-lockup-mark" aria-hidden="true" />
        Contigo
      </div>
      <p className="signin-north-star">
        Contigo knows <span className="signin-accent">what we bought</span>,{" "}
        <span className="signin-accent">what we pay</span>,{" "}
        <span className="signin-accent">when we need to act</span>, and{" "}
        <span className="signin-accent">where we can save money</span>.
      </p>
      <dl className="signin-jobs">
        {V1_JOBS.map((job) => (
          <div className="signin-job" key={job.kicker}>
            <dt>{job.kicker}</dt>
            <dd>{job.bold}</dd>
          </div>
        ))}
      </dl>
    </section>
  );
}

// Microsoft's 4-square mark, quoted verbatim (viewBox + all four rects/
// opacities) from the compiled prototype's primary sign-in button
// (`<!-- SIGN-IN -->`, inputs/design/prototypes/day1-demo.html). `fill`
// (not the shared `.icon` stroke treatment in styles/base.css) is load-
// bearing here -- `.icon` sets `fill: none`, which would blank every rect.
// aria-hidden: the button's own text already names the action.
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
          Use your organisation account. Contigo never stores your password — identity is handled by
          Microsoft Entra ID.
        </p>
        <button
          type="button"
          className="btn btn-primary btn-block"
          onClick={handleContinue}
          disabled={redirecting}
        >
          {redirecting ? <span className="signin-spinner" aria-hidden="true" /> : <MicrosoftMark />}
          {redirecting ? "Redirecting to Microsoft Entra ID…" : "Continue with Microsoft Entra ID"}
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
