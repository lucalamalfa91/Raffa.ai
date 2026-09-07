import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

// Task E11/F08/US01/T01 (mockup-lock-tests, ADR-019): feature-08's own
// description is "Regression tests that fail if someone reintroduces 40rem,
// visible health text, or the pre-mockup sign-in type" -- this file is that
// net, reproducing task-01-mockup-lock-tests.md's five named checks (gap
// report reports/audit/visual-fidelity-gaps.md, OPEN rows under "Chrome",
// "Screen 1", and "Shell + Ask bar") in one place. Two of the five (the
// sign-in checks below) are deliberately also asserted in
// web/tests/routes/signin/signin.css.test.ts, written by the earlier task
// that fixed them (E11/F02/US01/T01) before this feature existed -- see that
// file's own header comment, which explicitly names this feature as the
// long-lived owner of the *consolidated* lock. Reproducing them here means a
// reviewer (or a future task) can read this one file and see the whole
// epic-11 chrome/type/health lock set without depending on whichever
// per-screen file happens to also assert the same value.
//
// vite.config.ts sets `test.css: false` -- there is no real CSS cascade
// under jsdom, so every check below reads CSS/JSX source text directly
// rather than asserting computed styles at render time (see
// web/tests/styles/layout.test.ts's own header comment for the identical
// reasoning). Comments are stripped before any assertion runs: several files
// under test *quote* an old, removed rule verbatim in a `/* ... */`
// explaining why it is gone, which would otherwise trip a naive regex on
// prose, not a live rule.
function readSource(relativePath: string): string {
  const raw = readFileSync(fileURLToPath(new URL(relativePath, import.meta.url)), "utf-8");
  return raw.replace(/\/\*[\s\S]*?\*\//g, "");
}

/** First non-nested `selector { ... }` block for `selector` -- same helper as web/tests/styles/layout.test.ts and web/tests/routes/signin/signin.css.test.ts (does not look past an `@media` override further down the file; no check below needs one). `selector` is a plain CSS selector (e.g. `.shell-layout`); this escapes it. */
function ruleBodyFor(css: string, selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const match = css.match(new RegExp(`${escaped}\\s*\\{([^}]*)\\}`));
  if (!match) {
    throw new Error(`No "${selector} { ... }" rule found.`);
  }
  return match[1];
}

describe("index.css -- <main> stays uncapped (AC-1, gap G-40REM)", () => {
  it("does not reintroduce a bare `main` selector (the old `main, .startup-error { max-width: 40rem; ... }` rule)", () => {
    const css = readSource("../../src/index.css");
    // Identical regex to web/tests/styles/layout.test.ts's own "does not
    // select the bare `main` element anywhere" -- reproduced here so this
    // file alone proves the whole task-01-mockup-lock-tests.md checklist
    // without a reader needing a second file for AC-1. `[^.\w-]` before
    // "main" rules out matching `.signin-statement`, `.shell-main`, or
    // similar -- only the bare element selector counts.
    expect(css).not.toMatch(/(^|[^.\w-])main\b\s*[,{]/m);
  });
});

describe("signin.css -- north-star type and statement ground (AC-2, gaps G-S1-TYPE/G-S1-FILL)", () => {
  const css = readSource("../../src/routes/signin/signin.css");

  it("keeps the north-star sentence fluid via clamp(30px, 3.8vw, 52px), not a fixed 32px", () => {
    const body = ruleBodyFor(css, ".signin-north-star");
    expect(body).toMatch(/font-size:\s*clamp\(30px,\s*3\.8vw,\s*52px\)/);
  });

  it("fills the statement panel with --color-accent-100 (not a plain/neutral ground)", () => {
    const body = ruleBodyFor(css, ".signin-statement");
    expect(body).toMatch(/background-color:\s*var\(--color-accent-100\)/);
  });
});

describe("shell.css -- rail stays a fixed 224px beside the fluid main track (gap G-SHELL)", () => {
  it("keeps .shell-layout's grid at 224px 1fr", () => {
    const css = readSource("../../src/components/shell/shell.css");
    const body = ruleBodyFor(css, ".shell-layout");
    expect(body).toMatch(/grid-template-columns:\s*224px\s+1fr/);
  });
});

describe("App.tsx / base.css -- health probe stays out of the visible canvas (AC-3, gap G-HEALTH)", () => {
  // DOM-level proof (render App, findByTestId, assert the class) already
  // lives in web/tests/App.test.tsx (task E11/F01/US01/T01) -- that file's
  // own mock ApiClient/MSAL setup is heavy (App reaches every screen from
  // there), so this suite proves the same regression the source-only way
  // the rest of this file already uses: the JSX still pairs the test id
  // with the hiding class, and the class itself still clips rather than
  // removing the node from the accessibility tree (`display: none` would
  // also drop it from a11y and break the "still exposes data-testid for
  // tests" requirement task-01-hide-health-lock-chrome.md names).
  it("keeps the health <p> both visually-hidden and test-id-addressable", () => {
    const appSource = readSource("../../src/App.tsx");
    const healthBlock = appSource.match(/const healthStatus = \(([\s\S]*?)\);/);
    if (!healthBlock) {
      throw new Error('No "const healthStatus = ( ... );" block found in App.tsx.');
    }
    expect(healthBlock[1]).toMatch(/data-testid="api-health-status"/);
    expect(healthBlock[1]).toMatch(/className="visually-hidden"/);
  });

  it("implements .visually-hidden as an accessible clip, never display:none", () => {
    const baseCss = readSource("../../src/styles/base.css");
    const body = ruleBodyFor(baseCss, ".visually-hidden");
    expect(body).not.toMatch(/display:\s*none/);
    expect(body).toMatch(/clip:\s*rect\(0,\s*0,\s*0,\s*0\)/);
  });
});
