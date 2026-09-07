import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

// Task E11/F07/US01/T01 (review-mockup, gap G-REV): vite.config.ts sets `test.css: false`, so
// component tests under jsdom never apply the real cascade -- there is no computed style to assert
// against at render time (see web/tests/styles/layout.test.ts's own header comment, and
// web/tests/routes/signin/signin.css.test.ts, whose exact helpers this file reuses). This proves the
// list/legend/evidence-pane fixes for gap G-REV (reports/audit/visual-fidelity-gaps.md) the only way
// they are checkable in this harness: the CSS source itself. Lives next to this task's own screen
// files (not web/tests/styles/layout.test.ts, which epic-11's own F08 "mockup-lock regression tests"
// feature owns) to avoid two tasks writing the same test file.
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

describe("review.css (E11/F07/US01/T01 -- list/legend/evidence-pane vs day1-demo.html)", () => {
  const css = readSource("../../../../src/routes/contracts/review/review.css");

  it("no longer references the non-existent --space-5 token (scale skips 5/7)", () => {
    expect(css).not.toMatch(/--space-5/);
    expect(ruleBodyFor(css, ".review-screen")).toMatch(/gap:\s*var\(--space-6\)/);
    expect(ruleBodyFor(css, ".review-body")).toMatch(/gap:\s*var\(--space-6\)/);
  });

  it("wraps the list below the evidence pane under ~900px (AC-1)", () => {
    expect(css).toMatch(/@media \(max-width:\s*900px\)\s*\{\s*\.review-body\s*\{\s*flex-direction:\s*column/);
  });

  it("gives the field table fixed, non-equal column widths (export's own narrow-field/wide-value/auto/auto proportions)", () => {
    expect(css).toMatch(/\.review-field-table th:nth-child\(1\),\s*\.review-field-table td:nth-child\(1\)\s*\{\s*width:\s*20%/);
    expect(css).toMatch(/\.review-field-table th:nth-child\(2\),\s*\.review-field-table td:nth-child\(2\)\s*\{\s*width:\s*32%/);
    expect(css).toMatch(/\.review-field-table th:nth-child\(3\),\s*\.review-field-table td:nth-child\(3\)\s*\{\s*width:\s*16%/);
    expect(css).toMatch(/\.review-field-table th:nth-child\(4\),\s*\.review-field-table td:nth-child\(4\)\s*\{\s*width:\s*32%/);
  });

  it("truncates a long extracted value with an ellipsis instead of wrapping the row", () => {
    const body = ruleBodyFor(css, ".review-field-value");
    expect(body).toMatch(/overflow:\s*hidden/);
    expect(body).toMatch(/text-overflow:\s*ellipsis/);
    expect(body).toMatch(/white-space:\s*nowrap/);
  });

  it("sizes the legend's tag chips compactly, matching the export's inline padding:1px 6px;font-size:10px", () => {
    const body = ruleBodyFor(css, ".review-legend-item .tag");
    expect(body).toMatch(/padding:\s*1px 6px/);
    expect(body).toMatch(/font-size:\s*10px/);
  });

  it("sizes the in-row Accept/Correct buttons compactly, matching the export's inline font-size:12px;padding:4px 10px", () => {
    const body = ruleBodyFor(css, ".review-decision-actions .btn");
    expect(body).toMatch(/font-size:\s*12px/);
    expect(body).toMatch(/padding:\s*4px 10px/);
  });

  it("gives the evidence pane's highlighted-passage card the export's own paper/serif treatment", () => {
    const body = ruleBodyFor(css, ".review-evidence-passage");
    expect(body).toMatch(/background:\s*#fff/);
    expect(body).toMatch(/border:\s*1px solid var\(--color-divider\)/);
    expect(body).toMatch(/font-family:\s*Georgia,\s*serif/);
  });
});

describe("components.css .detail-pane (shared; Review's evidence pane reuses it verbatim)", () => {
  const css = readSource("../../../../src/styles/components.css");

  it("is 340-400px, --color-surface, with a 2px left rule (Definition of done)", () => {
    const body = ruleBodyFor(css, ".detail-pane");
    expect(body).toMatch(/min-width:\s*340px/);
    expect(body).toMatch(/max-width:\s*400px/);
    expect(body).toMatch(/background:\s*var\(--color-surface\)/);
    expect(body).toMatch(/border-left:\s*2px solid var\(--color-divider\)/);
  });
});
