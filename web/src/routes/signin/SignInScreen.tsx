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
// bundle -- ADR-020 "the pixel reference"). The "4 V1 jobs" grid's kicker
// words (Contract / Renewal / Savings / New purchase) are also verbatim from
// that export; two of the four bold companion words (cell 1 "Intelligence",
// cell 4 "Quote Check") were recovered unambiguously, matching the R1/R4
// release names in reports/architecture/ADR-020-web-screen-inventory.md.
// The other two ("Tracking", "Opportunities") could not be disambiguated
// from a reused inline-style pattern in the minified single-line export, so
// they are reconstructed from the same ADR's release vocabulary (R2
// Renewals, R3 Savings/SavingsOpportunity) rather than guessed as verbatim.
const V1_JOBS: ReadonlyArray<{ kicker: string; bold: string }> = [
  { kicker: "Contract", bold: "Intelligence" },
  { kicker: "Renewal", bold: "Tracking" },
  { kicker: "Savings", bold: "Opportunities" },
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
        <h1 id="signin-heading" className="screen-title">
          Contigo
        </h1>
        <button
          type="button"
          className="btn btn-primary btn-block"
          onClick={handleContinue}
          disabled={redirecting}
        >
          {redirecting && <span className="signin-spinner" aria-hidden="true" />}
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
