import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

// vite.config.ts sets `test.css: false` -- CSS imports are a no-op under
// jsdom here, so there is no real cascade/computed style to assert against
// at render time (see tests/styles/semantics.test.ts's own header comment
// for the equivalent reasoning on the *logic* side of the design system).
// This suite proves task E06/F06/US01/T01's fix the only way it is
// checkable in this harness: the CSS source itself. It is a regression
// guard against the E01 OIDC-scaffold rule (`main, .startup-error {
// max-width: 40rem; margin: 3rem auto; ... }`) reappearing, not a
// re-implementation of a CSS engine -- see each file's own header comment
// for the full diagnosis.
//
// Comments are stripped before any assertion runs: several of the files
// under test now *quote* that old rule verbatim in a `/* ... */` explaining
// why it is gone, which would otherwise trip these same regexes on prose,
// not a live rule.
function readSource(relativePath: string): string {
  const raw = readFileSync(fileURLToPath(new URL(relativePath, import.meta.url)), "utf-8");
  return raw.replace(/\/\*[\s\S]*?\*\//g, "");
}

/** First non-nested `selector { ... }` block for `selector` (i.e. not one nested inside an `@media` block further down the file) -- enough for every selector this suite checks, all of which are declared before any `@media` override of the same selector. `selector` is a plain CSS selector (e.g. `.shell-main`); this escapes it. */
function ruleBodyFor(css: string, selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const match = css.match(new RegExp(`${escaped}\\s*\\{([^}]*)\\}`));
  if (!match) {
    throw new Error(`No "${selector} { ... }" rule found.`);
  }
  return match[1];
}

describe("index.css (E06/F06/US01/T01 -- no 40rem main wrapper)", () => {
  const css = readSource("../../src/index.css");

  it("does not select the bare `main` element anywhere (no ancestor width cap for any screen)", () => {
    // A regression would look like the old `main, .startup-error { ... }`
    // combined selector, or a stand-alone `main { max-width: ... }` rule.
    // `[^.\w-]` before "main" rules out matching `.signin-statement`,
    // `.shell-main`, or similar -- only the bare element selector counts.
    expect(css).not.toMatch(/(^|[^.\w-])main\b\s*[,{]/m);
  });

  it("keeps `.startup-error` narrow -- a one-off boot-config-failure alert, not a shipped mockup screen", () => {
    expect(ruleBodyFor(css, ".startup-error")).toMatch(/max-width:\s*40rem/);
  });
});

describe("shell.css (E06/F06/US01/T01 -- shell-main fills the rail's 1fr track)", () => {
  const css = readSource("../../src/components/shell/shell.css");

  it("does not cap `.shell-main`'s width (it must fill the grid's 1fr track, not float as a card inside it)", () => {
    const body = ruleBodyFor(css, ".shell-main");
    expect(body).toMatch(/max-width:\s*none/);
  });
});

describe("signin.css (E06/F06/US01/T01 -- full-viewport two-column sign-in canvas)", () => {
  const css = readSource("../../src/routes/signin/signin.css");

  it("makes .signin-screen a full-height two-column grid (not a fraction of a 40rem ancestor)", () => {
    const body = ruleBodyFor(css, ".signin-screen");
    expect(body).toMatch(/min-height:\s*100vh/);
    expect(body).toMatch(/grid-template-columns:\s*1fr\s+1fr/);
    expect(body).not.toMatch(/max-width/);
  });

  it("no longer defines a standalone narrow `.workspace-picker` card (the picker now shares .signin-screen/.signin-action)", () => {
    expect(css).not.toMatch(/\.workspace-picker\s*\{/);
  });
});

describe("documents.css (E06/F06/US01/T01 regression guard; V2 layout reconciled by E13/F09/US01/T03)", () => {
  const css = readSource("../../src/routes/documents/documents.css");

  // Task E13/F09/US01/T03 rebuilt /documents from V1's ~400px/1fr two-column grid (a persistent
  // dropzone column beside the table) to the V2 prototype's own stacked single-column layout
  // (`.documents-screen`: a centered onboarding block, then a full-width list with a slim
  // `.upload-dropzone--list` bar above the table -- screens-v2.md #3 describes one column
  // throughout, never a side-by-side pair) -- `.documents-columns` no longer exists anywhere in this
  // file. This suite's job is unchanged from the original E06/F06/US01/T01 fix this describe block
  // guards ("not crushed to a sliver by an ancestor width cap"), just re-pointed at the real V2
  // top-level container -- the same `max-width: none` shape the shell.css block above already checks
  // on `.shell-main`.
  it("does not cap the top-level `.documents-screen` container (fills the shell's content track, not a narrow column)", () => {
    const body = ruleBodyFor(css, ".documents-screen");
    expect(body).not.toMatch(/max-width/);
  });

  it("wraps long filenames at word/character-run boundaries, not one glyph per line", () => {
    // V2 moved this property off the raw `.document-status-table th/td:nth-child(1)` column-width
    // selector and onto the semantic content classes actually rendered inside that cell --
    // `DocumentStatusTable.tsx` renders every column-1 filename through one of these two, never bare
    // text directly on the `td` -- see tests/routes/documents/documents.css.test.ts for this task's
    // fuller export-fidelity coverage of the same selectors (including `.document-status-table-link`).
    expect(ruleBodyFor(css, ".upload-result-filename")).toMatch(/overflow-wrap:\s*anywhere/);
    expect(ruleBodyFor(css, ".document-status-table-filename")).toMatch(/overflow-wrap:\s*anywhere/);
  });
});
