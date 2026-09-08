import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

// Task E11/F05/US01/T01 (portfolio-mockup, gap G-PORT): vite.config.ts sets `test.css: false`, so
// component tests under jsdom never apply the real cascade -- there is no computed style to assert
// against at render time (see web/tests/routes/signin/signin.css.test.ts's own header comment for the
// same reasoning, and web/tests/styles/layout.test.ts's). This suite proves the DoD's "attention strip
// and critical-row rules exist and match export values" the only way that is checkable in this
// harness: the CSS source itself. It reads both this screen's own contracts.css (the additive
// overrides this task added, on top of components.css's shared rules) and components.css (the
// pre-existing shared rules this task verified against day1-demo.html's own compiled `<style>` block
// rather than changing) -- it lives next to this task's own screen files (not
// web/tests/styles/layout.test.ts, which epic-11's own F08 "mockup-lock regression tests" feature
// owns) to avoid two tasks writing the same file.
function readSource(relativePath: string): string {
  const raw = readFileSync(fileURLToPath(new URL(relativePath, import.meta.url)), "utf-8");
  return raw.replace(/\/\*[\s\S]*?\*\//g, "");
}

/** First non-nested `selector { ... }` rule body for the *exact* selector text given. */
function ruleBodyFor(css: string, selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const match = css.match(new RegExp(`${escaped}\\s*\\{([^}]*)\\}`));
  if (!match) {
    throw new Error(`No "${selector} { ... }" rule found.`);
  }
  return match[1];
}

describe("contracts.css (E11/F05/US01/T01 -- attention strip + table vs day1-demo.html)", () => {
  const css = readSource("../../../src/routes/contracts/contracts.css");

  it("reserves a 3px left bar on every attention cell, accent-coloured only once a bucket is urgent", () => {
    const base = ruleBodyFor(css, ".portfolio-screen .attention-cell");
    expect(base).toMatch(/border-left:\s*3px solid transparent/);

    const urgent = ruleBodyFor(css, ".portfolio-screen .attention-cell.is-urgent");
    expect(urgent).toMatch(/border-left-color:\s*var\(--color-accent\)/);

    const urgentNumber = ruleBodyFor(css, ".portfolio-screen .attention-cell.is-urgent .attention-number");
    expect(urgentNumber).toMatch(/color:\s*var\(--color-accent-700\)/);
  });

  it("keeps the 'High risk' bucket ink-coloured (never accent) even once it has a match -- attDef.map's own a.k==='risk' branch", () => {
    const riskBar = ruleBodyFor(css, ".portfolio-screen .attention-cell.is-risk-flagged");
    expect(riskBar).toMatch(/border-left-color:\s*var\(--color-neutral-500\)/);

    const riskNumber = ruleBodyFor(css, ".portfolio-screen .attention-cell.is-risk-flagged .attention-number");
    expect(riskNumber).toMatch(/color:\s*var\(--color-text\)/);
  });

  it("tints the selected bucket's background (gap G-PORT: components.css does not style .is-active for any strip)", () => {
    const active = ruleBodyFor(css, ".portfolio-screen .attention-cell.is-active");
    expect(active).toMatch(/background:\s*var\(--color-neutral-200\)/);
  });

  it("lays the number and label out side by side, baseline-aligned -- day1-demo.html's own flex row", () => {
    const headline = ruleBodyFor(css, ".portfolio-screen .attention-cell-headline");
    expect(headline).toMatch(/display:\s*flex/);
    expect(headline).toMatch(/align-items:\s*baseline/);
  });

  it('keeps the label at 13px/600/ink -- day1-demo.html\'s own <span style="font-weight:600;font-size:13px">', () => {
    const label = ruleBodyFor(css, ".portfolio-screen .attention-cell .attention-label");
    expect(label).toMatch(/font-size:\s*13px/);
    expect(label).toMatch(/font-weight:\s*var\(--weight-body-strong\)/);
  });

  it("gives the table its export instance values -- tabular-nums, 13px body, 8px-all-sides cell padding", () => {
    const table = ruleBodyFor(css, ".portfolio-table");
    expect(table).toMatch(/font-variant-numeric:\s*tabular-nums/);
    expect(table).toMatch(/font-size:\s*13px/);

    // Multi-selector rule (`.portfolio-table th,\n.portfolio-table td { padding: var(--space-2); }`)
    // -- ruleBodyFor's exact-selector match does not apply to a comma-joined selector list, so this
    // asserts on the literal rule text instead.
    expect(css).toMatch(/\.portfolio-table th,\s*\.portfolio-table td\s*\{\s*padding:\s*var\(--space-2\);\s*\}/);
  });

  it("letter-spaces the header with the correctly-valued token (components.css's own --tracking-table-header is 0.1em; the export's compiled .table th rule is literally 0.08em, same as --tracking-kicker)", () => {
    const header = ruleBodyFor(css, ".portfolio-table th");
    expect(header).toMatch(/letter-spacing:\s*var\(--tracking-kicker\)/);
  });
});

describe("components.css (shared rules this task verified against day1-demo.html's own compiled <style> block rather than changing)", () => {
  const css = readSource("../../../src/styles/components.css");

  it("keeps the table header 11px/uppercase (AC-2 'Table headers are 11px uppercase letter-spaced')", () => {
    const header = ruleBodyFor(css, ".table th");
    expect(header).toMatch(/font-size:\s*11px/);
    expect(header).toMatch(/text-transform:\s*uppercase/);
  });

  it("keeps the critical row tinted accent-100 with a 3px accent bar (AC-2 'critical rows use accent-100 + 3px left bar')", () => {
    const critical = ruleBodyFor(css, ".table tr.row-critical");
    expect(critical).toMatch(/background:\s*var\(--color-accent-100\)/);
    expect(critical).toMatch(/box-shadow:\s*inset 3px 0 0 var\(--color-accent\)/);
  });

  it("keeps the attention strip a full-width equal-cell grid with a big (26px/800) number (AC-1)", () => {
    const strip = ruleBodyFor(css, ".attention-strip");
    expect(strip).toMatch(/display:\s*grid/);
    expect(strip).toMatch(/grid-auto-columns:\s*1fr/);

    const number = ruleBodyFor(css, ".attention-cell .attention-number");
    expect(number).toMatch(/font-size:\s*26px/);
    expect(number).toMatch(/font-weight:\s*var\(--weight-heading\)/);
  });
});
