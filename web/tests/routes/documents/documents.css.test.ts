import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

// Task E11/F04/US01/T01 (documents-mockup): vite.config.ts sets `test.css:
// false`, so component tests under jsdom never apply the real cascade --
// there is no computed style to assert against at render time (see
// web/tests/styles/layout.test.ts's own header comment for the same
// reasoning). This suite proves the grid/dropzone/strip/pipeline/result-card
// fixes for gap G-DOC (reports/audit/visual-fidelity-gaps.md) the only way
// they are checkable in this harness: the CSS source itself. It lives next
// to this task's own screen files (not web/tests/styles/layout.test.ts,
// which epic-11's own F08 "mockup-lock regression tests" feature owns) --
// same convention tests/routes/signin/signin.css.test.ts already set.
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

describe("documents.css (E11/F04/US01/T01 -- 1:1 grid/dropzone/pipeline/result-card vs day1-demo.html)", () => {
  const css = readSource("../../../src/routes/documents/documents.css");

  it("AC-1: two columns, exactly 400px dropzone + 1fr status (gap G-DOC)", () => {
    const body = ruleBodyFor(css, ".documents-columns");
    expect(body).toMatch(/grid-template-columns:\s*400px\s+1fr/);
  });

  it("AC-1: filenames wrap at word/character-run boundaries, not one glyph per line", () => {
    expect(ruleBodyFor(css, ".upload-result-filename")).toMatch(/overflow-wrap:\s*anywhere/);
    const documentColumnRule = css.match(
      /\.document-status-table th:nth-child\(1\),\s*\.document-status-table td:nth-child\(1\)\s*\{([^}]*)\}/,
    );
    expect(documentColumnRule).not.toBeNull();
    expect(documentColumnRule![1]).toMatch(/overflow-wrap:\s*anywhere/);
  });

  it("dropzone is dashed, not solid, and holds the export's 300px min-height", () => {
    const body = ruleBodyFor(css, ".upload-dropzone");
    expect(body).toMatch(/border:\s*2px dashed var\(--color-divider\)/);
    expect(body).toMatch(/min-height:\s*300px/);
  });

  it("dropzone title matches the export's 22px/1.1 heading, not a smaller default", () => {
    const body = ruleBodyFor(css, ".upload-dropzone-title");
    expect(body).toMatch(/font-size:\s*22px/);
    expect(body).toMatch(/line-height:\s*1\.1/);
  });

  it("dropzone title keeps its auto top-margin, so the 300px min-height box pins icon top / title+subtitle+buttons bottom instead of leaving dead space below the buttons", () => {
    const body = ruleBodyFor(css, ".upload-dropzone-title");
    expect(body).toMatch(/margin:\s*auto 0 0/);
  });

  it("formats/size/sources strip is offset 20px from the buttons row above it", () => {
    const body = ruleBodyFor(css, ".upload-strip");
    expect(body).toMatch(/margin-top:\s*20px/);
    expect(body).toMatch(/padding-top:\s*10px/);
  });

  it("pipeline rows sit 6px apart, each dot 10px from its label", () => {
    expect(ruleBodyFor(css, ".pipeline-list")).toMatch(/gap:\s*6px/);
    expect(ruleBodyFor(css, ".pipeline-stage")).toMatch(/gap:\s*10px/);
  });

  it("does not reintroduce a boxed .card on the upload result summary (ADR-019: recommendation/provenance only)", () => {
    // Structural guard, not just the TSX check (UploadResultCard.test.tsx):
    // proves the composite class itself carries no card-like border/background
    // of its own either, so a future edit can't half-restore the box.
    const body = ruleBodyFor(css, ".upload-result-card");
    expect(body).not.toMatch(/border|background/);
  });
});
