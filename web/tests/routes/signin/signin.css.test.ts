import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

// Task E11/F02/US01/T01 (signin-1to1): vite.config.ts sets `test.css: false`,
// so component tests under jsdom never apply the real cascade -- there is no
// computed style to assert against at render time (see
// web/tests/styles/layout.test.ts's own header comment for the same
// reasoning). This suite proves the fill/grid/type fixes for gaps
// G-S1-FILL/G-S1-TYPE (reports/audit/visual-fidelity-gaps.md) the only way
// they are checkable in this harness: the CSS source itself. It lives next
// to this task's own screen files (not web/tests/styles/layout.test.ts,
// which epic-11's own F08 "mockup-lock regression tests" feature owns) to
// avoid two tasks writing the same test file.
function readSource(relativePath: string): string {
  const raw = readFileSync(fileURLToPath(new URL(relativePath, import.meta.url)), "utf-8");
  return raw.replace(/\/\*[\s\S]*?\*\//g, "");
}

function ruleBodyFor(css: string, selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const match = css.match(new RegExp(`${escaped}\\s*\\{([^}]*)\\}`));
  if (!match) {
    throw new Error(`No "${selector} { ... }" rule found.`);
  }
  return match[1];
}

describe("signin.css (E11/F02/US01/T01 -- 1:1 fill/grid/type vs day1-demo.html)", () => {
  const css = readSource("../../../src/routes/signin/signin.css");

  it("fills the left panel with accent-100 and grids it with accent-200 at 48px (gap G-S1-FILL)", () => {
    const body = ruleBodyFor(css, ".signin-statement");
    expect(body).toMatch(/background-color:\s*var\(--color-accent-100\)/);
    expect(body).toMatch(/var\(--color-accent-200\)/);
    expect(body).toMatch(/48px 48px/);
    expect(body).toMatch(/justify-content:\s*space-between/);
  });

  it("types the north-star at clamp(30px, 3.8vw, 52px) (gap G-S1-TYPE)", () => {
    const body = ruleBodyFor(css, ".signin-north-star");
    expect(body).toMatch(/font-size:\s*clamp\(30px,\s*3\.8vw,\s*52px\)/);
    expect(body).toMatch(/line-height:\s*1\.02/);
    expect(body).toMatch(/letter-spacing:\s*-0\.02em/);
  });

  it("caps the right column at 520px (gap G-S1-RIGHT)", () => {
    const body = ruleBodyFor(css, ".signin-action");
    expect(body).toMatch(/max-width:\s*520px/);
  });
});
